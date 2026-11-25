#include "ProducerReads.hpp"
#include "OrderedProducerRead.hpp"
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

namespace gcet::producer {
namespace {
using namespace RED4ext;
using IntKey=game::bb::ScriptID_Int32;
using BoolKey=game::bb::ScriptID_Bool;
using Result=ReadResult;
using Receiver=Handle<IScriptable>;

// Resolve the same short-name call as CET, but refuse overloads or a
// mismatched signature before invoking ANY getter. Do not remember RTTI
// pointers across calls, sessions, source changes or CET reload.
CBaseFunction* Resolve(CClass* cls,CName name,bool isStatic,
                      rtti::IType* result,rtti::IType* param=nullptr) {
    if(!cls||!result)return nullptr;
    CBaseFunction* candidate=nullptr;
    auto visit=[&](CBaseFunction* f){
        if(!f||f->shortName!=name)return true;
        if(candidate){
            // Inherited same-full-name implementation is shadowed by
            // the most-derived class, just as CET's overload resolver.
            return candidate->fullName==f->fullName;
        }
        candidate=f;
        return true;
    };
    for(auto* cur=cls;cur;cur=cur->parent) {
        if(isStatic){
            for(auto* f:cur->staticFuncs)if(!visit(f))return nullptr;
        }else{
            for(auto* f:cur->funcs)if(!visit(f))return nullptr;
        }
    }
    if(!candidate||!candidate->flags.isNative||candidate->flags.isStatic!=isStatic||
       !candidate->returnType||candidate->returnType->type!=result||
       candidate->params.Size()!=(param?1u:0u))return nullptr;
    if(param){
        auto* p=candidate->params[0];
        if(!p||p->type!=param||p->flags.isOut||p->flags.isOptional)return nullptr;
    }
    return candidate;
}

template<class Value>
bool Read(CBaseFunction* fn,void* receiver,rtti::IType* type,Value& result) {
    CStackType ret(type,&result);
    CStack stack(receiver,nullptr,0,&ret);
    return fn->Execute(&stack);
}
template<class Value,class Arg>
bool Read(CBaseFunction* fn,void* receiver,rtti::IType* type,Value& result,
          rtti::IType* argType,Arg& value) {
    CStackType arg(argType,&value),ret(type,&result);
    CStack stack(receiver,&arg,1,&ret);
    return fn->Execute(&stack);
}

struct Pose {Vector4 position{};Quaternion orientation{};EulerAngles euler{};};
void ReadPose(IScriptable*,CStackFrame* frame,int32_t* out,int64_t) {
    Receiver receiver;
    Vector4* pos=nullptr;Quaternion* orientation=nullptr;float* yaw=nullptr;
    GetParameter(frame,&receiver);
    GetParameter(frame,&pos);GetParameter(frame,&orientation);GetParameter(frame,&yaw);
    frame->code++;
    if(!out)return;
    *out=static_cast<int32_t>(Result::Unsupported);
    if(!receiver.instance||!pos||!orientation||!yaw)return;
    auto* rtti=CRTTISystem::Get();
    auto* cls=receiver.instance->GetType();
    if(!rtti||!cls)return;
    auto* vec=rtti->GetType("Vector4");
    auto* quat=rtti->GetType("Quaternion");
    auto* angles=rtti->GetType("EulerAngles");
    auto* posFn=Resolve(cls,"GetWorldPosition",false,vec);
    auto* oriFn=Resolve(cls,"GetWorldOrientation",false,quat);
    auto* yawFn=Resolve(rtti->GetClass("Quaternion"),"ToEulerAngles",true,angles,quat);
    Pose sample;
    auto result=ReadOrdered(sample,[&]{return posFn&&oriFn&&yawFn;},
        [&](Pose& p){return Read(posFn,receiver.instance,vec,p.position);},
        [&](Pose& p){return Read(oriFn,receiver.instance,quat,p.orientation);},
        [&](Pose& p){return Read(yawFn,nullptr,angles,p.euler,quat,p.orientation);});
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
    if(!receiver.instance)return;
    for(size_t i=0;i<(LastBool?3u:4u);++i)if(!values[i])return;
    if constexpr(LastBool)if(!boolValue)return;
    auto* rtti=CRTTISystem::Get();
    auto* cls=receiver.instance->GetType();
    if(!rtti||!cls)return;
    auto* ints=rtti->GetType("Int32");
    auto* intType=rtti->GetType(IntKey::NAME);
    auto* intFn=Resolve(cls,"GetInt",false,ints,intType);
    auto* boolType=rtti->GetType(BoolKey::NAME);
    auto* bools=rtti->GetType("Bool");
    auto* boolFn=LastBool?Resolve(cls,"GetBool",false,bools,boolType):nullptr;
    struct Values {std::array<int32_t,4> ints{};bool last=false;} sample;
    auto result=ReadOrdered(sample,[&]{return intFn&&intType&&(!LastBool||(boolFn&&boolType));},
        [&](Values& v){return Read(intFn,receiver.instance,ints,v.ints[0],intType,keys[0]);},
        [&](Values& v){return Read(intFn,receiver.instance,ints,v.ints[1],intType,keys[1]);},
        [&](Values& v){return Read(intFn,receiver.instance,ints,v.ints[2],intType,keys[2]);},
        [&](Values& v){
            if constexpr(LastBool)return Read(boolFn,receiver.instance,bools,v.last,boolType,boolKey);
            else return Read(intFn,receiver.instance,ints,v.ints[3],intType,keys[3]);
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
void RegisterProducerReads(){
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
