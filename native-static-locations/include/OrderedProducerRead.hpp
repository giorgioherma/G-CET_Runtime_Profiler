#pragma once
#include <cstdint>
#include <utility>

namespace gcet::producer {
// Explicit distinction between preflight refusal (author fallback is safe)
// and a failed invocation after the first native operation has started.
enum class ReadResult : int32_t { Unsupported=0, Complete=1, Failed=2 };

template<class Value,class Admit,class... Readers>
ReadResult ReadOrdered(Value& output,Admit&& admit,Readers&&... readers) {
    if(!admit())return ReadResult::Unsupported;
    Value temporary{};
    // Short circuit in original author order. Publish nothing if ANY read
    // fails; never replay the successfully executed prefix.
    if(!(std::forward<Readers>(readers)(temporary)&&...))
        return ReadResult::Failed;
    output=std::move(temporary);
    return ReadResult::Complete;
}
}
