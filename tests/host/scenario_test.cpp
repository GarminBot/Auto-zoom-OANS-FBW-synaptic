// Host test for src/OansAutoZoom.cpp: runs the module logic outside the simulator.
//
// SimConnect and execute_calculator_code are faked. The fake only understands the
// two RPN forms the module is allowed to use - "(L:NAME)" to read and
// "<number> (>K:NAME)" to send an event - and fails on anything else.
// Build and run: tests/host/run.sh

#include <MSFS/Legacy/gauges.h>
#include <MSFS/MSFS.h>
#include <MSFS/MSFS_WindowsTypes.h>
#include <SimConnect.h>

#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <map>
#include <string>
#include <vector>

extern "C" void module_init(void);
extern "C" void module_deinit(void);

namespace {

int g_failures = 0;

void fail(const std::string& message) {
  ++g_failures;
  std::printf("FAIL %s\n", message.c_str());
}

// --- Fake simulator ---------------------------------------------------------

DispatchProc g_dispatch = nullptr;
std::map<SIMCONNECT_CLIENT_EVENT_ID, std::string> g_systemEvents;
bool g_dataRequested = false;

std::map<std::string, double> g_lvars;
std::vector<std::string> g_sentEvents;  // "NAME=value", cleared by each tick()

struct Aircraft {
  std::string title;
  std::string atcModel;
  bool onGround = true;
  double groundSpeedKts = 0;
  double altitudeAglFt = 0;
} g_aircraft;

// Fake FBW A380X: FCU state behind the custom events.
void fbwHandleEvent(const std::string& name, int value) {
  for (const char* side : {"L", "R"}) {
    const std::string prefix = std::string("A32NX.FCU_EFIS_") + side;
    if (name == prefix + "_MODE_SET") {
      g_lvars[std::string("A32NX_EFIS_") + side + "_ND_MODE"] = value;
    } else if (name == prefix + "_RANGE_SET") {
      g_lvars[std::string("A32NX_EFIS_") + side + "_OANS_RANGE"] = value <= 4 ? value : 5;
      g_lvars[std::string("A32NX_EFIS_") + side + "_ND_RANGE"] = value <= 4 ? 0 : value - 4;
    }
  }
}

void tick() {
  g_dataRequested = false;

  // "1sec" system event -> the module requests the aircraft data once.
  for (const auto& [id, name] : g_systemEvents) {
    if (name == "1sec") {
      SIMCONNECT_RECV_EVENT event{};
      event.dwID = SIMCONNECT_RECV_ID_EVENT;
      event.uEventID = id;
      g_dispatch(&event, sizeof(event), nullptr);
    }
  }
  if (!g_dataRequested) {
    fail("module did not request aircraft data on the 1sec event");
    return;
  }

  // SimConnect appends the requested data at dwData, beyond the end of the declared struct.
  alignas(8) unsigned char buffer[sizeof(SIMCONNECT_RECV_SIMOBJECT_DATA) + 1024] = {};
  auto* header = reinterpret_cast<SIMCONNECT_RECV_SIMOBJECT_DATA*>(buffer);
  header->dwID = SIMCONNECT_RECV_ID_SIMOBJECT_DATA;
  header->dwRequestID = 0;
  const auto dataOffset = reinterpret_cast<unsigned char*>(&header->dwData) - buffer;
  unsigned char* data = buffer + dataOffset;
  std::strncpy(reinterpret_cast<char*>(data), g_aircraft.title.c_str(), 255);
  std::strncpy(reinterpret_cast<char*>(data + 256), g_aircraft.atcModel.c_str(), 255);
  const double values[] = {g_aircraft.onGround ? 1.0 : 0.0, g_aircraft.groundSpeedKts, g_aircraft.altitudeAglFt};
  std::memcpy(data + 512, values, sizeof(values));
  g_dispatch(header, sizeof(buffer), nullptr);
}

// Runs `seconds` ticks with the given aircraft state; g_sentEvents collects what was sent.
void fly(int seconds, bool onGround, double groundSpeedKts, double altitudeAglFt) {
  g_aircraft.onGround = onGround;
  g_aircraft.groundSpeedKts = groundSpeedKts;
  g_aircraft.altitudeAglFt = altitudeAglFt;
  g_sentEvents.clear();
  for (int i = 0; i < seconds; ++i) {
    tick();
  }
}

void expectEvents(const char* label, const std::vector<std::string>& expected) {
  if (g_sentEvents == expected) {
    std::printf("ok   %s\n", label);
    return;
  }
  std::string got;
  for (const auto& e : g_sentEvents) {
    got += e + " ";
  }
  std::string want;
  for (const auto& e : expected) {
    want += e + " ";
  }
  fail(std::string(label) + ": sent [" + got + "] expected [" + want + "]");
}

void expectLVar(const char* label, const char* name, double expected) {
  if (g_lvars[name] == expected) {
    std::printf("ok   %s\n", label);
    return;
  }
  fail(std::string(label) + ": " + name + " = " + std::to_string(g_lvars[name]) + ", expected " +
       std::to_string(expected));
}

// Take-off from the ground and cruise long enough to arm the module.
void takeOffAndCruise() {
  fly(60, true, 15, 0);     // taxi
  fly(30, true, 140, 0);    // take-off roll
  fly(20, false, 160, 50);  // initial climb below 100 ft AGL does not count
  fly(40, false, 250, 3000);
}

void setUpFbw(int ndMode, bool oansAvailable) {
  g_aircraft.title = "FlyByWire A380X (A380-842)";
  g_aircraft.atcModel = "TT:ATCCOM.AC_MODEL A380.0.text";
  g_lvars["A32NX_OANS_AVAILABLE"] = oansAvailable ? 1 : 0;
  for (const char* side : {"L", "R"}) {
    g_lvars[std::string("A32NX_EFIS_") + side + "_ND_MODE"] = ndMode;
    fbwHandleEvent(std::string("A32NX.FCU_EFIS_") + side + "_RANGE_SET", 5);  // 10 NM
  }
}

}  // namespace

// --- Fakes for the MSFS API used by the module ------------------------------

extern "C" {
BOOL execute_calculator_code(PCSTRINGZ code, FLOAT64* fvalue, SINT32*, PCSTRINGZ*) {
  const std::string rpn = code;
  char name[128];
  int value = 0;
  int consumed = 0;
  if (std::sscanf(code, "(L:%127[A-Z0-9_])%n", name, &consumed) == 1 && rpn.size() == static_cast<size_t>(consumed)) {
    if (fvalue == nullptr) {
      fail("read without result pointer: " + rpn);
    } else {
      *fvalue = g_lvars[name];
    }
    return 1;
  }
  consumed = 0;
  if (std::sscanf(code, "%d (>K:%127[A-Za-z0-9_.])%n", &value, name, &consumed) == 2 &&
      rpn.size() == static_cast<size_t>(consumed)) {
    g_sentEvents.push_back(std::string(name) + "=" + std::to_string(value));
    fbwHandleEvent(name, value);
    return 1;
  }
  fail("unsupported RPN: \"" + rpn + "\"");
  return 0;
}
ID register_named_variable(PCSTRINGZ) {
  fail("register_named_variable is not expected");
  return -1;
}
FLOAT64 get_named_variable_value(ID) {
  fail("get_named_variable_value is not expected");
  return 0;
}
void set_named_variable_value(ID, FLOAT64) {
  fail("set_named_variable_value is not expected");
}
HRESULT SimConnect_Open(HANDLE* phSimConnect, LPCSTR, HWND, DWORD, HANDLE, DWORD) {
  *phSimConnect = 1;
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
                                          SIMCONNECT_OBJECT_ID objectId, SIMCONNECT_PERIOD period,
                                          SIMCONNECT_DATA_REQUEST_FLAG, DWORD, DWORD, DWORD) {
  if (objectId != SIMCONNECT_OBJECT_ID_USER || period != SIMCONNECT_PERIOD_ONCE) {
    fail("unexpected data request");
  }
  g_dataRequested = true;
  return S_OK;
}
HRESULT SimConnect_SubscribeToSystemEvent(HANDLE, SIMCONNECT_CLIENT_EVENT_ID eventId, const char* systemEventName) {
  g_systemEvents[eventId] = systemEventName;
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

  // Unsupported aircraft: never touches anything.
  g_aircraft.title = "Asobo Cessna 172";
  g_aircraft.atcModel = "C172";
  takeOffAndCruise();
  fly(3, true, 120, 0);
  fly(30, true, 20, 0);
  expectEvents("unsupported aircraft is ignored", {});

  // FBW A380X, approach flown in ROSE ILS.
  setUpFbw(0, true);
  fly(60, true, 15, 0);
  fly(30, true, 140, 0);
  fly(20, false, 160, 50);
  fly(40, false, 250, 3000);
  expectEvents("FBW: nothing during taxi, take-off and flight", {});
  fly(1, true, 140, 0);
  expectEvents("FBW: first second on the ground is not yet a touchdown", {});
  fly(1, true, 135, 0);
  expectEvents("FBW: touchdown confirmed, still fast", {});
  fly(3, true, 100, 0);
  expectEvents("FBW: waiting while faster than 80 kt", {});
  fly(1, true, 79, 0);
  expectEvents("FBW: below 80 kt -> ROSE ILS switched to ARC",
               {"A32NX.FCU_EFIS_L_MODE_SET=3", "A32NX.FCU_EFIS_R_MODE_SET=3"});
  fly(1, true, 60, 0);
  expectEvents("FBW: next second -> ZOOM 2 NM", {"A32NX.FCU_EFIS_L_RANGE_SET=3", "A32NX.FCU_EFIS_R_RANGE_SET=3"});
  expectLVar("FBW: left ND shows the OANS at 2 NM", "A32NX_EFIS_L_OANS_RANGE", 3);
  fly(120, true, 15, 0);
  expectEvents("FBW: nothing more while taxiing in", {});

  // Next flight, ND already in ARC: only the range is set, at the latest 15 s after touchdown.
  takeOffAndCruise();
  fly(2, true, 130, 0);
  fly(15, true, 90, 0);
  expectEvents("FBW: ARC approach, still faster than 80 kt after 15 s",
               {"A32NX.FCU_EFIS_L_RANGE_SET=3", "A32NX.FCU_EFIS_R_RANGE_SET=3"});

  // Bounce and touch-and-go do not count; the full stop landing afterwards does.
  takeOffAndCruise();
  fly(1, true, 130, 0);
  fly(2, false, 130, 10);
  fly(10, true, 120, 0);
  fly(40, false, 150, 1500);
  expectEvents("FBW: bounce and touch-and-go faster than 80 kt: nothing", {});
  fly(2, true, 125, 0);
  fly(4, true, 70, 0);
  expectEvents("FBW: full stop landing -> ZOOM", {"A32NX.FCU_EFIS_L_RANGE_SET=3", "A32NX.FCU_EFIS_R_RANGE_SET=3"});

  // No Navigraph data: the OANS cannot be shown, nothing is changed.
  setUpFbw(3, false);
  takeOffAndCruise();
  fly(2, true, 130, 0);
  fly(10, true, 60, 0);
  expectEvents("FBW: OANS not available -> hands off", {});

  // Flight started in the air (e.g. on approach): arms after 30 s airborne.
  g_aircraft.title = "Asobo Cessna 172";
  fly(1, false, 150, 2000);
  setUpFbw(3, true);
  fly(35, false, 150, 2000);
  fly(2, true, 130, 0);
  fly(3, true, 60, 0);
  expectEvents("FBW: flight started in the air", {"A32NX.FCU_EFIS_L_RANGE_SET=3", "A32NX.FCU_EFIS_R_RANGE_SET=3"});

  module_deinit();

  std::printf("%s (%d failure(s))\n", g_failures == 0 ? "PASSED" : "FAILED", g_failures);
  return g_failures == 0 ? 0 : 1;
}
