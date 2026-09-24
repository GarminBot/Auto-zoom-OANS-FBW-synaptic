// OANS Auto-Zoom: standalone WASM module for Microsoft Flight Simulator 2024.
//
// Shortly after landing it brings up the airport map (OANS) on both navigation
// displays, zoomed in so that the airport and the own aircraft are clearly
// visible, in aircraft that do not do this on their own.
//
// How it works:
// - Every second the module requests TITLE, ATC MODEL, SIM ON GROUND,
//   GROUND VELOCITY and PLANE ALT ABOVE GROUND through SimConnect.
// - The loaded aircraft is recognised by its TITLE / ATC MODEL (see kProfiles).
// - A landing is a touchdown after a real flight phase (see kLanding*). Once it
//   is confirmed, the profile of the aircraft runs its steps, one per second.
// - It acts once per landing and never again until the next flight, so the
//   pilot keeps full control of the displays afterwards.
//
// Background and sources: docs/fbw-a380x-oans-zoom.md, docs/aircraft-profiles.md

#include <MSFS/Legacy/gauges.h>
#include <MSFS/MSFS.h>
#include <MSFS/MSFS_WindowsTypes.h>
#include <SimConnect.h>

#include <cstdio>
#include <cstring>

namespace {

// ---------------------------------------------------------------------------
// Landing detection
// ---------------------------------------------------------------------------

// The module only arms after the aircraft has been airborne this long, above this height.
// This skips short hops, bounces on the take-off roll and flights that start on the ground.
constexpr int kArmAfterAirborneTicks = 30;
constexpr double kArmMinAltitudeAglFt = 100.0;

// SIM ON GROUND must be true this many ticks (seconds) in a row to count as touchdown.
constexpr int kTouchdownConfirmTicks = 2;

// After the confirmed touchdown the OANS is brought up once the aircraft has slowed
// below this ground speed, or at the latest after this many seconds.
constexpr double kLandingTriggerGroundSpeedKts = 80.0;
constexpr int kLandingTriggerMaxDelayTicks = 15;

// ---------------------------------------------------------------------------
// Access to the aircraft: RPN (calculator code), like MobiFlight/HubHop presets
// ---------------------------------------------------------------------------

double readRpn(const char* code) {
  FLOAT64 value = 0;
  execute_calculator_code(code, &value, nullptr, nullptr);
  return value;
}

void runRpn(const char* code) {
  printf("[OansAutoZoom] %s\n", code);
  execute_calculator_code(code, nullptr, nullptr, nullptr);
}

// ---------------------------------------------------------------------------
// Aircraft profiles
// ---------------------------------------------------------------------------

struct AircraftProfile {
  const char* name;
  // The aircraft matches if TITLE or ATC MODEL contains one of these texts (nullptr-terminated).
  const char* const* keywords;
  // Brings up the OANS. Called once per tick with step = 0, 1, 2, ... until it returns true.
  bool (*showOans)(int step);
};

// --- FlyByWire A380X --------------------------------------------------------
// ND mode L:A32NX_EFIS_{L,R}_ND_MODE: 0 ROSE ILS, 1 ROSE VOR, 2 ROSE NAV, 3 ARC, 4 PLAN.
// The OANS is shown in ROSE NAV, ARC and PLAN when the range selector is on a ZOOM position.
// Range selector positions for A32NX.FCU_EFIS_{L,R}_RANGE_SET: 0..4 = ZOOM 0.2/0.5/1/2/5 NM,
// 5..11 = 10..640 NM. The L-vars are outputs of the FBW FCU, so only the events are used.
constexpr int kFbwModeArc = 3;
constexpr int kFbwZoomPosition = 3;  // ZOOM 2 NM

bool fbwA380xShowOans(int step) {
  static const char* const sides[] = {"L", "R"};
  char code[128];

  if (step == 0) {
    if (readRpn("(L:A32NX_OANS_AVAILABLE)") < 0.5) {
      printf("[OansAutoZoom] FBW A380X: OANS not available (Navigraph data or ARPT NAV reset), nothing to do\n");
      return true;
    }
    // ROSE ILS / ROSE VOR cannot show the OANS: switch to ARC first. The FCU shifts the
    // range position when the mode changes, so the range is set one tick later.
    for (const char* side : sides) {
      snprintf(code, sizeof(code), "(L:A32NX_EFIS_%s_ND_MODE)", side);
      if (readRpn(code) < 2) {
        snprintf(code, sizeof(code), "%d (>K:A32NX.FCU_EFIS_%s_MODE_SET)", kFbwModeArc, side);
        runRpn(code);
      }
    }
    return false;
  }

  for (const char* side : sides) {
    snprintf(code, sizeof(code), "%d (>K:A32NX.FCU_EFIS_%s_RANGE_SET)", kFbwZoomPosition, side);
    runRpn(code);
  }
  return true;
}

const char* const kFbwA380xKeywords[] = {"A380X", nullptr};

const AircraftProfile kProfiles[] = {
    {"FlyByWire A380X", kFbwA380xKeywords, fbwA380xShowOans},
};

const AircraftProfile* findProfile(const char* title, const char* atcModel) {
  for (const AircraftProfile& profile : kProfiles) {
    for (const char* const* keyword = profile.keywords; *keyword != nullptr; ++keyword) {
      if (std::strstr(title, *keyword) != nullptr || std::strstr(atcModel, *keyword) != nullptr) {
        return &profile;
      }
    }
  }
  return nullptr;
}

// ---------------------------------------------------------------------------
// SimConnect
// ---------------------------------------------------------------------------

enum DataDefinitionId : SIMCONNECT_DATA_DEFINITION_ID { DEF_AIRCRAFT = 0 };
enum DataRequestId : SIMCONNECT_DATA_REQUEST_ID { REQ_AIRCRAFT = 0 };
enum ClientEventId : SIMCONNECT_CLIENT_EVENT_ID { EVT_ONE_SECOND = 0, EVT_FLIGHT_LOADED = 1 };

// Order and types must match the SimConnect_AddToDataDefinition calls in module_init.
struct AircraftData {
  char title[256];
  char atcModel[256];
  double simOnGround;
  double groundVelocityKts;
  double altitudeAglFt;
};

HANDLE g_simConnect = 0;

// ---------------------------------------------------------------------------
// State machine
// ---------------------------------------------------------------------------

const AircraftProfile* g_profile = nullptr;
int g_airborneTicks = 0;
int g_groundTicks = 0;
bool g_armed = false;   // a real flight phase happened, the next landing counts
bool g_landed = false;  // touchdown confirmed, waiting for the trigger
int g_ticksSinceTouchdown = 0;
bool g_running = false;  // profile steps in progress
int g_step = 0;
int g_exceptionLogCount = 0;

void resetFlightState() {
  g_airborneTicks = 0;
  g_groundTicks = 0;
  g_armed = false;
  g_landed = false;
  g_ticksSinceTouchdown = 0;
  g_running = false;
  g_step = 0;
}

void onAircraftData(const AircraftData& data) {
  const AircraftProfile* profile = findProfile(data.title, data.atcModel);
  if (profile != g_profile) {
    g_profile = profile;
    resetFlightState();
    printf("[OansAutoZoom] aircraft \"%s\": %s\n", data.title, profile != nullptr ? profile->name : "not supported");
  }
  if (g_profile == nullptr) {
    return;
  }

  if (g_running) {
    g_running = !g_profile->showOans(g_step++);
    return;
  }

  const bool onGround = data.simOnGround > 0.5;
  if (!onGround) {
    g_groundTicks = 0;
    g_landed = false;  // touch and go: wait for the next landing
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

  if (!g_landed) {
    if (++g_groundTicks < kTouchdownConfirmTicks) {
      return;
    }
    g_landed = true;
    g_ticksSinceTouchdown = g_groundTicks - 1;
    printf("[OansAutoZoom] touchdown confirmed\n");
  } else {
    ++g_ticksSinceTouchdown;
  }

  if (data.groundVelocityKts <= kLandingTriggerGroundSpeedKts ||
      g_ticksSinceTouchdown >= kLandingTriggerMaxDelayTicks) {
    printf("[OansAutoZoom] %s: bringing up the OANS (%.0f kt, %d s after touchdown)\n", g_profile->name,
           data.groundVelocityKts, g_ticksSinceTouchdown);
    g_armed = false;
    g_landed = false;
    g_step = 0;
    g_running = !g_profile->showOans(g_step++);
  }
}

void CALLBACK dispatchProc(SIMCONNECT_RECV* pData, DWORD cbData, void* pContext) {
  (void)cbData;
  (void)pContext;
  switch (pData->dwID) {
    case SIMCONNECT_RECV_ID_EVENT: {
      const auto* event = static_cast<SIMCONNECT_RECV_EVENT*>(pData);
      if (event->uEventID == EVT_ONE_SECOND) {
        // Requested anew every second, so it keeps working across flights and aircraft changes.
        SimConnect_RequestDataOnSimObject(g_simConnect, REQ_AIRCRAFT, DEF_AIRCRAFT, SIMCONNECT_OBJECT_ID_USER,
                                          SIMCONNECT_PERIOD_ONCE);
      } else if (event->uEventID == EVT_FLIGHT_LOADED) {
        // A new flight starts from scratch, e.g. at a gate after quitting the last one in the air.
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
  track(SimConnect_AddToDataDefinition(g_simConnect, DEF_AIRCRAFT, "ATC MODEL", nullptr,
                                       SIMCONNECT_DATATYPE_STRING256));
  track(SimConnect_AddToDataDefinition(g_simConnect, DEF_AIRCRAFT, "SIM ON GROUND", "Bool",
                                       SIMCONNECT_DATATYPE_FLOAT64));
  track(SimConnect_AddToDataDefinition(g_simConnect, DEF_AIRCRAFT, "GROUND VELOCITY", "Knots",
                                       SIMCONNECT_DATATYPE_FLOAT64));
  track(SimConnect_AddToDataDefinition(g_simConnect, DEF_AIRCRAFT, "PLANE ALT ABOVE GROUND", "Feet",
                                       SIMCONNECT_DATATYPE_FLOAT64));
  track(SimConnect_SubscribeToSystemEvent(g_simConnect, EVT_ONE_SECOND, "1sec"));
  track(SimConnect_SubscribeToSystemEvent(g_simConnect, EVT_FLIGHT_LOADED, "FlightLoaded"));
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
