// Stand-in for the MSFS SDK header, only for the host test (tests/host/run.sh).
// The real module is built against the headers of the MSFS 2024 SDK.
#pragma once

#define MSFS_CALLBACK __attribute__((visibility("default")))
