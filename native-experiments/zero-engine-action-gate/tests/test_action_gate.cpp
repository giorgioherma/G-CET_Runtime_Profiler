#include "gcet_action_gate.h"
#include <cstdlib>
#define CHECK(expr) do { if (!(expr)) { std::fprintf(stderr, "CHECK failed %s:%d: %s\\n", __FILE__, __LINE__, #expr); std::abort(); } } while (false)
#include <atomic>
#include <thread>
#include <vector>
#include <cstdio>

int main() {
    CHECK(gcet_gate_abi_version() == 1);
    CHECK(gcet_gate_should_deliver(nullptr, "Jump") == 1);
    auto* g = gcet_gate_create();
    CHECK(g);
    CHECK(gcet_gate_should_deliver(g, "Jump") == 1); // unarmed
    CHECK(gcet_gate_begin(g));
    CHECK(gcet_gate_add(g, "Jump"));
    CHECK(gcet_gate_add(g, "Jump")); // duplicate does not change set
    CHECK(gcet_gate_add(g, "Crouch"));
    CHECK(!gcet_gate_seal(g, 0)); // incomplete never suppresses
    CHECK(gcet_gate_should_deliver(g, "Unknown") == 1);
    CHECK(gcet_gate_seal(g, 1));
    CHECK(gcet_gate_should_deliver(g, "Jump") == 1);
    CHECK(gcet_gate_should_deliver(g, "Crouch") == 1);
    CHECK(gcet_gate_should_deliver(g, "Unknown") == 0);
    CHECK(gcet_gate_should_deliver(g, nullptr) == 1);
    gcet_action_gate_stats st{};
    st.struct_size = sizeof(st);
    CHECK(gcet_gate_get_stats(g, &st));
    CHECK(st.armed == 1 && st.exact_actions == 2 && st.rejected == 1);
    CHECK(st.lookups == st.admitted + st.rejected + st.fail_open);
    CHECK(gcet_gate_begin(g));
    CHECK(gcet_gate_should_deliver(g, "Unknown") == 1);
    CHECK(gcet_gate_add(g, "*"));
    CHECK(gcet_gate_seal(g, 1));
    CHECK(gcet_gate_should_deliver(g, "Unknown") == 1);
    gcet_gate_disarm(g);
    CHECK(gcet_gate_should_deliver(g, "Unknown") == 1);
    CHECK(gcet_gate_begin(g));
    CHECK(gcet_gate_add(g, "Interact"));
    CHECK(gcet_gate_seal(g, 1));
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
        CHECK(gcet_gate_should_deliver(g, "Other") == 1);
        gcet_gate_add(g, "Interact");
        gcet_gate_seal(g, 1);
    }
    for (auto& worker : workers) worker.join();
    CHECK(errors == 0);
    gcet_gate_destroy(g);
    std::puts("gcet_action_gate: all conservative routing and concurrency tests passed");
}
