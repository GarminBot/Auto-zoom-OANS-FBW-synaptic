// Stand-in for the MSFS SDK header, only for the host test (tests/host/run.sh).
// Declares just the legacy gauge API functions used by src/OansAutoZoom.cpp.
#pragma once

#include <MSFS/MSFS_WindowsTypes.h>

typedef double FLOAT64;
typedef int SINT32;
typedef SINT32 ID;
typedef const char* PCSTRINGZ;

extern "C" {
ID register_named_variable(PCSTRINGZ name);
ID check_named_variable(PCSTRINGZ name);
FLOAT64 get_named_variable_value(ID id);
void set_named_variable_value(ID id, FLOAT64 value);
BOOL execute_calculator_code(PCSTRINGZ code, FLOAT64* fvalue, SINT32* ivalue, PCSTRINGZ* svalue);
}
