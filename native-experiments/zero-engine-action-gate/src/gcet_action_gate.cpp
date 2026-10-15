#include "gcet_action_gate.h"
#include <atomic>
#include <cstring>
#include <mutex>
#include <shared_mutex>
#include <string>
#include <string_view>
#include <unordered_set>

namespace {
struct Hash {
    using is_transparent = void;
    size_t operator()(std::string_view v) const noexcept { return std::hash<std::string_view>{}(v); }
    size_t operator()(const std::string& v) const noexcept { return operator()(std::string_view(v)); }
};
struct Equal {
    using is_transparent = void;
    bool operator()(std::string_view a, std::string_view b) const noexcept { return a == b; }
};
} // namespace

struct gcet_action_gate {
    mutable std::shared_mutex lock;
    std::unordered_set<std::string, Hash, Equal> exact;
    bool armed = false;
    bool wildcard = true;
    uint64_t revision = 0;
    std::atomic<uint64_t> lookups{0}, admitted{0}, rejected{0}, fail_open{0};
};

extern "C" {
uint32_t gcet_gate_abi_version(void) { return 1; }

gcet_action_gate* gcet_gate_create(void) {
    try { return new gcet_action_gate; } catch (...) { return nullptr; }
}
void gcet_gate_destroy(gcet_action_gate* g) { delete g; }

int gcet_gate_begin(gcet_action_gate* g) {
    if (!g) return 0;
    try {
        std::unique_lock<std::shared_mutex> guard(g->lock);
        g->armed = false;
        g->wildcard = false;
        g->exact.clear();
        ++g->revision;
        return 1;
    } catch (...) { return 0; }
}
int gcet_gate_add(gcet_action_gate* g, const char* name) {
    if (!g) return 0;
    try {
        std::unique_lock<std::shared_mutex> guard(g->lock);
        g->armed = false; // Never continue using an out-of-date index.
        if (!name || !name[0]) { g->wildcard = true; return 0; }
        if (std::strcmp(name, "*") == 0) { g->wildcard = true; return 1; }
        g->exact.emplace(name);
        return 1;
    } catch (...) { gcet_gate_disarm(g); return 0; }
}
int gcet_gate_seal(gcet_action_gate* g, int complete) {
    if (!g) return 0;
    try {
        std::unique_lock<std::shared_mutex> guard(g->lock);
        g->armed = (complete == 1);
        return g->armed ? 1 : 0;
    } catch (...) { gcet_gate_disarm(g); return 0; }
}
void gcet_gate_disarm(gcet_action_gate* g) {
    if (!g) return;
    try { std::unique_lock<std::shared_mutex> guard(g->lock); g->armed = false; } catch (...) {}
}
int gcet_gate_should_deliver(gcet_action_gate* g, const char* action) {
    if (!g) return 1;
    g->lookups.fetch_add(1, std::memory_order_relaxed);
    try {
        std::shared_lock<std::shared_mutex> guard(g->lock);
        // Never suppress an unknown name, incomplete subscription state or wildcard.
        if (!g->armed || g->wildcard || !action || !action[0]) {
            g->fail_open.fetch_add(1, std::memory_order_relaxed);
            return 1;
        }
        if (g->exact.find(action) != g->exact.end()) {
            g->admitted.fetch_add(1, std::memory_order_relaxed);
            return 1;
        }
        g->rejected.fetch_add(1, std::memory_order_relaxed);
        return 0;
    } catch (...) { g->fail_open.fetch_add(1, std::memory_order_relaxed); return 1; }
}
int gcet_gate_get_stats(gcet_action_gate* g, gcet_action_gate_stats* o) {
    if (!g || !o || o->struct_size != sizeof(gcet_action_gate_stats)) return 0;
    try {
        std::shared_lock<std::shared_mutex> guard(g->lock);
        o->abi_version = 1;
        o->revision = g->revision;
        o->lookups = g->lookups.load(std::memory_order_relaxed);
        o->admitted = g->admitted.load(std::memory_order_relaxed);
        o->rejected = g->rejected.load(std::memory_order_relaxed);
        o->fail_open = g->fail_open.load(std::memory_order_relaxed);
        o->exact_actions = static_cast<uint32_t>(g->exact.size());
        o->armed = g->armed ? 1u : 0u;
        o->wildcard = g->wildcard ? 1u : 0u;
        o->reserved = 0;
        return 1;
    } catch (...) { return 0; }
}
} // extern "C"
