// Stand-in for the MSFS SDK header, only for the host test (tests/host/run.sh).
#pragma once

typedef unsigned long DWORD;
typedef long HRESULT;
typedef unsigned long long HANDLE;  // as in the MSFS SDK
struct HWND__ {
  int unused;
};
typedef struct HWND__* HWND;
typedef const char* LPCSTR;
typedef int BOOL;

#define S_OK ((HRESULT)0L)
#define SUCCEEDED(hr) (((HRESULT)(hr)) >= 0)
#define FAILED(hr) (((HRESULT)(hr)) < 0)
#define CALLBACK
