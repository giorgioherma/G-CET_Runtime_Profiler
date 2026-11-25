#include "ProducerReads.hpp"
#include "OrderedProducerRead.hpp"
#include "MethodSlot.hpp"
#include <RED4ext/RED4ext.hpp>
#include <RED4ext/RTTITypes.hpp>
#include <RED4ext/Scripting/CProperty.hpp>
#include <RED4ext/Scripting/IScriptable.hpp>
#include <RED4ext/Scripting/Utils.hpp>
#include <RED4ext/Scripting/Stack.hpp>
#include <RED4ext/Scripting/Natives/Generated/Vector4.hpp>
#include <RED4ext/Scripting/Natives/Generated/Quaternion.hpp>
#include <RED4ext/Scripting/Natives/Generated/EulerAngles.hpp>
#include <RED4ext/Scripting/Natives/Generated/game/bb/ScriptID_Int32.hpp>
#include <RED4ext/Scripting/Natives/Generated/game/bb/ScriptID_Bool.hpp>
#include <array>
#include <initializer_list>
#include <atomic>
#include <cstring>
#include <RED4ext/Detail/AddressHashes.hpp>
#include <RED4ext/Relocation.hpp>

namespace gcet::producer {
namespace {
using namespace RED4ext;
using IntKey=game::bb::ScriptID_Int32;
using BoolKey=game::bb::ScriptID_Bool;
using Result=ReadResult;
using Receiver=Handle<IScriptable>;

struct MethodAdapter {
    static CClass* Parent(CClass* cls){return cls->parent;}
    static uint32_t Size(CClass* cls,bool isStatic){
        return isStatic?cls->staticFuncs.Size():cls->funcs.Size();
    }
    static const void* Storage(CClass* cls,bool isStatic){
        if(!Size(cls,isStatic))return nullptr;
        return isStatic?static_cast<const void*>(&cls->staticFuncs[0]):
                        static_cast<const void*>(&cls->funcs[0]);
    }
    static CBaseFunction* At(CClass* cls,bool isStatic,uint32_t index){
        return isStatic?static_cast<CBaseFunction*>(cls->staticFuncs[index]):
                        static_cast<CBaseFunction*>(cls->funcs[index]);
    }
};
using Slot=MethodSlot<CClass,CBaseFunction,MethodAdapter>;
std::atomic<uint64_t> g_generation{1};
struct Bindings {
    uint64_t generation=0;
    CRTTISystem* registry=nullptr;
    rtti::IType *vec=nullptr,*quat=nullptr,*angles=nullptr,*ints=nullptr,*intKey=nullptr,*bools=nullptr,*boolKey=nullptr;
    CClass* quaternionClass=nullptr;
    Slot position,orientation,euler,readInt,readBool;
    void Reset(CRTTISystem* current,uint64_t currentGeneration){
        generation=currentGeneration;registry=current;
        vec=current->GetType("Vector4");quat=current->GetType("Quaternion");
        angles=current->GetType("EulerAngles");quaternionClass=current->GetClass("Quaternion");
        ints=current->GetType("Int32");intKey=current->GetType(IntKey::NAME);
        bools=current->GetType("Bool");boolKey=current->GetType(BoolKey::NAME);
        position.Reset();orientation.Reset();euler.Reset();readInt.Reset();readBool.Reset();
    }
};
thread_local Bindings g_bindings;
Bindings* CurrentBindings(){
    auto* rtti=CRTTISystem::Get();
    if(!rtti)return nullptr;
    auto epoch=g_generation.load(std::memory_order_acquire);
    if(g_bindings.registry!=rtti||g_bindings.generation!=epoch)
        g_bindings.Reset(rtti,epoch);
    return &g_bindings;
}

// CET resolves a short name once and caches its exact function identity.
// Resolve only when the currently bound class/metadata lineage or exact
// signature changes, not by scanning the whole class on every producer read.
// CET swaps the *contents* of the existing descriptor for observers;
// invoke that live descriptor via the original CET internal call path.
CBaseFunction* Resolve(Slot& lease,CClass* cls,CName name,bool isStatic,
                      rtti::IType* result,rtti::IType* param=nullptr) {
    if(!cls||!result)return nullptr;
    auto check=[&](CBaseFunction* f){
        if(!f||f->shortName!=name||!f->flags.isNative||f->flags.isStatic!=isStatic||
           !f->returnType||f->returnType->type!=result||
           f->params.Size()!=(param?1u:0u))return false;
        if(param){
            auto* p=f->params[0];
            if(!p||p->type!=param||p->flags.isOut||p->flags.isOptional)return false;
        }
        return true;
    };
    if(auto* cached=lease.Get(cls,isStatic,check))return cached;
    lease.Reset();
    CBaseFunction* candidate=nullptr;
    CClass* owner=nullptr;
    uint32_t slot=0;
    for(auto* cur=cls;cur;cur=cur->parent){
        const auto size=MethodAdapter::Size(cur,isStatic);
        for(uint32_t i=0;i<size;++i){
            auto* f=MethodAdapter::At(cur,isStatic,i);
            if(!f||f->shortName!=name)continue;
            if(candidate){
                if(candidate->fullName!=f->fullName)return nullptr;
                continue;
            }
            candidate=f;owner=cur;slot=i;
        }
    }
    if(!check(candidate)||!lease.Bind(cls,isStatic,owner,slot,candidate))return nullptr;
    return candidate;
}

// Actual CET 1.37.1 RTTIHelper::ExecuteFunction uses ParamOp 27,
// ParamEndOp 38, CBaseFunction_InternalExecute, and the existing CET
// dummy caller/context. Reuse those call semantics, not SDK Execute()
// (which selects a different native/script stack path). Our own global
// call already received the real CET dummy caller and static context.
// No new dummy entity, synthetic RTTI function, heap argument vector,
// or bypass of the currently installed CET trampoline.
using InternalCall=bool(*)(CBaseFunction*,IScriptable*,CStackFrame*,void*,void*);
bool Invoke(CBaseFunction* fn,IScriptable* receiver,CBaseFunction* caller,
            rtti::IType* result,void* out,rtti::IType* param=nullptr,void* value=nullptr){
    if(!fn||!receiver||!caller||!result||!out)return false;
    std::array<char,1+2*sizeof(void*)+1> code{};
    if(param){
        if(!value)return false;
        code[0]=27;
        std::memcpy(code.data()+1,&param,sizeof(void*));
        std::memcpy(code.data()+1+sizeof(void*),&value,sizeof(void*));
        code[1+2*sizeof(void*)]=38;
    }else code[0]=38;
    CStackFrame frame(nullptr,code.data());
    frame.func=caller;
    static UniversalRelocFunc<InternalCall> call(Detail::AddressHashes::CBaseFunction_InternalExecute);
    return call(fn,receiver,&frame,out,result);
}
template<class Value>
bool Read(CBaseFunction* fn,IScriptable* receiver,CBaseFunction* caller,rtti::IType* type,Value& result) {
    return Invoke(fn,receiver,caller,type,&result);
}
template<class Value,class Arg>
bool Read(CBaseFunction* fn,IScriptable* receiver,CBaseFunction* caller,rtti::IType* type,Value& result,
          rtti::IType* argType,Arg& value) {
    return Invoke(fn,receiver,caller,type,&result,argType,&value);
}

struct Pose {Vector4 position{};Quaternion orientation{};EulerAngles euler{};};
void ReadPose(IScriptable* context,CStackFrame* frame,int32_t* out,int64_t) {
    Receiver receiver;
    Vector4* pos=nullptr;Quaternion* orientation=nullptr;float* yaw=nullptr;
    GetParameter(frame,&receiver);
    GetParameter(frame,&pos);GetParameter(frame,&orientation);GetParameter(frame,&yaw);
    frame->code++;
    if(!out)return;
    *out=static_cast<int32_t>(Result::Unsupported);
    if(!receiver.instance||!pos||!orientation||!yaw||!context||!frame->func)return;
    auto* b=CurrentBindings();
    auto* cls=receiver.instance->GetType();
    if(!b||!cls||!b->vec||!b->quat||!b->angles)return;
    auto* posFn=Resolve(b->position,cls,"GetWorldPosition",false,b->vec);
    auto* oriFn=Resolve(b->orientation,cls,"GetWorldOrientation",false,b->quat);
    auto* yawFn=Resolve(b->euler,b->quaternionClass,"ToEulerAngles",true,b->angles,b->quat);
    Pose sample;
    auto result=ReadOrdered(sample,[&]{return posFn&&oriFn&&yawFn;},
        [&](Pose& p){return Read(posFn,receiver.instance,frame->func,b->vec,p.position);},
        [&](Pose& p){return Read(oriFn,receiver.instance,frame->func,b->quat,p.orientation);},
        [&](Pose& p){return Read(yawFn,context,frame->func,b->angles,p.euler,b->quat,p.orientation);});
    *out=static_cast<int32_t>(result);
    if(result==Result::Complete){*pos=sample.position;*orientation=sample.orientation;*yaw=sample.euler.Yaw;}
}

// Four values at most per crossing. CET's actual RTTI marshalling has a
// 264-byte opcode buffer; a single 25-argument PSM call would exceed it.
// Accept the original typed keys by value, not guessed key layout, fixed
// mod names, persistent blackboard handles or a second key cache.
template<bool LastBool>
void ReadBoard(IScriptable*,CStackFrame* frame,int32_t* out,int64_t) {
    Receiver receiver;
    std::array<IntKey,4> keys{};
    BoolKey boolKey{};
    std::array<int32_t*,4> values{};
    bool* boolValue=nullptr;
    GetParameter(frame,&receiver);
    for(size_t i=0;i<(LastBool?3u:4u);++i)GetParameter(frame,&keys[i]);
    if constexpr(LastBool)GetParameter(frame,&boolKey);
    for(size_t i=0;i<(LastBool?3u:4u);++i)GetParameter(frame,&values[i]);
    if constexpr(LastBool)GetParameter(frame,&boolValue);
    frame->code++;
    if(!out)return;
    *out=static_cast<int32_t>(Result::Unsupported);
    if(!receiver.instance||!frame->func)return;
    for(size_t i=0;i<(LastBool?3u:4u);++i)if(!values[i])return;
    if constexpr(LastBool)if(!boolValue)return;
    auto* b=CurrentBindings();
    auto* cls=receiver.instance->GetType();
    if(!b||!cls||!b->ints||!b->intKey)return;
    if constexpr(LastBool)if(!b->bools||!b->boolKey)return;
    auto* intFn=Resolve(b->readInt,cls,"GetInt",false,b->ints,b->intKey);
    auto* boolFn=LastBool?Resolve(b->readBool,cls,"GetBool",false,b->bools,b->boolKey):nullptr;
    struct Values {std::array<int32_t,4> ints{};bool last=false;} sample;
    auto result=ReadOrdered(sample,[&]{return intFn&&(!LastBool||boolFn);},
        [&](Values& v){return Read(intFn,receiver.instance,frame->func,b->ints,v.ints[0],b->intKey,keys[0]);},
        [&](Values& v){return Read(intFn,receiver.instance,frame->func,b->ints,v.ints[1],b->intKey,keys[1]);},
        [&](Values& v){return Read(intFn,receiver.instance,frame->func,b->ints,v.ints[2],b->intKey,keys[2]);},
        [&](Values& v){
            if constexpr(LastBool)return Read(boolFn,receiver.instance,frame->func,b->bools,v.last,b->boolKey,boolKey);
            else return Read(intFn,receiver.instance,frame->func,b->ints,v.ints[3],b->intKey,keys[3]);
        });
    *out=static_cast<int32_t>(result);
    if(result!=Result::Complete)return;
    for(size_t i=0;i<(LastBool?3u:4u);++i)*values[i]=sample.ints[i];
    if constexpr(LastBool)*boolValue=sample.last;
}

struct Arg {const char* type;const char* name;bool out=false;};
template<class T>
void Register(const char* name,T handler,std::initializer_list<Arg> args) {
    auto* rtti=CRTTISystem::Get();
    if(!rtti||!rtti->GetType("Int32"))return;
    for(const auto& a:args)if(!rtti->GetType(a.type))return;
    // CET's own 264-byte marshalling buffer fits at most 15 ordinary
    // 17-byte parameters and the ParamEnd opcode.
    if(args.size()>15)return;
    auto* fn=CGlobalFunction::Create(name,name,handler);
    if(!fn)return;
    fn->flags={.isNative=true,.isStatic=true};
    if(!fn->SetReturnType("Int32"))return;
    for(const auto& a:args)if(!fn->AddParam(a.type,a.name,a.out))return;
    rtti->RegisterFunction(fn);
}
}
void ResetProducerReads() noexcept {g_generation.fetch_add(1,std::memory_order_acq_rel);}
void RegisterProducerReads(){
    ResetProducerReads();
    Register("GCETReadPlayerPoseV1",ReadPose,{
        {"handle:entEntity","player"},{"Vector4","position",true},
        {"Quaternion","orientation",true},{"Float","yaw",true}});
    Register("GCETReadBlackboard4IntV1",ReadBoard<false>,{
        {"handle:gameIBlackboard","board"},
        {IntKey::NAME,"key1"},{IntKey::NAME,"key2"},{IntKey::NAME,"key3"},{IntKey::NAME,"key4"},
        {"Int32","value1",true},{"Int32","value2",true},{"Int32","value3",true},{"Int32","value4",true}});
    Register("GCETReadBlackboard3IntBoolV1",ReadBoard<true>,{
        {"handle:gameIBlackboard","board"},
        {IntKey::NAME,"key1"},{IntKey::NAME,"key2"},{IntKey::NAME,"key3"},{BoolKey::NAME,"key4"},
        {"Int32","value1",true},{"Int32","value2",true},{"Int32","value3",true},{"Bool","value4",true}});
}
}
