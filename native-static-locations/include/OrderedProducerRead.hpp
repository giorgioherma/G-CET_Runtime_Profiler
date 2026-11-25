#pragma once
#include <cstdint>
#include <utility>

namespace gcet::producer {
// Explicit distinction between preflight refusal (author fallback is safe)
// and a failed invocation after the first native operation has started.
enum class ReadResult : int32_t { Unsupported=0, Complete=1, Failed=2 };

template<class Value,class Admit,class Current,class... Readers>
ReadResult ReadOrdered(Value& output,Admit&& admit,Current&& current,Readers&&... readers) {
    if(!admit()||!current())return ReadResult::Unsupported;
    Value temporary{};
    bool started=false;
    auto step=[&](auto&& read) {
        // The previous native call can re-enter CET, invalidate a session or
        // replace the live RTTI registry. Never use an admitted preflight as
        // a lifetime guarantee for later operations in the same batch.
        if(started&&!current())return false;
        started=true;
        return std::forward<decltype(read)>(read)(temporary);
    };
    if(!(step(std::forward<Readers>(readers))&&...)||!current())
        return started?ReadResult::Failed:ReadResult::Unsupported;
    output=std::move(temporary);
    return ReadResult::Complete;
}
}
