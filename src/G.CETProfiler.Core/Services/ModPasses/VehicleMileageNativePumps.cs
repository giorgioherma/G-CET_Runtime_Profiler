namespace GCETRuntimeProfiler.Core.Services;

// Compiled native FAR only parks pump discovery. Vehicle fuel/mileage,
// independent player-state work, station economy and marker lifecycle remain.
internal static partial class SemanticInjectors
{
    private static SemanticInjectionResult ApplyVehicleMileageNativePumps(SemanticPatchContext context)
    {
        var init = context.FindFile("init.lua",
            "GAS.setup({", "Mod.Subscribe(\"VehicleMount\"", "Mod.Subscribe(\"VehicleUnmount\"",
            "local function maybe_run_gas(dt, id, label, vehicle, spec)");
        var gas = context.FindFile("vm_gas_station.lua",
            "function M.update(dt, helpers)", "local pointIdx, gasPoint = findGasPoint(px, py, pz)",
            "function M.setup(opts)");
        const string marker = "G-CET native location:vehicle-mileage-pumps-v1";
        var hasInit = init.Text.Contains(marker, StringComparison.Ordinal);
        var hasGas = gas.Text.Contains(marker, StringComparison.Ordinal);
        if(hasInit && hasGas)
            return SemanticInjectionResult.Skip("VehicleMileage native pump stage already installed.");
        if(hasInit != hasGas)
            throw new InvalidOperationException("VehicleMileage partial native pump stage.");

        var crlfInit=init.Text.Contains("\r\n",StringComparison.Ordinal);
        var crlfGas=gas.Text.Contains("\r\n",StringComparison.Ordinal);
        var i=crlfInit ? init.Text.Replace("\r\n","\n",StringComparison.Ordinal) : init.Text;
        var g=crlfGas ? gas.Text.Replace("\r\n","\n",StringComparison.Ordinal) : gas.Text;
        const string gasHead="function M.update(dt, helpers)\n  M._currentStationEmpty = false\n";
        if(g.Split(gasHead,StringSplitOptions.None).Length!=2)
            throw new InvalidOperationException("VehicleMileage pump state boundary changed.");
        const string gatedGas = """
-- G-CET native location:vehicle-mileage-pumps-v1
-- UNKNOWN and missing native subscription always run original Lua proximity.
function M.__gcetSetNativeFar(far)
  M.__gcetNativeFar = far == true
end

function M.update(dt, helpers)
  M._currentStationEmpty = false
  if M.__gcetNativeFar then
    if M._playing then
      stopAudioEvent(M.opts.sound_event, M.opts.sound_stop_event)
      M._playing = false
    end
    M._wasInRange = false
    M._lastPointIdx = nil
    return false
  end
""";
        g=g.Replace(gasHead,gatedGas+"\n",StringComparison.Ordinal);
        const string initAttach="  if Mod then\n    Mod.Subscribe(\"VehicleMount\", on_vehicle_mount)";
        if(i.Split(initAttach,StringSplitOptions.None).Length!=2)
            throw new InvalidOperationException("VehicleMileage 0-Engine mount lane changed.");
        const string nativeAttach = """
  -- G-CET native location:vehicle-mileage-pumps-v1
  -- The installed JSON is compiled into GCAS by Resolver at generation time.
  -- Failed/unknown native ownership leaves the original pump scan running.
  if Mod and type(Engine) == "table" then
    local host = Engine.GCET and Engine.GCET.NativeLocationHost
    if host and type(host.attachCompiled) == "function"
        and GAS.points and #GAS.points > 0
        and type(GAS.__gcetSetNativeFar) == "function" then
      local ok, attached = pcall(function()
        return host:attachCompiled(10040, #GAS.points,
          function() GAS.__gcetSetNativeFar(false) end,
          function() GAS.__gcetSetNativeFar(true) end,
          function() GAS.__gcetSetNativeFar(false); return true end)
      end)
      if not ok or not attached then GAS.__gcetSetNativeFar(false) end
    end
  end
""";
        i=i.Replace(initAttach,nativeAttach+"\n"+initAttach,StringComparison.Ordinal);
        context.Write(init,crlfInit ? i.Replace("\n","\r\n",StringComparison.Ordinal) : i);
        context.Write(gas,crlfGas ? g.Replace("\n","\r\n",StringComparison.Ordinal) : g);
        return SemanticInjectionResult.Success(
            "Native compiled pump FAR parks only station discovery; NEAR/UNKNOWN preserves original exact pump lookup and refuel lifecycle. Mount, fuel, mileage, economy and player signals unchanged.");
    }
}
