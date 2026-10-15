#include "gcet_action_gate.h"
#include <cassert>
#include <atomic>
#include <thread>
#include <vector>
#include <cstdio>

int main() {
    assert(gcet_gate_abi_version() == 1);
    assert(gcet_gate_should_deliver(nullptr, "Jump") == 1);
    auto* g = gcet_gate_create();
    assert(g);
    assert(gcet_gate_should_deliver(g, "Jump") == 1); // unarmed
    assert(gcet_gate_begin(g));
    assert(gcet_gate_add(g, "Jump"));
    assert(gcet_gate_add(g, "Jump")); // duplicate does not change set
    assert(gcet_gate_add(g, "Crouch"));
    assert(!gcet_gate_seal(g, 0)); // incomplete never suppresses
    assert(gcet_gate_should_deliver(g, "Unknown") == 1);
    assert(gcet_gate_seal(g, 1));
    assert(gcet_gate_should_deliver(g, "Jump") == 1);
    assert(gcet_gate_should_deliver(g, "Crouch") == 1);
    assert(gcet_gate_should_deliver(g, "Unknown") == 0);
    assert(gcet_gate_should_deliver(g, nullptr) == 1);
    gcet_action_gate_stats st{};
    st.struct_size = sizeof(st);
    assert(gcet_gate_get_stats(g, &st));
    assert(st.armed == 1 && st.exact_actions == 2 && st.rejected == 1);
    assert(st.lookups == st.admitted + st.rejected + st.fail_open);
    assert(gcet_gate_begin(g));
    assert(gcet_gate_should_deliver(g, "Unknown") == 1);
    assert(gcet_gate_add(g, "*"));
    assert(gcet_gate_seal(g, 1));
    assert(gcet_gate_should_deliver(g, "Unknown") == 1);
    gcet_gate_disarm(g);
    assert(gcet_gate_should_deliver(g, "Unknown") == 1);
    assert(gcet_gate_begin(g));
    assert(gcet_gate_add(g, "Interact"));
    assert(gcet_gate_seal(g, 1));
    std::atomic<int> errors{0};
    std::vector<std::thread> workers;
    for (int i = 0; i < 4; ++i) workers.emplace_back([&]() {
        for (int j = 0; j < 20000; ++j) {
            int result = gcet_gate_should_deliver(g, "Interact");
            if (result != 1) ++errors;
        }
    });
    for (int j = 0; j < 20; ++j) {
        gcet_gate_begin(g);
        assert(gcet_gate_should_deliver(g, "Other") == 1);
        gcet_gate_add(g, "Interact");
        gcet_gate_seal(g, 1);
    }
    for (auto& worker : workers) worker.join();
    assert(errors == 0);
    gcet_gate_destroy(g);
    std::puts("gcet_action_gate: all conservative routing and concurrency tests passed");
}
