// OANS Auto-Zoom: standalone WASM module for Microsoft Flight Simulator 2024.
//
// Right after touchdown it brings up the airport map on both navigation displays,
// zoomed in so that the airport and the own aircraft are clearly visible - the way
// the iniBuilds A380 does it ("OANS Auto Zoom") and the way the real A350 does it
// ("At landing, the ND automatically displays the ANF in ARC mode, with a 2 NM
// range", FCOM DSC-34-NAV-80-20-10). The zoom is 0.5 NM rather than 2 NM: tested in
// the sim, 2 NM was two detents too wide. Supported aircraft: FlyByWire A380X,
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
//   use. They are sent at most one per simulator frame; the A220's knob detents at the
//   pace of a hand on the knob (see its profile).
// - Everything the module decides is written to the developer console and to
//   oans_autozoom.log in the module's work folder (see kLogPath).
//
// Background and sources: docs/aircraft-profiles.md

#include <MSFS/Legacy/gauges.h>
#include <MSFS/MSFS.h>
#include <MSFS/MSFS_WindowsTypes.h>
#include <SimConnect.h>

#include <cstdarg>
#include <cstdio>
#include <cstring>

// The module's private, writable folder. On disk (MSFS 2024, Microsoft Store):
// %LOCALAPPDATA%\Packages\Microsoft.Limitless_8wekyb3d8bbwe\LocalState\WASM\MSFS2024\<package folder>\work
// Steam: %APPDATA%\Microsoft Flight Simulator 2024\WASM\MSFS2024\<package folder>\work
#ifndef OANS_LOG_PATH
#define OANS_LOG_PATH "\\work\\oans_autozoom.log"
#endif

namespace {

// ---------------------------------------------------------------------------
// Log
// ---------------------------------------------------------------------------

constexpr const char* kVersion = "1.4.0";
constexpr const char* kLogPath = OANS_LOG_PATH;
double g_secondsSinceStart = 0;  // simulator time since the module was loaded, from the Frame event

// One line to the developer console and to the log file. The file is started anew at every
// simulator start and only gets a few lines per flight.
void logLine(const char* format, ...) {
  char line[640];
  va_list args;
  va_start(args, format);
  vsnprintf(line, sizeof(line), format, args);
  va_end(args);
  printf("[OansAutoZoom] %s\n", line);
  if (FILE* file = fopen(kLogPath, "a")) {
    fprintf(file, "[%8.1f s] %s\n", g_secondsSinceStart, line);
    fclose(file);
  }
}

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

struct QueuedCommand {
  float delaySeconds;  // how long after the previous command this one goes out
  char text[kMaxCommandLength];
};

QueuedCommand g_queue[kQueueCapacity];
int g_queueHead = 0;
int g_queueCount = 0;
double g_secondsSinceCommand = 0;
// Set when a queued sequence ends by setting an L-var back that it set at its start (the A220's
// hold on AMDB's map). If the queue is dropped half way, that last command is sent right away.
const char* g_queueCleanupCommand = nullptr;

void queueCommandV(float delaySeconds, const char* format, va_list args) {
  if (g_queueCount == kQueueCapacity) {
    logLine("command queue full, dropping \"%s\"", format);
    return;
  }
  QueuedCommand& entry = g_queue[(g_queueHead + g_queueCount) % kQueueCapacity];
  entry.delaySeconds = delaySeconds;
  vsnprintf(entry.text, sizeof(entry.text), format, args);
  ++g_queueCount;
}

// Queues one RPN command for the next simulator frame; the queue sends at most one per frame.
void queueCommand(const char* format, ...) {
  va_list args;
  va_start(args, format);
  queueCommandV(0, format, args);
  va_end(args);
}

// Queues one RPN command that goes out delaySeconds after the previous one.
void queueCommandAfter(float delaySeconds, const char* format, ...) {
  va_list args;
  va_start(args, format);
  queueCommandV(delaySeconds, format, args);
  va_end(args);
}

// Called once per simulator frame with the frame's duration.
void sendNextQueuedCommand(double frameSeconds) {
  g_secondsSinceCommand += frameSeconds;
  if (g_queueCount == 0) {
    return;
  }
  const QueuedCommand& next = g_queue[g_queueHead];
  if (g_secondsSinceCommand < next.delaySeconds) {
    return;
  }
  g_secondsSinceCommand = 0;
  logLine("send %s", next.text);
  execute_calculator_code(next.text, nullptr, nullptr, nullptr);
  g_queueHead = (g_queueHead + 1) % kQueueCapacity;
  --g_queueCount;
  if (g_queueCount == 0) {
    g_queueCleanupCommand = nullptr;  // the sequence ran to its end
  }
}

void clearQueue() {
  if (g_queueCount > 0 && g_queueCleanupCommand != nullptr) {
    logLine("sequence dropped, send %s", g_queueCleanupCommand);
    execute_calculator_code(g_queueCleanupCommand, nullptr, nullptr, nullptr);
  }
  g_queueCleanupCommand = nullptr;
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
  // Matches if the TITLE or the aircraft.cfg path contains one of these texts (case-insensitive).
  const char* keywords[2];
  // Brings up the map. Called once per second with step = 0, 1, 2, ... until it returns true.
  bool (*showAirportMap)(int step);
};

// Airbus profiles follow the A350 FCOM: the ND goes to ARC mode with the ZOOM range 2 NM.
// A ZOOM range that is already selected (by the pilot, or by the aircraft's own logic) is kept.

// --- FlyByWire A380X --------------------------------------------------------
// See docs/fbw-a380x-oans-zoom.md. ND mode L:A32NX_EFIS_{L,R}_ND_MODE: 0 ROSE ILS, 1 ROSE VOR,
// 2 ROSE NAV, 3 ARC, 4 PLAN. Zoom L:A32NX_EFIS_{L,R}_OANS_RANGE: 0..4 = ZOOM 0.2/0.5/1/2/5 NM,
// 5 = no ZOOM range selected. A32NX.FCU_EFIS_{L,R}_RANGE_SET takes the knob position:
// 0..4 = ZOOM 0.2/0.5/1/2/5 NM, 5..11 = 10..640 NM (a380_efis_range_selection).
// The L-vars are outputs of FBW's FCU simulation, so only its events are used to change them.
// Entering ARC never moves a ZOOM position, so mode and range can be sent back to back.
// The displays are switched whether or not the OANS has airport data: L:A32NX_OANS_AVAILABLE
// only reflects the one airport search FBW makes when the aircraft loads, which fails when the
// map server (Navigraph, or AMDB Bridge in its place) was not reachable at that moment.
constexpr int kFbwModeArc = 3;
// ZOOM 0.5 NM: tested in the sim, ZOOM 2 NM (the A350 FCOM value) was two detents too wide
// to see the aircraft and the airport around it well. A closer ZOOM already selected is kept.
constexpr int kFbwZoomPosition = 1;

const char* fbwZoomName(double position) {
  static const char* const kNames[] = {"0.2 NM", "0.5 NM", "1 NM", "2 NM", "5 NM", "off"};
  if (!(position >= 0 && position <= 5)) {
    return "?";
  }
  return kNames[static_cast<int>(position)];
}

void fbwQueueSide(const char* side) {
  char name[64];
  snprintf(name, sizeof(name), "A32NX_EFIS_%s_ND_MODE", side);
  if (readLVar(name) != kFbwModeArc) {
    queueCommand("%d (>K:A32NX.FCU_EFIS_%s_MODE_SET)", kFbwModeArc, side);
  }
  snprintf(name, sizeof(name), "A32NX_EFIS_%s_ND_RANGE", side);
  const bool zoomSelected = readLVar(name) == 0;
  snprintf(name, sizeof(name), "A32NX_EFIS_%s_OANS_RANGE", side);
  if (!zoomSelected || readLVar(name) > kFbwZoomPosition) {  // no ZOOM range, or a wider one
    queueCommand("%d (>K:A32NX.FCU_EFIS_%s_RANGE_SET)", kFbwZoomPosition, side);
  }
}

void fbwLogSide(const char* when, const char* side) {
  char mode[64];
  char range[64];
  char oansRange[64];
  snprintf(mode, sizeof(mode), "A32NX_EFIS_%s_ND_MODE", side);
  snprintf(range, sizeof(range), "A32NX_EFIS_%s_ND_RANGE", side);
  snprintf(oansRange, sizeof(oansRange), "A32NX_EFIS_%s_OANS_RANGE", side);
  logLine("FBW A380X %s: ND %s mode %.0f (3 = ARC), range %.0f (0 = ZOOM), zoom %s", when, side, readLVar(mode),
          readLVar(range), fbwZoomName(readLVar(oansRange)));
}

bool fbwA380xShowAirportMap(int step) {
  switch (step) {
    case 0:
      logLine("FBW A380X: L:A32NX_OANS_AVAILABLE = %.0f", readLVar("A32NX_OANS_AVAILABLE"));
      fbwLogSide("before", "L");
      fbwLogSide("before", "R");
      fbwQueueSide("L");
      return false;
    case 1:
      fbwQueueSide("R");
      return false;
    case 2:
      return false;
    default:  // two seconds after the last command
      fbwLogSide("after", "L");
      fbwLogSide("after", "R");
      return true;
  }
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
// ZOOM 0.5 NM, like the FBW A380X. A closer ZOOM already selected is kept; a wider one is zoomed
// in, including the one the aircraft's own OIS "autozoom" option selects at touchdown.
constexpr int kA350ZoomPosition = 1;

const char* a350ModeName(double mode) {
  static const char* const kNames[] = {"LS", "VOR", "NAV", "ARC", "PLAN"};
  if (!(mode >= 0 && mode <= 4)) {
    return "?";
  }
  return kNames[static_cast<int>(mode)];
}

const char* a350RangeName(double position) {
  static const char* const kNames[] = {"ZOOM 0.2 NM", "ZOOM 0.5 NM", "ZOOM 1 NM", "ZOOM 2 NM", "ZOOM 5 NM", "10 NM",
                                       "20 NM",       "40 NM",       "80 NM",     "160 NM",    "320 NM",    "640 NM"};
  if (!(position >= 0 && position <= 11)) {
    return "?";
  }
  return kNames[static_cast<int>(position)];
}

// Looks up the names of the mode and range L-vars of one side; false if the aircraft has none.
bool a350FindSideVars(const char* side, char (&modeVar)[64], char (&rangeVar)[64]) {
  snprintf(modeVar, sizeof(modeVar), "INI_MAP_MODE_%s_SWITCH", side);
  snprintf(rangeVar, sizeof(rangeVar), "INI_MAP_RANGE_%s_SWITCH", side);
  if (!lvarExists(rangeVar)) {
    snprintf(rangeVar, sizeof(rangeVar), "INI_MAP_MODE_RANGE_%s_SWITCH", side);
  }
  return lvarExists(modeVar) && lvarExists(rangeVar);
}

void a350LogSide(const char* when, const char* side) {
  char modeVar[64];
  char rangeVar[64];
  if (a350FindSideVars(side, modeVar, rangeVar)) {
    const double mode = readLVar(modeVar);
    const double range = readLVar(rangeVar);
    logLine("iniBuilds A350 %s: %s = %.0f (%s), %s = %.0f (%s)", when, modeVar, mode, a350ModeName(mode), rangeVar,
            range, a350RangeName(range));
  }
}

void a350QueueSide(const char* side) {
  char modeVar[64];
  char rangeVar[64];
  if (!a350FindSideVars(side, modeVar, rangeVar)) {
    logLine("iniBuilds A350: EFIS L-vars for %s not found, nothing to do", side);
    return;
  }
  a350LogSide("before", side);
  if (readLVar(modeVar) != kA350ModeArc) {
    queueCommand("%d (>L:%s)", kA350ModeArc, modeVar);
  }
  if (readLVar(rangeVar) > kA350ZoomPosition) {  // no ZOOM range, or a wider one
    queueCommand("%d (>L:%s)", kA350ZoomPosition, rangeVar);
  }
}

// Two seconds after a side has been set: if its range has been widened again in the meantime,
// e.g. by the aircraft's own autozoom firing after the add-on, it is set once more.
void a350RecheckSide(const char* side) {
  char modeVar[64];
  char rangeVar[64];
  if (!a350FindSideVars(side, modeVar, rangeVar)) {
    return;
  }
  const double range = readLVar(rangeVar);
  if (range > kA350ZoomPosition) {
    logLine("iniBuilds A350: %s is %s again, setting %s once more", rangeVar, a350RangeName(range),
            a350RangeName(kA350ZoomPosition));
    queueCommand("%d (>L:%s)", kA350ZoomPosition, rangeVar);
  }
}

bool iniA350ShowAirportMap(int step) {
  // The first officer's side follows two seconds after the captain's, and each re-check two
  // seconds after the previous change: loading the map on both NDs at the same moment has
  // crashed the A350 in the past (fixed in v1.0.5).
  switch (step) {
    case 0:
      if (lvarExists("INI_ANF_AUTO_ZOOM")) {
        logLine("iniBuilds A350: own autozoom option L:INI_ANF_AUTO_ZOOM = %.0f", readLVar("INI_ANF_AUTO_ZOOM"));
      }
      a350QueueSide("CAPT");
      return false;
    case 2:
      a350QueueSide("FO");
      return false;
    case 4:
      a350RecheckSide("CAPT");
      return false;
    case 6:
      a350RecheckSide("FO");
      return false;
    case 8:  // two seconds after the last possible command
      a350LogSide("after", "CAPT");
      a350LogSide("after", "FO");
      return true;
    default:
      return false;
  }
}

// --- Synaptic A220 ----------------------------------------------------------
// The MAP range knob of each Control Tuning Panel fires H:A220_CTP_RANGE_{1,2}_{INC,DEC}
// once per detent (1 = captain, 2 = first officer). Below 2 NM on the ground the MAP shows the
// airport moving map; its ranges are 1000 FT, 2000 FT, 3000 FT and 1 NM. The aircraft publishes
// the selected range nowhere, so the knob is turned to the smallest range first and then two
// detents up to 3000 FT (0.49 NM), the closest match to the 0.5 NM of the Airbus profiles. The
// airport moving map itself needs Synaptic A220 v1.0.10 or newer (or AMDB Bridge's A220 map);
// older versions show AIRPORT MAP FAULT at these ranges.
constexpr int kA220DetentsToSmallestRange = 20;
constexpr int kA220DetentsSmallestTo3000Ft = 2;
// The detents go out 0.1 s apart, alternating between the two knobs, so each knob gets one
// every 0.2 s, about the pace of a hand on the knob. Sent once per frame (up to version 1.3.0),
// not all of them arrived: after a landing one side stood at 2000 FT, the other at 1000 FT.
constexpr float kA220DetentSeconds = 0.1f;
// A pause at the smallest range before turning back up: on the way down the display switches
// over to the airport map.
constexpr float kA220SettleSeconds = 1.0f;
// AMDB Bridge's airport map for the A220 can be dragged away from the aircraft. While this L-var
// is 1 it follows the aircraft again (amdb-a220-amm.js). The map reads it once it has been on
// screen, so the L-var only exists then; the add-on holds it at 1 while turning the knobs (the
// map only looks at it while it is showing) and sets it back to 0 afterwards. If something else
// already holds it at 1, the add-on leaves it alone.
constexpr const char* kAmdbPanResetVar = "AMDB_AMM_PAN_RESET";
constexpr const char* kAmdbPanReleaseCommand = "0 (>L:AMDB_AMM_PAN_RESET)";
constexpr float kAmdbPanResetHoldSeconds = 1.0f;

bool synapticA220ShowAirportMap(int step) {
  (void)step;  // everything is queued at once, the queue keeps the pace
  const bool amdbMap = lvarExists(kAmdbPanResetVar) && readLVar(kAmdbPanResetVar) == 0;
  if (amdbMap) {
    queueCommand("1 (>L:%s)", kAmdbPanResetVar);
    g_queueCleanupCommand = kAmdbPanReleaseCommand;
  }
  for (int i = 0; i < kA220DetentsToSmallestRange; ++i) {
    queueCommandAfter(kA220DetentSeconds, "(>H:A220_CTP_RANGE_1_DEC)");
    queueCommandAfter(kA220DetentSeconds, "(>H:A220_CTP_RANGE_2_DEC)");
  }
  for (int i = 0; i < kA220DetentsSmallestTo3000Ft; ++i) {
    queueCommandAfter(i == 0 ? kA220SettleSeconds : kA220DetentSeconds, "(>H:A220_CTP_RANGE_1_INC)");
    queueCommandAfter(kA220DetentSeconds, "(>H:A220_CTP_RANGE_2_INC)");
  }
  if (amdbMap) {
    queueCommandAfter(kAmdbPanResetHoldSeconds, "%s", kAmdbPanReleaseCommand);
  }
  logLine("Synaptic A220: both range knobs %d detents down, then %d up to 3000 FT%s", kA220DetentsToSmallestRange,
          kA220DetentsSmallestTo3000Ft, amdbMap ? "; AMDB airport map centred on the aircraft" : "");
  return true;
}

// FBW A380X: title "FlyByWire A380X (A380-842)", folder FlyByWire_A380X (MSFS 2024 package) or
// FlyByWire_A380_842 (MSFS 2020 package).
const AircraftProfile kProfiles[] = {
    {"FlyByWire A380X", {"a380x", "flybywire_a380"}, fbwA380xShowAirportMap},
    {"iniBuilds A350", {"a350", nullptr}, iniA350ShowAirportMap},
    {"Synaptic A220", {"a220", nullptr}, synapticA220ShowAirportMap},
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
    for (const char* keyword : profile.keywords) {
      if (keyword != nullptr &&
          (findIgnoreCase(title, keyword) != nullptr || findIgnoreCase(aircraftFolders, keyword) != nullptr)) {
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
bool g_aircraftLogged = false;  // the recognised aircraft has been written to the log
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
    g_aircraftLogged = false;
  }
  if (!g_aircraftLogged) {
    g_aircraftLogged = true;
    logLine("aircraft \"%s\" (%s): %s", data.title, g_aircraftPath,
            profile != nullptr ? profile->name : "not supported, the module stays idle");
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
      logLine("airborne at %.0f ft AGL: armed for the next landing", data.altitudeAglFt);
    }
    return;
  }

  g_airborneTicks = 0;
  if (!g_armed) {
    return;
  }
  if (++g_groundTicks == 1) {
    if (data.groundVelocityKts < kMinTouchdownGroundSpeedKts) {
      logLine("on the ground at %.0f kt: not a landing, disarmed", data.groundVelocityKts);
      g_armed = false;
      return;
    }
    logLine("touchdown at %.0f kt", data.groundVelocityKts);
  }
  if (g_groundTicks < kTouchdownConfirmTicks) {
    return;
  }

  logLine("%s: landing confirmed at %.0f kt, bringing up the airport map", g_profile->name,
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
      // A frame rate outside 5..1000 fps can only be a bogus value; 30 fps keeps the clock going.
      const float frameRate = frame->fFrameRate;
      const float frameSeconds = frameRate >= 5.0f && frameRate <= 1000.0f ? 1.0f / frameRate : 1.0f / 30.0f;
      sendNextQueuedCommand(frameSeconds);
      g_secondsSinceStart += frameSeconds;
      g_secondsSinceDataRequest += frameSeconds;
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
        logLine("aircraft loaded: %s", g_aircraftPath);
      } else if (event->uEventID == EVT_FLIGHT_LOADED) {
        logLine("flight loaded: %s", event->szFileName);
      }
      if (event->uEventID == EVT_AIRCRAFT_LOADED || event->uEventID == EVT_FLIGHT_LOADED) {
        // A new aircraft or flight starts from scratch, e.g. at a gate after quitting the last
        // flight in the air; commands still queued for the previous aircraft are dropped.
        g_profile = nullptr;
        g_aircraftLogged = false;
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
        logLine("SimConnect exception %u", static_cast<unsigned>(exception->dwException));
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
  // Start a fresh log file for this simulator session.
  if (FILE* file = fopen(kLogPath, "w")) {
    fclose(file);
  }
  logLine("OANS Auto Zoom %s started", kVersion);
  if (FAILED(SimConnect_Open(&g_simConnect, "OansAutoZoom", nullptr, 0, 0, 0))) {
    logLine("SimConnect_Open failed");
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

  logLine("%s", SUCCEEDED(result) ? "initialised, waiting for an aircraft" : "initialisation incomplete");
}

extern "C" MSFS_CALLBACK void module_deinit(void) {
  if (g_simConnect != 0) {
    SimConnect_Close(g_simConnect);
    g_simConnect = 0;
  }
}
