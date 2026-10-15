#pragma once
#include <stdint.h>

#if defined(_WIN32)
#  define GCET_EXPORT __declspec(dllexport)
#else
#  define GCET_EXPORT __attribute__((visibility("default")))
#endif

#ifdef __cplusplus
extern "C" {
#endif

typedef struct gcet_action_gate gcet_action_gate;
typedef struct gcet_action_gate_stats {
    uint32_t struct_size;
    uint32_t abi_version;
    uint64_t revision;
    uint64_t lookups;
    uint64_t admitted;
    uint64_t rejected;
    uint64_t fail_open;
    uint32_t exact_actions;
    uint32_t armed;
    uint32_t wildcard;
    uint32_t reserved;
} gcet_action_gate_stats;

// Version 1. This is a decision helper, NOT a CET hook or a replacement ASI.
GCET_EXPORT uint32_t gcet_gate_abi_version(void);
GCET_EXPORT gcet_action_gate* gcet_gate_create(void);
GCET_EXPORT void gcet_gate_destroy(gcet_action_gate* gate);
// A rebuild DISARMS before changing subscriber data; all decisions fail open.
GCET_EXPORT int gcet_gate_begin(gcet_action_gate* gate);
// Copies the name. "*" denotes an active wildcard subscriber.
GCET_EXPORT int gcet_gate_add(gcet_action_gate* gate, const char* exact_action);
// Arm ONLY when the caller proved an exhaustive, current snapshot.
GCET_EXPORT int gcet_gate_seal(gcet_action_gate* gate, int complete_snapshot);
GCET_EXPORT void gcet_gate_disarm(gcet_action_gate* gate);
// Return 1 = deliver (including unknown/error); 0 = safe-to-skip THIS ROUTER.
GCET_EXPORT int gcet_gate_should_deliver(gcet_action_gate* gate, const char* action);
// Stats are diagnostic only. Snapshot counters are NOT native allocation metrics.
GCET_EXPORT int gcet_gate_get_stats(gcet_action_gate* gate, gcet_action_gate_stats* out_stats);

#ifdef __cplusplus
}
#endif
