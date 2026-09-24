// Stand-in for SimConnect.h, only for the host test (tests/host/run.sh).
// Mirrors the signatures of the functions and types used by src/OansAutoZoom.cpp.
#pragma once

#include <MSFS/MSFS_WindowsTypes.h>

typedef DWORD SIMCONNECT_OBJECT_ID;
typedef DWORD SIMCONNECT_CLIENT_EVENT_ID;
typedef DWORD SIMCONNECT_NOTIFICATION_GROUP_ID;
typedef DWORD SIMCONNECT_DATA_DEFINITION_ID;
typedef DWORD SIMCONNECT_DATA_REQUEST_ID;
typedef DWORD SIMCONNECT_EVENT_FLAG;
typedef DWORD SIMCONNECT_DATA_REQUEST_FLAG;

static const DWORD SIMCONNECT_UNUSED = 0xFFFFFFFF;
static const DWORD SIMCONNECT_OBJECT_ID_USER = 0;
static const DWORD SIMCONNECT_GROUP_PRIORITY_HIGHEST = 1;
static const DWORD SIMCONNECT_EVENT_FLAG_GROUPID_IS_PRIORITY = 0x00000010;

enum SIMCONNECT_RECV_ID {
  SIMCONNECT_RECV_ID_NULL,
  SIMCONNECT_RECV_ID_EXCEPTION,
  SIMCONNECT_RECV_ID_OPEN,
  SIMCONNECT_RECV_ID_QUIT,
  SIMCONNECT_RECV_ID_EVENT,
  SIMCONNECT_RECV_ID_EVENT_OBJECT_ADDREMOVE,
  SIMCONNECT_RECV_ID_EVENT_FILENAME,
  SIMCONNECT_RECV_ID_EVENT_FRAME,
  SIMCONNECT_RECV_ID_SIMOBJECT_DATA,
};

enum SIMCONNECT_DATATYPE {
  SIMCONNECT_DATATYPE_INVALID,
  SIMCONNECT_DATATYPE_INT32,
  SIMCONNECT_DATATYPE_INT64,
  SIMCONNECT_DATATYPE_FLOAT32,
  SIMCONNECT_DATATYPE_FLOAT64,
  SIMCONNECT_DATATYPE_STRING8,
  SIMCONNECT_DATATYPE_STRING32,
  SIMCONNECT_DATATYPE_STRING64,
  SIMCONNECT_DATATYPE_STRING128,
  SIMCONNECT_DATATYPE_STRING256,
};

enum SIMCONNECT_PERIOD {
  SIMCONNECT_PERIOD_NEVER,
  SIMCONNECT_PERIOD_ONCE,
  SIMCONNECT_PERIOD_VISUAL_FRAME,
  SIMCONNECT_PERIOD_SIM_FRAME,
  SIMCONNECT_PERIOD_SECOND,
};

struct SIMCONNECT_RECV {
  DWORD dwSize;
  DWORD dwVersion;
  DWORD dwID;
};

struct SIMCONNECT_RECV_EXCEPTION : public SIMCONNECT_RECV {
  DWORD dwException;
  DWORD dwSendID;
  DWORD dwIndex;
};

struct SIMCONNECT_RECV_SIMOBJECT_DATA : public SIMCONNECT_RECV {
  DWORD dwRequestID;
  DWORD dwObjectID;
  DWORD dwDefineID;
  DWORD dwFlags;
  DWORD dwentrynumber;
  DWORD dwoutof;
  DWORD dwDefineCount;
  DWORD dwData;
};

typedef void (CALLBACK* DispatchProc)(SIMCONNECT_RECV* pData, DWORD cbData, void* pContext);

#define SIMCONNECTAPI extern "C" HRESULT

SIMCONNECTAPI SimConnect_Open(HANDLE* phSimConnect, LPCSTR szName, HWND hWnd, DWORD UserEventWin32,
                              HANDLE hEventHandle, DWORD ConfigIndex);
SIMCONNECTAPI SimConnect_Close(HANDLE hSimConnect);
SIMCONNECTAPI SimConnect_AddToDataDefinition(HANDLE hSimConnect, SIMCONNECT_DATA_DEFINITION_ID DefineID,
                                             const char* DatumName, const char* UnitsName,
                                             SIMCONNECT_DATATYPE DatumType = SIMCONNECT_DATATYPE_FLOAT64,
                                             float fEpsilon = 0, DWORD DatumID = SIMCONNECT_UNUSED);
SIMCONNECTAPI SimConnect_RequestDataOnSimObject(HANDLE hSimConnect, SIMCONNECT_DATA_REQUEST_ID RequestID,
                                                SIMCONNECT_DATA_DEFINITION_ID DefineID, SIMCONNECT_OBJECT_ID ObjectID,
                                                SIMCONNECT_PERIOD Period, SIMCONNECT_DATA_REQUEST_FLAG Flags = 0,
                                                DWORD origin = 0, DWORD interval = 0, DWORD limit = 0);
SIMCONNECTAPI SimConnect_MapClientEventToSimEvent(HANDLE hSimConnect, SIMCONNECT_CLIENT_EVENT_ID EventID,
                                                  const char* EventName = "");
SIMCONNECTAPI SimConnect_TransmitClientEvent(HANDLE hSimConnect, SIMCONNECT_OBJECT_ID ObjectID,
                                             SIMCONNECT_CLIENT_EVENT_ID EventID, DWORD dwData,
                                             SIMCONNECT_NOTIFICATION_GROUP_ID GroupID, SIMCONNECT_EVENT_FLAG Flags);
SIMCONNECTAPI SimConnect_CallDispatch(HANDLE hSimConnect, DispatchProc pfcnDispatch, void* pContext);
