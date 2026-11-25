#pragma once
#include <RED4ext/Scripting/Functions.hpp>
namespace gcet::producer {
// Registers two ordinary synchronous native producers in the existing
// RED4ext service. No RunningUpdate hook, retained receiver or polling.
void RegisterProducerReads();
void ResetProducerReads() noexcept;
}
