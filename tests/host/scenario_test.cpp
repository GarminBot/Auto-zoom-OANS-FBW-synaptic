// Host test for src/OansAutoZoom.cpp: runs the module logic outside the simulator.
//
// SimConnect, execute_calculator_code and check_named_variable are faked, together with
// the parts of the three supported aircraft the module talks to. The fake RPN parser only
// accepts the exact command forms the module is meant to use and fails on anything else.
// Build and run: tests/host/run.sh

#include <MSFS/Legacy/gauges.h>
#include <MSFS/MSFS.h>
#include <MSFS/MSFS_WindowsTypes.h>
#include <SimConnect.h>

#include <algorithm>
#include <cstdio>
#include <cstring>
#include <fstream>
#include <map>
#include <regex>
#include <sstream>
#include <string>
#include <vector>

#ifndef OANS_LOG_PATH
#error "Build with tests/host/run.sh, it sets OANS_LOG_PATH for the module and the test"
#endif

extern "C" void module_init(void);
extern "C" void module_deinit(void);

namespace {

int g_failures = 0;

void fail(const std::string& message) {
  ++g_failures;
  std::printf("FAIL %s\n", message.c_str());
}

// --- Fake aircraft ------------------------------------------------------------

enum class Type { Other, FbwA380x, IniA350, IniA320, SynapticA220 };

struct Aircraft {
  Type type = Type::Other;
  std::string title;
  std::string path;
  bool onGround = true;
  double groundSpeedKts = 0;
  double altitudeAglFt = 0;
} g_aircraft;

std::map<std::string, double> g_lvars;  // L-vars the loaded aircraft has registered
std::vector<std::string> g_commands;    // every command sent to the aircraft, cleared by fly()

// Synaptic A220 map ranges; the knob stops at both ends.
const std::vector<std::string> kA220Ranges = {"1000 FT", "2000 FT", "3000 FT", "1 NM", "2 NM", "5 NM",
                                              "10 NM",   "20 NM",   "40 NM",   "80 NM", "160 NM", "320 NM"};
int g_a220Range[3] = {0, 6, 6};  // index per CTP (1 and 2)

void fbwApplyEvent(const std::string& name, int value) {
  static const std::regex pattern("A32NX\\.FCU_EFIS_([LR])_(MODE|RANGE)_SET");
  std::smatch match;
  if (!std::regex_match(name, match, pattern)) {
    fail("unknown FBW event " + name);
    return;
  }
  const std::string prefix = "A32NX_EFIS_" + match[1].str();
  if (match[2] == "MODE") {
    g_lvars[prefix + "_ND_MODE"] = value;
  } else {
    g_lvars[prefix + "_OANS_RANGE"] = value <= 4 ? value : 5;
    g_lvars[prefix + "_ND_RANGE"] = value <= 4 ? 0 : value - 4;
  }
}

void a220ApplyEvent(const std::string& name) {
  static const std::regex pattern("A220_CTP_RANGE_([12])_(INC|DEC)");
  std::smatch match;
  if (!std::regex_match(name, match, pattern)) {
    fail("unknown A220 H-event " + name);
    return;
  }
  int& index = g_a220Range[std::stoi(match[1].str())];
  index += match[2] == "INC" ? 1 : -1;
  index = std::max(0, std::min(index, static_cast<int>(kA220Ranges.size()) - 1));
}

void loadAircraft(Type type, const std::string& title, const std::string& path) {
  g_aircraft.type = type;
  g_aircraft.title = title;
  g_aircraft.path = path;
  g_lvars.clear();
}

void setUpFbw(int ndMode, int rangePosition, bool oansAvailable,
              const std::string& title = "FlyByWire A380X (A380-842)",
              const std::string& path =
                  "SimObjects\\AirPlanes\\FlyByWire_A380X\\presets\\flybywire\\FlyByWire_A380_842\\config\\"
                  "aircraft.cfg") {
  loadAircraft(Type::FbwA380x, title, path);
  g_lvars["A32NX_OANS_AVAILABLE"] = oansAvailable ? 1 : 0;
  for (const char* side : {"L", "R"}) {
    fbwApplyEvent(std::string("A32NX.FCU_EFIS_") + side + "_MODE_SET", ndMode);
    fbwApplyEvent(std::string("A32NX.FCU_EFIS_") + side + "_RANGE_SET", rangePosition);
  }
}

void setUpA350(const std::string& title, int ndMode, int range, bool officialListNames) {
  loadAircraft(Type::IniA350, title,
               "SimObjects\\Airplanes\\A350\\presets\\iniBuilds\\A350-900\\config\\aircraft.CFG");
  const std::string rangeName = officialListNames ? "INI_MAP_MODE_RANGE_" : "INI_MAP_RANGE_";
  for (const char* side : {"CAPT", "FO"}) {
    g_lvars[std::string("INI_MAP_MODE_") + side + "_SWITCH"] = ndMode;
    g_lvars[rangeName + side + "_SWITCH"] = range;
  }
}

// --- Fake simulator -------------------------------------------------------------

DispatchProc g_dispatch = nullptr;
std::map<SIMCONNECT_CLIENT_EVENT_ID, std::string> g_systemEvents;
bool g_dataRequested = false;
int g_commandsThisFrame = 0;
float g_frameRate = 60.0f;  // what the Frame event reports

void sendSystemEvent(const char* systemEventName) {
  for (const auto& [id, name] : g_systemEvents) {
    if (name != systemEventName) {
      continue;
    }
    if (name == "AircraftLoaded" || name == "FlightLoaded") {
      // Both carry a file name: the aircraft.cfg or the .FLT file.
      SIMCONNECT_RECV_EVENT_FILENAME event{};
      event.dwID = SIMCONNECT_RECV_ID_EVENT_FILENAME;
      event.uEventID = id;
      const std::string fileName = name == "AircraftLoaded" ? g_aircraft.path : "flights\\other\\MainMenu.FLT";
      std::strncpy(event.szFileName, fileName.c_str(), sizeof(event.szFileName) - 1);
      g_dispatch(&event, sizeof(event), nullptr);
    } else {
      fail(std::string("unexpected system event ") + systemEventName);
    }
  }
}

// The answer to the module's data request, delivered like SimConnect does on a later dispatch.
void sendAircraftData() {
  // SimConnect appends the requested data at dwData, beyond the end of the declared struct.
  alignas(8) unsigned char buffer[sizeof(SIMCONNECT_RECV_SIMOBJECT_DATA) + 512] = {};
  auto* header = reinterpret_cast<SIMCONNECT_RECV_SIMOBJECT_DATA*>(buffer);
  header->dwID = SIMCONNECT_RECV_ID_SIMOBJECT_DATA;
  header->dwRequestID = 0;
  const auto dataOffset = reinterpret_cast<unsigned char*>(&header->dwData) - buffer;
  unsigned char* data = buffer + dataOffset;
  std::strncpy(reinterpret_cast<char*>(data), g_aircraft.title.c_str(), 255);
  const double values[] = {g_aircraft.onGround ? 1.0 : 0.0, g_aircraft.groundSpeedKts, g_aircraft.altitudeAglFt};
  std::memcpy(data + 256, values, sizeof(values));
  g_dispatch(header, sizeof(buffer), nullptr);
}

int g_dataDeliveries = 0;

// One simulator frame at 60 fps.
void frame() {
  g_commandsThisFrame = 0;
  for (const auto& [id, name] : g_systemEvents) {
    if (name == "Frame") {
      SIMCONNECT_RECV_EVENT_FRAME event{};
      event.dwID = SIMCONNECT_RECV_ID_EVENT_FRAME;
      event.uEventID = id;
      event.fFrameRate = g_frameRate;
      event.fSimSpeed = 1.0f;
      g_dispatch(&event, sizeof(event), nullptr);
    }
  }
  if (g_dataRequested) {
    g_dataRequested = false;
    ++g_dataDeliveries;
    sendAircraftData();
  }
}

// One simulated second: 60 frames. The module asks for the aircraft data once per second; the
// fake keeps that at the first frame of every tick (see main), so commands follow in the same tick.
void tick() {
  const int deliveriesBefore = g_dataDeliveries;
  for (int i = 0; i < 60; ++i) {
    frame();
  }
  if (g_dataDeliveries != deliveriesBefore + 1) {
    fail("module requested aircraft data " + std::to_string(g_dataDeliveries - deliveriesBefore) +
         " times in one second");
  }
}

// Runs `seconds` ticks with the given aircraft state; g_commands collects what was sent.
void fly(int seconds, bool onGround, double groundSpeedKts, double altitudeAglFt) {
  g_aircraft.onGround = onGround;
  g_aircraft.groundSpeedKts = groundSpeedKts;
  g_aircraft.altitudeAglFt = altitudeAglFt;
  g_commands.clear();
  for (int i = 0; i < seconds; ++i) {
    tick();
  }
}

void takeOffAndClimb() {
  fly(60, true, 15, 0);    // taxi
  fly(30, true, 140, 0);   // take-off roll
  fly(8, false, 160, 60);  // lift-off, below 100 ft AGL
  fly(30, false, 250, 3000);
}

// --- Checks -------------------------------------------------------------------

std::string join(const std::vector<std::string>& items) {
  std::string text;
  for (const auto& item : items) {
    text += "\n       " + item;
  }
  return text;
}

void expectCommands(const char* label, const std::vector<std::string>& expected) {
  if (g_commands == expected) {
    std::printf("ok   %s\n", label);
    return;
  }
  fail(std::string(label) + "\n     sent:" + join(g_commands) + "\n     expected:" + join(expected));
}

void expectValue(const char* label, double actual, double expected) {
  if (actual == expected) {
    std::printf("ok   %s\n", label);
    return;
  }
  fail(std::string(label) + ": " + std::to_string(actual) + ", expected " + std::to_string(expected));
}

std::string readLog() {
  std::ifstream file(OANS_LOG_PATH);
  std::stringstream text;
  text << file.rdbuf();
  return text.str();
}

void expectLogContains(const char* label, const std::string& log, const std::string& expected) {
  if (log.find(expected) != std::string::npos) {
    std::printf("ok   %s\n", label);
    return;
  }
  fail(std::string(label) + ": log does not contain \"" + expected + "\"");
}

}  // namespace

// --- Fakes for the MSFS API used by the module ------------------------------------

extern "C" {
BOOL execute_calculator_code(PCSTRINGZ code, FLOAT64* fvalue, SINT32*, PCSTRINGZ*) {
  const std::string rpn = code;
  static const std::regex readLVar("\\(L:([A-Za-z0-9_]+)\\)");
  static const std::regex keyEvent("(\\d+) \\(>K:(A32NX\\.[A-Z0-9_]+)\\)");
  static const std::regex writeLVar("(\\d+) \\(>L:([A-Za-z0-9_]+)\\)");
  static const std::regex hEvent("\\(>H:([A-Za-z0-9_]+)\\)");
  std::smatch match;

  if (std::regex_match(rpn, match, readLVar)) {
    if (fvalue == nullptr) {
      fail("read without result pointer: " + rpn);
    } else {
      const auto it = g_lvars.find(match[1].str());
      *fvalue = it != g_lvars.end() ? it->second : 0;
    }
    return 1;
  }

  // Everything else is a command to the aircraft: at most one per frame.
  if (++g_commandsThisFrame > 1) {
    fail("more than one command in one frame: " + rpn);
  }
  g_commands.push_back(rpn);
  if (std::regex_match(rpn, match, keyEvent)) {
    if (g_aircraft.type == Type::FbwA380x) {
      fbwApplyEvent(match[2].str(), std::stoi(match[1].str()));
    }
  } else if (std::regex_match(rpn, match, writeLVar)) {
    if (g_lvars.count(match[2].str()) == 0) {
      fail("write to an L-var the aircraft does not have: " + rpn);
    }
    g_lvars[match[2].str()] = std::stoi(match[1].str());
  } else if (std::regex_match(rpn, match, hEvent)) {
    if (g_aircraft.type == Type::SynapticA220) {
      a220ApplyEvent(match[1].str());
    }
  } else {
    fail("unsupported RPN: \"" + rpn + "\"");
  }
  return 1;
}
ID check_named_variable(PCSTRINGZ name) {
  return g_lvars.count(name) != 0 ? 1 : -1;
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
  if (g_systemEvents.count(eventId) != 0) {
    fail("event id used twice");
  }
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
  for (int i = 0; i < 59; ++i) {
    frame();  // phase: the next frame completes the first second
  }

  // --- Unsupported aircraft -----------------------------------------------------
  loadAircraft(Type::Other, "Asobo Cessna 172", "SimObjects\\Airplanes\\Asobo_C172\\aircraft.cfg");
  sendSystemEvent("AircraftLoaded");
  takeOffAndClimb();
  fly(3, true, 135, 0);
  fly(30, true, 20, 0);
  expectCommands("unsupported aircraft: nothing is sent", {});

  // --- FlyByWire A380X ------------------------------------------------------------
  setUpFbw(0, 5, true);  // ROSE ILS, 10 NM
  sendSystemEvent("AircraftLoaded");
  takeOffAndClimb();
  expectCommands("FBW: nothing during taxi, take-off and climb", {});
  fly(2, true, 140, 0);
  expectCommands("FBW: 2 s on the ground is not yet the landing", {});
  fly(1, true, 130, 0);
  expectCommands("FBW: 3 s on the ground -> captain ND to ARC, ZOOM 2 NM",
                 {"3 (>K:A32NX.FCU_EFIS_L_MODE_SET)", "3 (>K:A32NX.FCU_EFIS_L_RANGE_SET)"});
  fly(1, true, 120, 0);
  expectCommands("FBW: one second later the first officer ND",
                 {"3 (>K:A32NX.FCU_EFIS_R_MODE_SET)", "3 (>K:A32NX.FCU_EFIS_R_RANGE_SET)"});
  expectValue("FBW: left ND mode ARC", g_lvars["A32NX_EFIS_L_ND_MODE"], 3);
  expectValue("FBW: left OANS range 2 NM", g_lvars["A32NX_EFIS_L_OANS_RANGE"], 3);
  expectValue("FBW: right OANS range 2 NM", g_lvars["A32NX_EFIS_R_OANS_RANGE"], 3);
  fly(120, true, 15, 0);
  expectCommands("FBW: nothing more while taxiing in", {});
  {
    const std::string log = readLog();
    expectLogContains("log: module start", log, "OANS Auto Zoom 1.1.0 started");
    expectLogContains("log: unsupported aircraft", log, "\"Asobo Cessna 172\"");
    expectLogContains("log: aircraft recognised", log,
                      "aircraft \"FlyByWire A380X (A380-842)\" (SimObjects\\AirPlanes\\FlyByWire_A380X\\presets"
                      "\\flybywire\\FlyByWire_A380_842\\config\\aircraft.cfg): FlyByWire A380X");
    expectLogContains("log: armed", log, "armed for the next landing");
    expectLogContains("log: touchdown", log, "touchdown at 140 kt");
    expectLogContains("log: landing", log, "FlyByWire A380X: landing confirmed at 130 kt");
    expectLogContains("log: OANS availability", log, "L:A32NX_OANS_AVAILABLE = 1");
    expectLogContains("log: command", log, "send 3 (>K:A32NX.FCU_EFIS_L_MODE_SET)");
    expectLogContains("log: displays before", log,
                      "FBW A380X before: ND L mode 0 (3 = ARC), range 1 (0 = ZOOM), zoom 5 (3 = 2 NM)");
    expectLogContains("log: displays after", log,
                      "FBW A380X after: ND R mode 3 (3 = ARC), range 0 (0 = ZOOM), zoom 3 (3 = 2 NM)");
  }

  // Recognised by the MSFS 2020 package folder even if the livery title does not name it.
  setUpFbw(0, 5, true, "Emirates A6-EUA", "SimObjects\\AirPlanes\\FlyByWire_A380_842\\aircraft.cfg");
  sendSystemEvent("AircraftLoaded");
  takeOffAndClimb();
  fly(3, true, 130, 0);
  expectCommands("FBW: recognised by the FlyByWire_A380_842 folder",
                 {"3 (>K:A32NX.FCU_EFIS_L_MODE_SET)", "3 (>K:A32NX.FCU_EFIS_L_RANGE_SET)"});
  fly(10, true, 60, 0);

  // BTV set up in PLAN with ZOOM 5 NM: only the mode changes, the pilot's zoom stays.
  setUpFbw(4, 4, true);
  takeOffAndClimb();
  fly(5, true, 120, 0);
  expectCommands("FBW: PLAN + ZOOM 5 NM -> ARC, zoom kept",
                 {"3 (>K:A32NX.FCU_EFIS_L_MODE_SET)", "3 (>K:A32NX.FCU_EFIS_R_MODE_SET)"});
  expectValue("FBW: left zoom still 5 NM", g_lvars["A32NX_EFIS_L_OANS_RANGE"], 4);

  // ARC 20 NM: only the range changes.
  setUpFbw(3, 6, true);
  takeOffAndClimb();
  fly(5, true, 120, 0);
  expectCommands("FBW: ARC 20 NM -> ZOOM 2 NM",
                 {"3 (>K:A32NX.FCU_EFIS_L_RANGE_SET)", "3 (>K:A32NX.FCU_EFIS_R_RANGE_SET)"});

  // A bounce does not count; staying on the ground afterwards does.
  setUpFbw(3, 5, true);
  takeOffAndClimb();
  fly(2, true, 135, 0);
  fly(2, false, 130, 8);
  fly(2, true, 125, 0);
  expectCommands("FBW: bounce -> nothing yet", {});
  fly(1, true, 120, 0);
  expectCommands("FBW: 3 s on the ground after the bounce -> ZOOM", {"3 (>K:A32NX.FCU_EFIS_L_RANGE_SET)"});
  fly(10, true, 60, 0);  // first officer side and read-back

  // L:A32NX_OANS_AVAILABLE = 0 (the airport search at aircraft load failed, e.g. AMDB Bridge
  // was not running yet): the displays are switched all the same.
  setUpFbw(0, 5, false);
  takeOffAndClimb();
  fly(10, true, 100, 0);
  expectCommands("FBW: L:A32NX_OANS_AVAILABLE = 0 -> switched anyway",
                 {"3 (>K:A32NX.FCU_EFIS_L_MODE_SET)", "3 (>K:A32NX.FCU_EFIS_L_RANGE_SET)",
                  "3 (>K:A32NX.FCU_EFIS_R_MODE_SET)", "3 (>K:A32NX.FCU_EFIS_R_RANGE_SET)"});
  expectLogContains("log: OANS not available is reported", readLog(), "L:A32NX_OANS_AVAILABLE = 0");

  // Rejected take-off and a short hop below 100 ft do not arm.
  setUpFbw(0, 5, true);
  fly(30, true, 100, 0);
  fly(5, false, 140, 40);
  fly(30, true, 60, 0);
  expectCommands("FBW: rejected take-off -> nothing", {});

  // Flight started in the air, e.g. on final: arms after 15 s.
  setUpFbw(3, 5, true);
  sendSystemEvent("FlightLoaded");
  fly(20, false, 150, 1500);
  fly(4, true, 130, 0);
  expectCommands("FBW: flight started on final",
                 {"3 (>K:A32NX.FCU_EFIS_L_RANGE_SET)", "3 (>K:A32NX.FCU_EFIS_R_RANGE_SET)"});

  // Moved to a gate while armed (no FlightLoaded event): no landing speed, nothing happens.
  setUpFbw(0, 5, true);
  takeOffAndClimb();
  fly(30, true, 0, 0);
  expectCommands("FBW: put on the ground at 0 kt -> nothing", {});
  fly(60, true, 120, 0);
  expectCommands("FBW: ... and it stays disarmed afterwards", {});

  // Quitting in the air and starting a new flight at a gate is no landing.
  setUpFbw(0, 5, true);
  takeOffAndClimb();
  sendSystemEvent("FlightLoaded");
  fly(30, true, 0, 0);
  expectCommands("FBW: new flight loaded at a gate -> nothing", {});

  // --- iniBuilds A350 -------------------------------------------------------------
  setUpA350("Airbus A350-900 iniBuilds", 0, 5, false);  // LS, 10 NM
  sendSystemEvent("AircraftLoaded");
  takeOffAndClimb();
  expectCommands("A350: nothing during taxi, take-off and climb", {});
  fly(3, true, 130, 0);
  expectCommands("A350: landing -> captain ND to ARC, ZOOM 2 NM",
                 {"3 (>L:INI_MAP_MODE_CAPT_SWITCH)", "3 (>L:INI_MAP_RANGE_CAPT_SWITCH)"});
  fly(1, true, 120, 0);
  expectCommands("A350: first officer not yet (2 s apart)", {});
  fly(1, true, 110, 0);
  expectCommands("A350: first officer ND two seconds later",
                 {"3 (>L:INI_MAP_MODE_FO_SWITCH)", "3 (>L:INI_MAP_RANGE_FO_SWITCH)"});
  fly(120, true, 15, 0);
  expectCommands("A350: nothing more while taxiing in", {});

  // Range knob named as in iniBuilds' L-var list.
  setUpA350("A350-1000", 3, 7, true);
  takeOffAndClimb();
  fly(6, true, 120, 0);
  expectCommands("A350: INI_MAP_MODE_RANGE_*_SWITCH naming",
                 {"3 (>L:INI_MAP_MODE_RANGE_CAPT_SWITCH)", "3 (>L:INI_MAP_MODE_RANGE_FO_SWITCH)"});

  // The aircraft's own auto zoom (OIS option) already selected ZOOM: nothing to do.
  setUpA350("A350-900", 3, 5, false);
  takeOffAndClimb();
  fly(2, true, 130, 0);
  g_lvars["INI_MAP_RANGE_CAPT_SWITCH"] = 3;
  g_lvars["INI_MAP_RANGE_FO_SWITCH"] = 3;
  fly(5, true, 100, 0);
  expectCommands("A350: map already shown by the aircraft -> nothing", {});

  // Recognised by the aircraft.cfg path even if the title does not name it.
  setUpA350("Lufthansa D-AIXA", 3, 5, false);
  sendSystemEvent("AircraftLoaded");
  takeOffAndClimb();
  fly(3, true, 130, 0);
  expectCommands("A350: recognised by its aircraft.cfg path", {"3 (>L:INI_MAP_RANGE_CAPT_SWITCH)"});

  // Other iniBuilds aircraft use INI_MAP_RANGE_CAPT_SWITCH with another scale: never touched.
  loadAircraft(Type::IniA320, "A320neo V2", "SimObjects\\Airplanes\\Asobo_A320_NEO\\aircraft.cfg");
  g_lvars["INI_MAP_MODE_CAPT_SWITCH"] = 3;
  g_lvars["INI_MAP_RANGE_CAPT_SWITCH"] = 0;
  sendSystemEvent("AircraftLoaded");
  takeOffAndClimb();
  fly(30, true, 60, 0);
  expectCommands("iniBuilds A320neo V2 with the same L-var names -> nothing", {});

  // A user folder that happens to contain "a350" does not make another aircraft an A350.
  loadAircraft(Type::IniA320, "A320neo V2",
               "d:\\sim\\a350-stuff\\community\\inibuilds-a320\\simobjects\\airplanes\\a320neo\\aircraft.cfg");
  g_lvars["INI_MAP_MODE_CAPT_SWITCH"] = 3;
  g_lvars["INI_MAP_RANGE_CAPT_SWITCH"] = 0;
  sendSystemEvent("AircraftLoaded");
  takeOffAndClimb();
  fly(30, true, 60, 0);
  expectCommands("keyword in the user's install path is ignored", {});

  // --- Synaptic A220 ---------------------------------------------------------------
  loadAircraft(Type::SynapticA220, "A220-300",
               "SimObjects\\Airplanes\\Synaptic_A220\\presets\\inibuilds\\A220-300\\config\\aircraft.cfg");
  sendSystemEvent("AircraftLoaded");
  g_a220Range[1] = 6;                                          // captain 10 NM
  g_a220Range[2] = static_cast<int>(kA220Ranges.size()) - 1;  // first officer at the widest range
  takeOffAndClimb();
  expectCommands("A220: nothing during taxi, take-off and climb", {});
  fly(4, true, 120, 0);
  std::vector<std::string> expected;
  for (int ctp = 1; ctp <= 2; ++ctp) {
    for (int i = 0; i < 30; ++i) {
      expected.push_back("(>H:A220_CTP_RANGE_" + std::to_string(ctp) + "_DEC)");
    }
    for (int i = 0; i < 3; ++i) {
      expected.push_back("(>H:A220_CTP_RANGE_" + std::to_string(ctp) + "_INC)");
    }
  }
  expectCommands("A220: both range knobs to the smallest range, then up to 1 NM", expected);
  expectValue("A220: captain map at 1 NM", g_a220Range[1], 3);
  expectValue("A220: first officer map at 1 NM", g_a220Range[2], 3);
  fly(120, true, 15, 0);
  expectCommands("A220: nothing more while taxiing in", {});

  // Leaving the A220 while the knob sequence is still running stops it.
  takeOffAndClimb();
  fly(2, true, 120, 0);
  g_commands.clear();
  for (int i = 0; i < 6; ++i) {
    frame();  // first frame: third second on the ground, the landing; then five detents
  }
  expectValue("A220: five detents sent in the first frames", static_cast<double>(g_commands.size()), 5);
  loadAircraft(Type::Other, "Asobo Cessna 172", "SimObjects\\Airplanes\\Asobo_C172\\aircraft.cfg");
  sendSystemEvent("AircraftLoaded");
  g_commands.clear();
  for (int i = 0; i < 54; ++i) {
    frame();  // rest of that second
  }
  for (int i = 0; i < 5; ++i) {
    tick();
  }
  expectCommands("A220: remaining detents are dropped when the aircraft changes", {});

  // A bogus frame rate in the Frame event must not stop the once-per-second clock.
  for (const float bogus : {0.0f, -1.0f, 1e9f}) {
    g_frameRate = bogus;
    const int deliveriesBefore = g_dataDeliveries;
    for (int i = 0; i < 90; ++i) {
      frame();
    }
    const int deliveries = g_dataDeliveries - deliveriesBefore;
    if (deliveries < 2 || deliveries > 4) {
      fail("frame rate " + std::to_string(bogus) + ": " + std::to_string(deliveries) +
           " data requests in 90 frames, expected about 3");
    } else {
      std::printf("ok   frame rate %g: data still requested about once per 30 frames\n", bogus);
    }
  }

  module_deinit();

  std::printf("%s (%d failure(s))\n", g_failures == 0 ? "PASSED" : "FAILED", g_failures);
  return g_failures == 0 ? 0 : 1;
}
