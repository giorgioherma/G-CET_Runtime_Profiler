// Isolated native C ABI lookup benchmark. NOT a CET marshalling or FPS benchmark.
#include "gcet_action_gate.h"
#include <chrono>
#include <cstdint>
#include <cstdio>
#include <cstdlib>

static constexpr uint64_t trials = 2'000'000;
static void run(const char* label, gcet_action_gate* gate, const char* action, int expected) {
    uint64_t delivered = 0;
    const auto begin = std::chrono::steady_clock::now();
    for (uint64_t i = 0; i < trials; ++i) {
        delivered += static_cast<uint64_t>(gcet_gate_should_deliver(gate, action));
    }
    const auto end = std::chrono::steady_clock::now();
    if (delivered != trials * static_cast<uint64_t>(expected)) {
        std::fprintf(stderr, "Unexpected gate decision for %s\n", label);
        std::exit(2);
    }
    const auto ns = std::chrono::duration<double, std::nano>(end - begin).count() / static_cast<double>(trials);
    std::printf("%-20s %8.1f ns/lookup (N=%llu)\n", label, ns, static_cast<unsigned long long>(trials));
}
int main() {
    auto* gate = gcet_gate_create();
    if (!gate) return 2;
    run("unarmed fail-open", gate, "Jump", 1);
    if (!gcet_gate_begin(gate) || !gcet_gate_add(gate, "Jump") || !gcet_gate_add(gate, "Crouch") || !gcet_gate_seal(gate, 1)) return 2;
    run("exact action hit", gate, "Jump", 1);
    run("exact action miss", gate, "UnrelatedAction", 0);
    if (!gcet_gate_begin(gate) || !gcet_gate_add(gate, "*") || !gcet_gate_seal(gate, 1)) return 2;
    run("wildcard fail-open", gate, "UnrelatedAction", 1);
    gcet_gate_destroy(gate);
    std::puts("Diagnostic only: these times exclude CET event dispatch, CName decoding, argument marshalling, and Lua execution.");
    return 0;
}
