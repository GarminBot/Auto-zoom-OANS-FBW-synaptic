// OANS Auto-Zoom: standalone WASM module for Microsoft Flight Simulator 2024.
//
// Automatically switches the OANS zoom of the FlyByWire A380X depending on
// ground speed and restores the previous ND range after take-off.
//
// STARTER CODE: written against the MSFS SDK API but NOT yet tested in the
// simulator. Background and sources: docs/fbw-a380x-oans-zoom.md
//
// How it works:
// - Once per second SimConnect delivers TITLE, SIM ON GROUND and GROUND VELOCITY.
// - The current state of the EFIS control panel is read from the FBW L-vars.
// - The zoom is changed exclusively through FBW's custom events
//   A32NX.FCU_EFIS_{L,R}_RANGE_SET (value 0..11). Writing the L-vars directly
//   has no effect because fbw.wasm overwrites them every frame.
// - If the pilot turns the range knob, the automation of that side pauses
//   until the next speed band is reached or the aircraft takes off.

#include <MSFS/MSFS.h>
#include <MSFS/MSFS_WindowsTypes.h>
#include <MSFS/Legacy/gauges.h>
#include <SimConnect.h>

#include <cstdio>
#include <cstring>

namespace {

// ---------------------------------------------------------------------------
// Settings
// ---------------------------------------------------------------------------

// Only aircraft whose TITLE contains this text are handled
// ("FlyByWire A380X (A380-842)", "FlyByWire A380X (A380-842) No Cabin").
constexpr const char* kAircraftTitleFilter = "A380X";

// Control the captain side (L) and/or the first officer side (R).
constexpr bool kControlLeftSide = true;
constexpr bool kControlRightSide = true;

// SIM ON GROUND must be stable for this many ticks (seconds) before it counts (touchdown bounces).
constexpr int kGroundStateDebounceTicks = 3;

// Ticks after which a sent command must be visible in the L-vars. The FCU applies it within
// one frame, so any later mismatch means the pilot (or FBW itself) moved the knob.
constexpr int kCommandSettleTicks = 1;

// Positions of the A380X range selector (see docs/fbw-a380x-oans-zoom.md).
constexpr int kPosZoom05Nm = 1;
constexpr int kPosZoom1Nm = 2;
constexpr int kPosZoom2Nm = 3;
constexpr int kPosZoom5Nm = 4;
constexpr int kPos10Nm = 5;

// Ground speed bands -> zoom position. `upperKts` is the upper limit of the band.
struct SpeedBand {
  int position;
  double upperKts;
};
constexpr SpeedBand kSpeedBands[] = {
    {kPosZoom05Nm, 10.0},  // gate, slow taxi, tight turns
    {kPosZoom1Nm, 25.0},   // normal taxi
    {kPosZoom2Nm, 60.0},   // fast taxi, start of the take-off roll, end of the landing rollout
    {kPosZoom5Nm, 1.0e9},  // take-off roll, landing rollout
};
constexpr int kSpeedBandCount = sizeof(kSpeedBands) / sizeof(kSpeedBands[0]);
constexpr double kHysteresisKts = 2.0;

// ---------------------------------------------------------------------------
// SimConnect IDs and data
// ---------------------------------------------------------------------------

enum DataDefinitionId : SIMCONNECT_DATA_DEFINITION_ID { DEF_AIRCRAFT = 0 };
enum DataRequestId : SIMCONNECT_DATA_REQUEST_ID { REQ_AIRCRAFT = 0 };
enum ClientEventId : SIMCONNECT_CLIENT_EVENT_ID { EVT_RANGE_SET_L = 0, EVT_RANGE_SET_R = 1 };

// Order and types must match the SimConnect_AddToDataDefinition calls in module_init.
struct AircraftData {
  char title[256];
  double simOnGround;
  double groundVelocityKts;
};

struct EfisSide {
  const char* name;
  SIMCONNECT_CLIENT_EVENT_ID rangeSetEvent;
  const char* rangeSetEventName;
  const char* ndRangeVarName;
  const char* oansRangeVarName;
  const char* ndModeVarName;
  bool enabled;

  ID ndRangeVar = -1;
  ID oansRangeVar = -1;
  ID ndModeVar = -1;

  int speedBand = -1;           // current speed band, -1 = not determined yet
  int lastSentPosition = -1;    // last position we sent, -1 = none
  int lastSentTick = 0;
  int positionBeforeZoom = -1;  // selector position before our first zoom, restored after take-off
  bool zoomedByUs = false;
  bool paused = false;          // pilot took over; wait for the next speed band
  int pausedInBand = -1;
};

HANDLE g_simConnect = nullptr;
int g_tick = 0;
ID g_oansAvailableVar = -1;

bool g_groundStateKnown = false;
bool g_onGround = false;
int g_groundStateCandidateTicks = 0;

EfisSide g_sides[] = {
    {"L", EVT_RANGE_SET_L, "A32NX.FCU_EFIS_L_RANGE_SET", "A32NX_EFIS_L_ND_RANGE", "A32NX_EFIS_L_OANS_RANGE",
     "A32NX_EFIS_L_ND_MODE", kControlLeftSide},
    {"R", EVT_RANGE_SET_R, "A32NX.FCU_EFIS_R_RANGE_SET", "A32NX_EFIS_R_ND_RANGE", "A32NX_EFIS_R_OANS_RANGE",
     "A32NX_EFIS_R_ND_MODE", kControlRightSide},
};

// ---------------------------------------------------------------------------
// Logic
// ---------------------------------------------------------------------------

// Current selector position 0..11 derived from the FBW L-vars.
// ND_RANGE: 0 = ZOOM, 1..7 = 10..640 NM. OANS_RANGE: 0..4 = zoom level, 5 = no zoom.
int readSelectorPosition(const EfisSide& side) {
  const int ndRange = static_cast<int>(get_named_variable_value(side.ndRangeVar));
  const int oansRange = static_cast<int>(get_named_variable_value(side.oansRangeVar));
  return ndRange == 0 ? oansRange : ndRange + 4;
}

// OANS is only shown in ROSE NAV (2), ARC (3) and PLAN (4).
bool isOansCapableMode(int ndMode) {
  return ndMode == 2 || ndMode == 3 || ndMode == 4;
}

int rawSpeedBand(double groundSpeedKts) {
  int band = 0;
  while (band < kSpeedBandCount - 1 && groundSpeedKts > kSpeedBands[band].upperKts) {
    ++band;
  }
  return band;
}

// Speed band with hysteresis, so the map does not jump back and forth at a band limit.
int nextSpeedBand(double groundSpeedKts, int currentBand) {
  const int raw = rawSpeedBand(groundSpeedKts);
  if (currentBand < 0 || raw == currentBand) {
    return raw;
  }
  if (raw > currentBand) {
    return groundSpeedKts > kSpeedBands[currentBand].upperKts + kHysteresisKts ? raw : currentBand;
  }
  return groundSpeedKts < kSpeedBands[currentBand - 1].upperKts - kHysteresisKts ? raw : currentBand;
}

void sendSelectorPosition(EfisSide& side, int position) {
  const HRESULT hr = SimConnect_TransmitClientEvent(g_simConnect, SIMCONNECT_OBJECT_ID_USER, side.rangeSetEvent,
                                                    static_cast<DWORD>(position), SIMCONNECT_GROUP_PRIORITY_HIGHEST,
                                                    SIMCONNECT_EVENT_FLAG_GROUPID_IS_PRIORITY);
  if (FAILED(hr)) {
    fprintf(stderr, "[OansAutoZoom] %s: sending %s failed\n", side.name, side.rangeSetEventName);
    return;
  }
  side.lastSentPosition = position;
  side.lastSentTick = g_tick;
  printf("[OansAutoZoom] %s: range selector -> %d\n", side.name, position);
}

void forgetControl(EfisSide& side) {
  side.speedBand = -1;
  side.lastSentPosition = -1;
  side.positionBeforeZoom = -1;
  side.zoomedByUs = false;
  side.paused = false;
  side.pausedInBand = -1;
}

void updateSide(EfisSide& side, bool onGround, double groundSpeedKts) {
  const int position = readSelectorPosition(side);
  const bool lastCommandSettled = g_tick - side.lastSentTick >= kCommandSettleTicks;

  // Manual input: our last command no longer matches although it had time to take effect.
  if (side.lastSentPosition >= 0 && lastCommandSettled && position != side.lastSentPosition) {
    printf("[OansAutoZoom] %s: manual input detected, pausing until the next speed band\n", side.name);
    const int band = side.speedBand;
    forgetControl(side);
    side.paused = onGround;
    side.pausedInBand = band;
  }

  if (!onGround) {
    if (side.zoomedByUs && position < kPos10Nm) {
      // After take-off: restore the range from before the zoom, once.
      sendSelectorPosition(side, side.positionBeforeZoom >= kPos10Nm ? side.positionBeforeZoom : kPos10Nm);
    } else if (lastCommandSettled) {
      side.lastSentPosition = -1;
    }
    side.speedBand = -1;
    side.positionBeforeZoom = -1;
    side.zoomedByUs = false;
    side.paused = false;
    return;
  }

  // On the ground: only zoom with OANS available (power, Navigraph data) and ND in ROSE NAV, ARC or PLAN.
  const int ndMode = static_cast<int>(get_named_variable_value(side.ndModeVar));
  const bool oansAvailable = get_named_variable_value(g_oansAvailableVar) > 0.5;
  if (!isOansCapableMode(ndMode) || !oansAvailable) {
    forgetControl(side);
    return;
  }

  side.speedBand = nextSpeedBand(groundSpeedKts, side.speedBand);
  if (side.paused) {
    if (side.speedBand == side.pausedInBand) {
      return;
    }
    side.paused = false;
  }

  const int target = kSpeedBands[side.speedBand].position;
  if (target == position) {
    return;
  }
  if (!side.zoomedByUs) {
    side.positionBeforeZoom = position;
    side.zoomedByUs = true;
  }
  sendSelectorPosition(side, target);
}

void updateGroundState(bool rawOnGround) {
  if (!g_groundStateKnown) {
    g_onGround = rawOnGround;
    g_groundStateKnown = true;
    g_groundStateCandidateTicks = 0;
  } else if (rawOnGround == g_onGround) {
    g_groundStateCandidateTicks = 0;
  } else if (++g_groundStateCandidateTicks >= kGroundStateDebounceTicks) {
    g_onGround = rawOnGround;
    g_groundStateCandidateTicks = 0;
  }
}

void onAircraftData(const AircraftData& data) {
  ++g_tick;
  if (std::strstr(data.title, kAircraftTitleFilter) == nullptr) {
    // Different aircraft: start from scratch next time the A380X is loaded.
    g_groundStateKnown = false;
    for (EfisSide& side : g_sides) {
      forgetControl(side);
    }
    return;
  }

  updateGroundState(data.simOnGround > 0.5);
  for (EfisSide& side : g_sides) {
    if (side.enabled) {
      updateSide(side, g_onGround, data.groundVelocityKts);
    }
  }
}

void CALLBACK dispatchProc(SIMCONNECT_RECV* pData, DWORD cbData, void* pContext) {
  (void)cbData;
  (void)pContext;
  switch (pData->dwID) {
    case SIMCONNECT_RECV_ID_SIMOBJECT_DATA: {
      const auto* objectData = static_cast<SIMCONNECT_RECV_SIMOBJECT_DATA*>(pData);
      if (objectData->dwRequestID == REQ_AIRCRAFT) {
        onAircraftData(*reinterpret_cast<const AircraftData*>(&objectData->dwData));
      }
      break;
    }
    case SIMCONNECT_RECV_ID_EXCEPTION: {
      const auto* exception = static_cast<SIMCONNECT_RECV_EXCEPTION*>(pData);
      fprintf(stderr, "[OansAutoZoom] SimConnect exception %u (send id %u, index %u)\n",
              static_cast<unsigned>(exception->dwException), static_cast<unsigned>(exception->dwSendID),
              static_cast<unsigned>(exception->dwIndex));
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
    fprintf(stderr, "[OansAutoZoom] SimConnect_Open failed\n");
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

  for (EfisSide& side : g_sides) {
    track(SimConnect_MapClientEventToSimEvent(g_simConnect, side.rangeSetEvent, side.rangeSetEventName));
    side.ndRangeVar = register_named_variable(side.ndRangeVarName);
    side.oansRangeVar = register_named_variable(side.oansRangeVarName);
    side.ndModeVar = register_named_variable(side.ndModeVarName);
  }
  g_oansAvailableVar = register_named_variable("A32NX_OANS_AVAILABLE");

  track(SimConnect_RequestDataOnSimObject(g_simConnect, REQ_AIRCRAFT, DEF_AIRCRAFT, SIMCONNECT_OBJECT_ID_USER,
                                          SIMCONNECT_PERIOD_SECOND));
  // In a WASM module a single call is enough; afterwards the sim calls dispatchProc for every message.
  track(SimConnect_CallDispatch(g_simConnect, dispatchProc, nullptr));

  printf("[OansAutoZoom] %s\n", SUCCEEDED(result) ? "initialised" : "initialisation incomplete");
}

extern "C" MSFS_CALLBACK void module_deinit(void) {
  if (g_simConnect != nullptr) {
    SimConnect_Close(g_simConnect);
    g_simConnect = nullptr;
  }
}
