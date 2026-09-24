// Host test for src/OansAutoZoom.cpp: runs the module logic outside the simulator.
//
// SimConnect, the L-vars and the A380X FCU are faked: a sent RANGE_SET event
// moves the fake range selector immediately, as the real FCU does within one frame.
// Build and run: tests/host/run.sh

#include <MSFS/Legacy/gauges.h>
#include <MSFS/MSFS.h>
#include <MSFS/MSFS_WindowsTypes.h>
#include <SimConnect.h>

#include <cstdio>
#include <cstring>
#include <map>
#include <string>

extern "C" void module_init(void);
extern "C" void module_deinit(void);

namespace {

std::map<std::string, ID> g_varIds;
std::map<ID, double> g_varValues;
std::map<SIMCONNECT_CLIENT_EVENT_ID, std::string> g_eventNames;
DispatchProc g_dispatch = nullptr;

// State of the fake aircraft.
int g_selector[2] = {5, 5};  // range selector L/R, 5 = 10 NM
int g_ndMode = 3;            // ARC
bool g_oansAvailable = true;
int g_failures = 0;

void publishFakeFcu() {
  const char* sides[2] = {"L", "R"};
  for (int i = 0; i < 2; ++i) {
    const std::string prefix = std::string("A32NX_EFIS_") + sides[i];
    const int position = g_selector[i];
    g_varValues[g_varIds[prefix + "_ND_RANGE"]] = position <= 4 ? 0 : position - 4;
    g_varValues[g_varIds[prefix + "_OANS_RANGE"]] = position <= 4 ? position : 5;
    g_varValues[g_varIds[prefix + "_ND_MODE"]] = g_ndMode;
  }
  g_varValues[g_varIds["A32NX_OANS_AVAILABLE"]] = g_oansAvailable ? 1 : 0;
}

void tick(const char* title, bool onGround, double groundSpeedKts, int expectedLeft, int expectedRight,
          const char* label) {
  publishFakeFcu();

  // SimConnect appends the requested data at dwData, beyond the end of the declared struct.
  alignas(8) unsigned char buffer[sizeof(SIMCONNECT_RECV_SIMOBJECT_DATA) + 512] = {};
  auto* header = reinterpret_cast<SIMCONNECT_RECV_SIMOBJECT_DATA*>(buffer);
  header->dwID = SIMCONNECT_RECV_ID_SIMOBJECT_DATA;
  header->dwRequestID = 0;
  const auto dataOffset = reinterpret_cast<unsigned char*>(&header->dwData) - buffer;
  unsigned char* data = buffer + dataOffset;
  std::strncpy(reinterpret_cast<char*>(data), title, 255);
  const double onGroundValue = onGround ? 1.0 : 0.0;
  std::memcpy(data + 256, &onGroundValue, sizeof(double));
  std::memcpy(data + 264, &groundSpeedKts, sizeof(double));

  g_dispatch(header, sizeof(buffer), nullptr);

  const bool ok = g_selector[0] == expectedLeft && g_selector[1] == expectedRight;
  if (!ok) {
    ++g_failures;
  }
  std::printf("%s %-40s L=%2d R=%2d (expected %2d/%2d)\n", ok ? "ok  " : "FAIL", label, g_selector[0],
              g_selector[1], expectedLeft, expectedRight);
}

}  // namespace

// Fakes for the MSFS API used by the module.
extern "C" {
ID register_named_variable(PCSTRINGZ name) {
  const auto it = g_varIds.find(name);
  if (it != g_varIds.end()) {
    return it->second;
  }
  const ID id = static_cast<ID>(g_varIds.size());
  g_varIds[name] = id;
  g_varValues[id] = 0;
  return id;
}
FLOAT64 get_named_variable_value(ID id) {
  return g_varValues[id];
}
void set_named_variable_value(ID id, FLOAT64 value) {
  g_varValues[id] = value;
}
BOOL execute_calculator_code(PCSTRINGZ, FLOAT64*, SINT32*, PCSTRINGZ*) {
  return 1;
}
HRESULT SimConnect_Open(HANDLE* phSimConnect, LPCSTR, HWND, DWORD, HANDLE, DWORD) {
  *phSimConnect = reinterpret_cast<HANDLE>(1);
  return S_OK;
}
HRESULT SimConnect_Close(HANDLE) {
  return S_OK;
}
HRESULT SimConnect_AddToDataDefinition(HANDLE, SIMCONNECT_DATA_DEFINITION_ID, const char*, const char*,
                                       SIMCONNECT_DATATYPE, float, DWORD) {
  return S_OK;
}
HRESULT SimConnect_RequestDataOnSimObject(HANDLE, SIMCONNECT_DATA_REQUEST_ID, SIMCONNECT_DATA_DEFINITION_ID,
                                          SIMCONNECT_OBJECT_ID, SIMCONNECT_PERIOD, SIMCONNECT_DATA_REQUEST_FLAG, DWORD,
                                          DWORD, DWORD) {
  return S_OK;
}
HRESULT SimConnect_MapClientEventToSimEvent(HANDLE, SIMCONNECT_CLIENT_EVENT_ID eventId, const char* eventName) {
  g_eventNames[eventId] = eventName;
  return S_OK;
}
HRESULT SimConnect_TransmitClientEvent(HANDLE, SIMCONNECT_OBJECT_ID, SIMCONNECT_CLIENT_EVENT_ID eventId, DWORD data,
                                       SIMCONNECT_NOTIFICATION_GROUP_ID, SIMCONNECT_EVENT_FLAG) {
  const std::string& name = g_eventNames[eventId];
  if (name == "A32NX.FCU_EFIS_L_RANGE_SET") {
    g_selector[0] = static_cast<int>(data);
  } else if (name == "A32NX.FCU_EFIS_R_RANGE_SET") {
    g_selector[1] = static_cast<int>(data);
  } else {
    std::printf("FAIL unexpected event %s\n", name.c_str());
    ++g_failures;
  }
  return S_OK;
}
HRESULT SimConnect_CallDispatch(HANDLE, DispatchProc dispatch, void*) {
  g_dispatch = dispatch;
  return S_OK;
}
}

int main() {
  module_init();
  if (g_dispatch == nullptr) {
    std::printf("FAIL module_init did not register a dispatch callback\n");
    return 1;
  }

  const char* a380x = "FlyByWire A380X (A380-842)";

  tick("Asobo Cessna 172", true, 0, 5, 5, "other aircraft is ignored");
  tick(a380x, true, 0, 1, 1, "gate -> ZOOM 0.5 NM");
  tick(a380x, true, 11, 1, 1, "11 kt stays (hysteresis)");
  tick(a380x, true, 13, 2, 2, "13 kt -> ZOOM 1 NM");
  tick(a380x, true, 9, 2, 2, "9 kt stays (hysteresis)");
  tick(a380x, true, 7, 1, 1, "7 kt -> ZOOM 0.5 NM");

  g_selector[0] = 0;  // pilot turns the left knob to ZOOM 0.2 NM
  tick(a380x, true, 7, 0, 1, "manual input left is respected");
  tick(a380x, true, 8, 0, 1, "left stays paused in the same band");
  tick(a380x, true, 20, 2, 2, "next band: left resumes");

  tick(a380x, true, 80, 4, 4, "take-off roll -> ZOOM 5 NM");
  tick(a380x, false, 160, 4, 4, "airborne, debounce 1");
  tick(a380x, false, 170, 4, 4, "airborne, debounce 2");
  tick(a380x, false, 180, 5, 5, "airborne -> 10 NM restored");
  tick(a380x, false, 250, 5, 5, "climb, no further commands");

  g_selector[0] = 7;  // pilot selects 40 NM / 80 NM in flight
  g_selector[1] = 8;
  tick(a380x, false, 250, 7, 8, "manual ranges in flight are kept");

  tick(a380x, true, 135, 7, 8, "touchdown, debounce 1");
  tick(a380x, false, 134, 7, 8, "bounce");
  tick(a380x, true, 130, 7, 8, "ground again, debounce 1");
  tick(a380x, true, 120, 7, 8, "ground, debounce 2");
  tick(a380x, true, 100, 4, 4, "landing confirmed -> ZOOM 5 NM");
  tick(a380x, true, 50, 3, 3, "rollout -> ZOOM 2 NM");
  tick(a380x, true, 20, 2, 2, "vacating -> ZOOM 1 NM");

  g_ndMode = 0;  // ROSE ILS: no OANS
  tick(a380x, true, 5, 2, 2, "ROSE ILS: hands off");
  g_ndMode = 3;
  tick(a380x, true, 5, 1, 1, "ARC again -> ZOOM 0.5 NM");

  g_oansAvailable = false;
  tick(a380x, true, 20, 1, 1, "OANS not available: hands off");

  module_deinit();

  std::printf("%s (%d failure(s))\n", g_failures == 0 ? "PASSED" : "FAILED", g_failures);
  return g_failures == 0 ? 0 : 1;
}
