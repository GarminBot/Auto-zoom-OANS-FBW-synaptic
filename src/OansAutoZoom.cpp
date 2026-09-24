// OANS Auto-Zoom: standalone WASM module for Microsoft Flight Simulator 2024.
//
// Right after touchdown it brings up the airport map on both navigation displays,
// zoomed in so that the airport and the own aircraft are clearly visible - the way
// the iniBuilds A380 does it ("OANS Auto Zoom") and the way the real A350 does it
// ("At landing, the ND automatically displays the ANF in ARC mode, with a 2 NM
// range", FCOM DSC-34-NAV-80-20-10). Supported aircraft: FlyByWire A380X,
// iniBuilds A350, Synaptic A220 (see kProfiles).
//
// How it works:
// - Once per second (counted with the SimConnect "Frame" event) the module requests
//   TITLE, SIM ON GROUND, GROUND VELOCITY and PLANE ALT ABOVE GROUND; the aircraft.cfg
//   path comes with the AircraftLoaded event.
// - The aircraft is recognised by a keyword in its title or aircraft.cfg path.
// - A landing is a touchdown after a real flight phase. Once it is confirmed, the
//   profile of the aircraft brings up the map, once. Afterwards the displays are left
//   alone until the next flight; nothing happens on take-off.
// - Commands are RPN (calculator code), the same mechanism MobiFlight/HubHop presets
//   use. They are sent one per simulator frame, so no knob detent gets lost.
//
// Background and sources: docs/aircraft-profiles.md

#include <MSFS/Legacy/gauges.h>
#include <MSFS/MSFS.h>
#include <MSFS/MSFS_WindowsTypes.h>
#include <SimConnect.h>

#include <cstdarg>
#include <cstdio>
#include <cstring>

namespace {

// ---------------------------------------------------------------------------
// Landing detection
// ---------------------------------------------------------------------------

// The module only arms after the aircraft has been airborne this long (seconds), above this
// height. This skips flights that start on the ground and hops during an aborted take-off.
constexpr int kArmAfterAirborneTicks = 15;
constexpr double kArmMinAltitudeAglFt = 100.0;

// SIM ON GROUND must be true this many seconds in a row to count as the landing. Similar to
// Airbus' own "nose gear down or 5 s after main gear down"; a bounce resets the count.
constexpr int kTouchdownConfirmTicks = 3;

// A landing starts at flying speed. Being put on the ground (slew, moving the aircraft to a
// gate) is no landing and disarms the module.
constexpr double kMinTouchdownGroundSpeedKts = 40.0;

// ---------------------------------------------------------------------------
// Commands to the aircraft
// ---------------------------------------------------------------------------

constexpr int kQueueCapacity = 256;
constexpr int kMaxCommandLength = 96;

char g_queue[kQueueCapacity][kMaxCommandLength];
int g_queueHead = 0;
int g_queueCount = 0;

// Queues one RPN command; the queue is drained one command per simulator frame.
void queueCommand(const char* format, ...) {
  if (g_queueCount == kQueueCapacity) {
    printf("[OansAutoZoom] command queue full, dropping \"%s\"\n", format);
    return;
  }
  va_list args;
  va_start(args, format);
  vsnprintf(g_queue[(g_queueHead + g_queueCount) % kQueueCapacity], kMaxCommandLength, format, args);
  va_end(args);
  ++g_queueCount;
}

void sendNextQueuedCommand() {
  if (g_queueCount == 0) {
    return;
  }
  const char* command = g_queue[g_queueHead];
  printf("[OansAutoZoom] %s\n", command);
  execute_calculator_code(command, nullptr, nullptr, nullptr);
  g_queueHead = (g_queueHead + 1) % kQueueCapacity;
  --g_queueCount;
}

void clearQueue() {
  g_queueHead = 0;
  g_queueCount = 0;
}

double readLVar(const char* name) {
  char code[kMaxCommandLength];
  snprintf(code, sizeof(code), "(L:%s)", name);
  FLOAT64 value = 0;
  execute_calculator_code(code, &value, nullptr, nullptr);
  return value;
}

// True if the aircraft has registered this L-var. Does not create it.
bool lvarExists(const char* name) {
  return check_named_variable(name) >= 0;
}

// ---------------------------------------------------------------------------
// Aircraft profiles
// ---------------------------------------------------------------------------

struct AircraftProfile {
  const char* name;
  // Matches if the TITLE or the aircraft.cfg path contains this text (case-insensitive).
  const char* keyword;
  // Brings up the map. Called once per second with step = 0, 1, 2, ... until it returns true.
  bool (*showAirportMap)(int step);
};

// Airbus profiles follow the A350 FCOM: the ND goes to ARC mode with the ZOOM range 2 NM.
// A ZOOM range that is already selected (by the pilot, or by the aircraft's own logic) is kept.

// --- FlyByWire A380X --------------------------------------------------------
// See docs/fbw-a380x-oans-zoom.md. ND mode L:A32NX_EFIS_{L,R}_ND_MODE: 0 ROSE ILS, 1 ROSE VOR,
// 2 ROSE NAV, 3 ARC, 4 PLAN. Range L:A32NX_EFIS_{L,R}_ND_RANGE: 0 = ZOOM, 1..7 = 10..640 NM.
// A32NX.FCU_EFIS_{L,R}_RANGE_SET: 0..4 = ZOOM 0.2/0.5/1/2/5 NM, 5..11 = 10..640 NM.
// The L-vars are outputs of FBW's FCU simulation, so only its events are used to change them.
// Entering ARC never moves a ZOOM position, so mode and range can be sent back to back.
constexpr int kFbwModeArc = 3;
constexpr int kFbwZoomPosition = 3;  // ZOOM 2 NM

void fbwQueueSide(const char* side) {
  char name[64];
  snprintf(name, sizeof(name), "A32NX_EFIS_%s_ND_MODE", side);
  if (readLVar(name) != kFbwModeArc) {
    queueCommand("%d (>K:A32NX.FCU_EFIS_%s_MODE_SET)", kFbwModeArc, side);
  }
  snprintf(name, sizeof(name), "A32NX_EFIS_%s_ND_RANGE", side);
  if (readLVar(name) != 0) {
    queueCommand("%d (>K:A32NX.FCU_EFIS_%s_RANGE_SET)", kFbwZoomPosition, side);
  }
}

bool fbwA380xShowAirportMap(int step) {
  if (step == 0) {
    if (readLVar("A32NX_OANS_AVAILABLE") < 0.5) {
      printf("[OansAutoZoom] FBW A380X: OANS not available (Navigraph or ARPT NAV reset), nothing to do\n");
      return true;
    }
    fbwQueueSide("L");
    return false;
  }
  fbwQueueSide("R");
  return true;
}

// --- iniBuilds A350 ---------------------------------------------------------
// ND mode L:INI_MAP_MODE_{CAPT,FO}_SWITCH: 0 LS, 1 VOR, 2 NAV, 3 ARC, 4 PLAN; the airport map
// (ANF) shows in NAV, ARC and PLAN on a ZOOM range. Range knob L:INI_MAP_RANGE_{CAPT,FO}_SWITCH:
// 0..4 = ZOOM 0.2/0.5/1/2/5 NM, 5..11 = 10..640 NM; the aircraft follows direct writes.
// iniBuilds' own L-var list spells the range knob INI_MAP_MODE_RANGE_{CAPT,FO}_SWITCH, so the
// name the loaded aircraft actually uses is looked up first.
// Other iniBuilds aircraft use the same names with a different scale; the profile only runs
// when the A350 has been recognised.
constexpr int kA350ModeArc = 3;
constexpr int kA350ZoomPosition = 3;  // ZOOM 2 NM

void a350QueueSide(const char* side) {
  char modeVar[64];
  char rangeVar[64];
  snprintf(modeVar, sizeof(modeVar), "INI_MAP_MODE_%s_SWITCH", side);
  snprintf(rangeVar, sizeof(rangeVar), "INI_MAP_RANGE_%s_SWITCH", side);
  if (!lvarExists(rangeVar)) {
    snprintf(rangeVar, sizeof(rangeVar), "INI_MAP_MODE_RANGE_%s_SWITCH", side);
  }
  if (!lvarExists(modeVar) || !lvarExists(rangeVar)) {
    printf("[OansAutoZoom] iniBuilds A350: EFIS L-vars for %s not found, nothing to do\n", side);
    return;
  }
  if (readLVar(modeVar) != kA350ModeArc) {
    queueCommand("%d (>L:%s)", kA350ModeArc, modeVar);
  }
  if (readLVar(rangeVar) > 4) {  // 0..4 = a ZOOM range is already selected
    queueCommand("%d (>L:%s)", kA350ZoomPosition, rangeVar);
  }
}

bool iniA350ShowAirportMap(int step) {
  // The first officer's side follows two seconds later: loading the map on both NDs at
  // the same moment has crashed the A350 in the past (fixed in v1.0.5).
  if (step == 0) {
    a350QueueSide("CAPT");
    return false;
  }
  if (step < 2) {
    return false;
  }
  a350QueueSide("FO");
  return true;
}

// --- Synaptic A220 ----------------------------------------------------------
// The MAP range knob of each Control Tuning Panel fires H:A220_CTP_RANGE_{1,2}_{INC,DEC}
// once per detent (1 = captain, 2 = first officer). Below 2 NM on the ground the MAP shows the
// airport moving map; its ranges are 1000 FT, 2000 FT, 3000 FT and 1 NM. The aircraft publishes
// the selected range nowhere, so the knob is turned to the smallest range first and then three
// detents up to 1 NM, the widest airport map range. The airport moving map itself needs
// Synaptic A220 v1.0.10 or newer; older versions show AIRPORT MAP FAULT at these ranges.
constexpr int kA220DetentsToSmallestRange = 30;
constexpr int kA220DetentsSmallestTo1Nm = 3;

void a220QueueSide(int ctp) {
  for (int i = 0; i < kA220DetentsToSmallestRange; ++i) {
    queueCommand("(>H:A220_CTP_RANGE_%d_DEC)", ctp);
  }
  for (int i = 0; i < kA220DetentsSmallestTo1Nm; ++i) {
    queueCommand("(>H:A220_CTP_RANGE_%d_INC)", ctp);
  }
}

bool synapticA220ShowAirportMap(int step) {
  if (step == 0) {
    a220QueueSide(1);
    return false;
  }
  a220QueueSide(2);
  return true;
}

const AircraftProfile kProfiles[] = {
    {"FlyByWire A380X", "a380x", fbwA380xShowAirportMap},
    {"iniBuilds A350", "a350", iniA350ShowAirportMap},
    {"Synaptic A220", "a220", synapticA220ShowAirportMap},
};

// Case-insensitive search; keyword must be lower-case letters, digits or '_'.
const char* findIgnoreCase(const char* text, const char* keyword) {
  const size_t keywordLength = std::strlen(keyword);
  for (; *text != '\0'; ++text) {
    size_t i = 0;
    while (i < keywordLength && text[i] != '\0' && (text[i] | 0x20) == (keyword[i] | 0x20)) {
      ++i;
    }
    if (i == keywordLength) {
      return text;
    }
  }
  return nullptr;
}

const AircraftProfile* findProfile(const char* title, const char* aircraftPath) {
  // Only the part from "SimObjects" on names the aircraft; the folders above it are the user's.
  const char* simObjects = findIgnoreCase(aircraftPath, "simobjects");
  const char* aircraftFolders = simObjects != nullptr ? simObjects : aircraftPath;
  for (const AircraftProfile& profile : kProfiles) {
    if (findIgnoreCase(title, profile.keyword) != nullptr || findIgnoreCase(aircraftFolders, profile.keyword) != nullptr) {
      return &profile;
    }
  }
  return nullptr;
}

// ---------------------------------------------------------------------------
// SimConnect
// ---------------------------------------------------------------------------

enum DataDefinitionId : SIMCONNECT_DATA_DEFINITION_ID { DEF_AIRCRAFT = 0 };
enum DataRequestId : SIMCONNECT_DATA_REQUEST_ID { REQ_AIRCRAFT = 0 };
enum ClientEventId : SIMCONNECT_CLIENT_EVENT_ID {
  EVT_FRAME = 0,
  EVT_FLIGHT_LOADED = 1,
  EVT_AIRCRAFT_LOADED = 2,
};

// Order and types must match the SimConnect_AddToDataDefinition calls in module_init.
struct AircraftData {
  char title[256];
  double simOnGround;
  double groundVelocityKts;
  double altitudeAglFt;
};

HANDLE g_simConnect = 0;
char g_aircraftPath[MAX_PATH] = "";
float g_secondsSinceDataRequest = 0;

// ---------------------------------------------------------------------------
// State machine
// ---------------------------------------------------------------------------

const AircraftProfile* g_profile = nullptr;
int g_airborneTicks = 0;
int g_groundTicks = 0;
bool g_armed = false;    // a real flight phase happened, the next landing counts
bool g_running = false;  // profile steps in progress
int g_step = 0;
int g_exceptionLogCount = 0;

void resetFlightState() {
  g_airborneTicks = 0;
  g_groundTicks = 0;
  g_armed = false;
  g_running = false;
  g_step = 0;
  clearQueue();
}

void onAircraftData(const AircraftData& data) {
  const AircraftProfile* profile = findProfile(data.title, g_aircraftPath);
  if (profile != g_profile) {
    g_profile = profile;
    resetFlightState();
    printf("[OansAutoZoom] aircraft \"%s\": %s\n", data.title, profile != nullptr ? profile->name : "not supported");
  }
  if (g_profile == nullptr) {
    return;
  }

  if (g_running) {
    g_running = !g_profile->showAirportMap(g_step++);
    return;
  }

  const bool onGround = data.simOnGround > 0.5;
  if (!onGround) {
    g_groundTicks = 0;  // bounce: the landing is confirmed only after staying on the ground
    if (data.altitudeAglFt > kArmMinAltitudeAglFt) {
      ++g_airborneTicks;
    }
    if (!g_armed && g_airborneTicks >= kArmAfterAirborneTicks) {
      g_armed = true;
      printf("[OansAutoZoom] airborne: armed for the next landing\n");
    }
    return;
  }

  g_airborneTicks = 0;
  if (!g_armed) {
    return;
  }
  if (++g_groundTicks == 1 && data.groundVelocityKts < kMinTouchdownGroundSpeedKts) {
    printf("[OansAutoZoom] on the ground at %.0f kt: not a landing, disarmed\n", data.groundVelocityKts);
    g_armed = false;
    return;
  }
  if (g_groundTicks < kTouchdownConfirmTicks) {
    return;
  }

  printf("[OansAutoZoom] %s: landing at %.0f kt, bringing up the airport map\n", g_profile->name,
         data.groundVelocityKts);
  g_armed = false;
  g_step = 0;
  g_running = !g_profile->showAirportMap(g_step++);
}

void CALLBACK dispatchProc(SIMCONNECT_RECV* pData, DWORD cbData, void* pContext) {
  (void)cbData;
  (void)pContext;
  switch (pData->dwID) {
    case SIMCONNECT_RECV_ID_EVENT_FRAME: {
      const auto* frame = static_cast<SIMCONNECT_RECV_EVENT_FRAME*>(pData);
      sendNextQueuedCommand();
      g_secondsSinceDataRequest += frame->fFrameRate > 1.0f ? 1.0f / frame->fFrameRate : 1.0f;
      if (g_secondsSinceDataRequest >= 0.999f) {
        g_secondsSinceDataRequest = 0;
        // Requested anew every second, so it keeps working across flights and aircraft changes.
        SimConnect_RequestDataOnSimObject(g_simConnect, REQ_AIRCRAFT, DEF_AIRCRAFT, SIMCONNECT_OBJECT_ID_USER,
                                          SIMCONNECT_PERIOD_ONCE);
      }
      break;
    }
    case SIMCONNECT_RECV_ID_EVENT_FILENAME: {
      // AircraftLoaded and FlightLoaded both come with a file name.
      const auto* event = static_cast<SIMCONNECT_RECV_EVENT_FILENAME*>(pData);
      if (event->uEventID == EVT_AIRCRAFT_LOADED) {
        snprintf(g_aircraftPath, sizeof(g_aircraftPath), "%s", event->szFileName);
        printf("[OansAutoZoom] aircraft loaded: %s\n", g_aircraftPath);
      }
      if (event->uEventID == EVT_AIRCRAFT_LOADED || event->uEventID == EVT_FLIGHT_LOADED) {
        // A new aircraft or flight starts from scratch, e.g. at a gate after quitting the last
        // flight in the air; commands still queued for the previous aircraft are dropped.
        g_profile = nullptr;
        resetFlightState();
      }
      break;
    }
    case SIMCONNECT_RECV_ID_SIMOBJECT_DATA: {
      const auto* objectData = static_cast<SIMCONNECT_RECV_SIMOBJECT_DATA*>(pData);
      if (objectData->dwRequestID == REQ_AIRCRAFT) {
        onAircraftData(*reinterpret_cast<const AircraftData*>(&objectData->dwData));
      }
      break;
    }
    case SIMCONNECT_RECV_ID_EXCEPTION: {
      // Expected while no flight is loaded (no user aircraft); only log the first few.
      if (g_exceptionLogCount < 5) {
        ++g_exceptionLogCount;
        const auto* exception = static_cast<SIMCONNECT_RECV_EXCEPTION*>(pData);
        printf("[OansAutoZoom] SimConnect exception %u\n", static_cast<unsigned>(exception->dwException));
      }
      break;
    }
    default:
      break;
  }
}

}  // namespace

// ---------------------------------------------------------------------------
// Entry points of a standalone WASM module
// ---------------------------------------------------------------------------

extern "C" MSFS_CALLBACK void module_init(void) {
  if (FAILED(SimConnect_Open(&g_simConnect, "OansAutoZoom", nullptr, 0, 0, 0))) {
    printf("[OansAutoZoom] SimConnect_Open failed\n");
    return;
  }

  HRESULT result = S_OK;
  auto track = [&result](HRESULT hr) {
    if (FAILED(hr)) {
      result = hr;
    }
  };

  track(SimConnect_AddToDataDefinition(g_simConnect, DEF_AIRCRAFT, "TITLE", nullptr, SIMCONNECT_DATATYPE_STRING256));
  track(SimConnect_AddToDataDefinition(g_simConnect, DEF_AIRCRAFT, "SIM ON GROUND", "Bool",
                                       SIMCONNECT_DATATYPE_FLOAT64));
  track(SimConnect_AddToDataDefinition(g_simConnect, DEF_AIRCRAFT, "GROUND VELOCITY", "Knots",
                                       SIMCONNECT_DATATYPE_FLOAT64));
  track(SimConnect_AddToDataDefinition(g_simConnect, DEF_AIRCRAFT, "PLANE ALT ABOVE GROUND", "Feet",
                                       SIMCONNECT_DATATYPE_FLOAT64));
  track(SimConnect_SubscribeToSystemEvent(g_simConnect, EVT_FRAME, "Frame"));
  track(SimConnect_SubscribeToSystemEvent(g_simConnect, EVT_FLIGHT_LOADED, "FlightLoaded"));
  track(SimConnect_SubscribeToSystemEvent(g_simConnect, EVT_AIRCRAFT_LOADED, "AircraftLoaded"));
  // In a WASM module a single call is enough; afterwards the sim calls dispatchProc for every message.
  track(SimConnect_CallDispatch(g_simConnect, dispatchProc, nullptr));

  printf("[OansAutoZoom] %s\n", SUCCEEDED(result) ? "initialised" : "initialisation incomplete");
}

extern "C" MSFS_CALLBACK void module_deinit(void) {
  if (g_simConnect != 0) {
    SimConnect_Close(g_simConnect);
    g_simConnect = 0;
  }
}
