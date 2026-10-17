// EXPERIMENTAL ONLY. Source-proven adapters opt in; no auto mod patching.
// Requires GRedRuntime 0.6 scheduler and G-CET-StaticLocations native declarations.
module GRedRuntime

public class GCETStaticLocationListener extends IScriptable {
  public func OnGCETStaticNear(owner: Int32, anchor: Int32, initial: Bool) -> Void {}
  public func OnGCETStaticFar(owner: Int32, anchor: Int32, initial: Bool) -> Void {}
  // MUST execute the preserved original author discovery/wake when UNKNOWN.
  public func OnGCETStaticUnavailable(owner: Int32) -> Void {}
}

public class GCETStaticLocationOwner extends IScriptable {
  public let owner: Int32;
  public let listener: ref<GCETStaticLocationListener>;
}

public class GCETStaticLocationTick extends ScheduledJob {
  private let m_hub: wref<GCETStaticLocationHub>;

  public func SetHub(hub: ref<GCETStaticLocationHub>) -> Void {
    this.m_hub = hub;
  }

  public func Execute(runtime: ref<Runtime>) -> Void {
    if IsDefined(this.m_hub) {
      this.m_hub.Tick();
    }
  }
}

// One native journal consumer for all REDscript owners. Source-generated mod
// adapters RegisterOwner at their author-proven lifecycle, not on every frame.
public class GCETStaticLocationHub extends ScriptableSystem {
  private let m_owners: array<ref<GCETStaticLocationOwner>>;
  private let m_jobId: Int32;
  private let m_subscribed: Bool;
  private let m_started: Bool;

  public static func Get(game: GameInstance) -> ref<GCETStaticLocationHub> {
    return GameInstance.GetScriptableSystemsContainer(game)
      .Get(n"GRedRuntime.GCETStaticLocationHub") as GCETStaticLocationHub;
  }

  public func RegisterOwner(owner: Int32, listener: ref<GCETStaticLocationListener>) -> Bool {
    if owner <= 0 || !IsDefined(listener) {
      return false;
    }
    let i: Int32 = 0;
    while i < ArraySize(this.m_owners) {
      if this.m_owners[i].owner == owner {
        return false;
      }
      i += 1;
    }
    let subscription = new GCETStaticLocationOwner();
    subscription.owner = owner;
    subscription.listener = listener;
    ArrayPush(this.m_owners, subscription);

    // Membership changes require a fresh snapshot, never a silent stale FAR.
    this.ReleaseNative();
    this.Start();
    if !this.m_started {
      // Do not leave a stale owner after G-RED is not yet ready: generated
      // source can retry registration when the runtime becomes available.
      ArrayErase(this.m_owners, ArraySize(this.m_owners) - 1);
      return false;
    }
    return true;
  }

  public func UnregisterOwner(owner: Int32) -> Bool {
    let i: Int32 = 0;
    while i < ArraySize(this.m_owners) {
      if this.m_owners[i].owner == owner {
        ArrayErase(this.m_owners, i);
        this.ReleaseNative();
        if ArraySize(this.m_owners) == 0 {
          this.Shutdown();
        }
        return true;
      }
      i += 1;
    }
    return false;
  }

  private func OnDetach() -> Void {
    this.Shutdown();
  }

  private func Start() -> Void {
    if this.m_started || ArraySize(this.m_owners) == 0 {
      return;
    }
    let runtime = Runtime.Get(this.GetGameInstance());
    if !IsDefined(runtime) || !runtime.IsReady() {
      return;
    }
    let scheduler = runtime.GetScheduler();
    if !IsDefined(scheduler) {
      return;
    }
    let job = new GCETStaticLocationTick();
    job.SetHub(this);
    job.Configure(n"GCET_STATIC_NATIVE_BRIDGE", 0.05, true);
    this.m_jobId = scheduler.Register(job);
    this.m_started = this.m_jobId > 0;
  }

  public func Shutdown() -> Void {
    this.ReleaseNative();
    if this.m_started {
      let runtime = Runtime.Get(this.GetGameInstance());
      if IsDefined(runtime) && runtime.IsReady() {
        let scheduler = runtime.GetScheduler();
        if IsDefined(scheduler) {
          scheduler.Unregister(this.m_jobId);
        }
      }
    }
    this.m_jobId = 0;
    this.m_started = false;
    ArrayClear(this.m_owners);
  }

  private func ReleaseNative() -> Void {
    if this.m_subscribed {
      GCETStaticUnsubscribe(1702002);
      this.m_subscribed = false;
    }
  }

  private func FailOpen() -> Void {
    let i: Int32 = 0;
    while i < ArraySize(this.m_owners) {
      let entry = this.m_owners[i];
      if IsDefined(entry.listener) {
        entry.listener.OnGCETStaticUnavailable(entry.owner);
      }
      i += 1;
    }
  }

  private func Dispatch(owner: Int32, anchor: Int32, state: Int32, initial: Bool) -> Void {
    let i: Int32 = 0;
    while i < ArraySize(this.m_owners) {
      let entry = this.m_owners[i];
      if entry.owner == owner && IsDefined(entry.listener) {
        if state == 1 {
          entry.listener.OnGCETStaticNear(owner, anchor, initial);
        } else {
          if state == 0 {
            entry.listener.OnGCETStaticFar(owner, anchor, initial);
          } else {
            entry.listener.OnGCETStaticUnavailable(owner);
          }
        }
        return;
      }
      i += 1;
    }
  }

  private func ReadSnapshot() -> Bool {
    let i: Int32 = 0;
    while i < ArraySize(this.m_owners) {
      let owner = this.m_owners[i].owner;
      let count = GCETStaticOwnerCount(owner);
      let j: Int32 = 0;
      while j < count {
        let id = GCETStaticOwnerAnchor(owner, j);
        if id > 0 {
          this.Dispatch(owner, id, GCETStaticState(id), true);
        }
        j += 1;
      }
      i += 1;
    }
    // Only the native game thread may advance position while this REDscript
    // callback is active. Ack the snapshot before polling event history.
    return GCETStaticAckSnapshot(1702002);
  }

  public func Tick() -> Void {
    if ArraySize(this.m_owners) == 0 {
      return;
    }
    if !GCETStaticReady() || GCETStaticCoordinatorState() != 2 {
      this.FailOpen();
      return;
    }
    if !this.m_subscribed {
      if !GCETStaticSubscribe(1702002, 0) {
        this.FailOpen();
        return;
      }
      this.m_subscribed = true;
    }
    if GCETStaticNeedsSnapshot(1702002) && !this.ReadSnapshot() {
      this.ReleaseNative();
      this.FailOpen();
      return;
    }
    let i: Int32 = 0;
    while i < 512 {
      let id = GCETStaticPoll(1702002);
      if id < 0 {
        // Overflow/session reset: never misinterpret missing events as FAR.
        this.ReleaseNative();
        this.FailOpen();
        return;
      }
      if id == 0 {
        return;
      }
      this.Dispatch(GCETStaticPolledOwner(1702002), id,
                    GCETStaticPolledState(1702002), false);
      i += 1;
    }
  }
}
