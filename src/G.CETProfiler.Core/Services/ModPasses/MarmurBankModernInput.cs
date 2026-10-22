using System.Text.RegularExpressions;
namespace GCETRuntimeProfiler.Core.Services;

// Marmur 22.5: no finance, timer, popup API or legacy source rollback.
// The worker owns finite routing and source-proven Vendor/Trade exit wake.
internal static partial class SemanticInjectors
{
    private static SemanticInjectionResult ApplyMarmurBankModernInput(SemanticPatchContext context)
    {
        var ui=context.FindFile("external/interactionUI.lua",
            "WORLD_INTERACTION_ACTIONS","shouldBlockWorldAction",
            "worldActionOverrideOk","if ui.input or not ui.hubShown then");
        var init=context.FindFile("init.lua","local packageEvent = registerForEvent",
            "BANK:initialize(SPAWN)","marmurRuntimeTick = function(delta)");
        var bank=context.FindFile("module/Bank.lua","function BANK:openAtmKeypadFromKeybind()",
            "function BANK:getNearbyAtmIndex(","self._atmPosCache");
        const string marker="-- G-CET semantic:marmurbank-modern-input-v2";
        var s=ui.Text.Replace("\r\n","\n",StringComparison.Ordinal);
        var main=init.Text.Replace("\r\n","\n",StringComparison.Ordinal);
        if(s.Contains(marker,StringComparison.Ordinal))
        {
            if(s.Contains("GCET_Semantic/marmurbank_ui_worker",StringComparison.Ordinal) &&
                main.Contains("__gcetDropoffTick",StringComparison.Ordinal))
                return SemanticInjectionResult.Skip("Marmur coordinator already installed.");
            throw new InvalidOperationException("Marmur coordinator partially installed.");
        }
        if(s.Contains("G-CET semantic:marmurbank-modern-input-v1",StringComparison.Ordinal))
            throw new InvalidOperationException("Marmur prior input stage needs independent compatible upgrade.");
        var overrideMatch=Regex.Match(s,
            @"Override\s*\(\s*[""']PlayerPuppet[""']\s*,\s*[""']OnAction[""']\s*,\s*function");
        var listener=Regex.Match(s,
            @"(?s)Observe\s*\(\s*[""']PlayerPuppet[""']\s*,\s*[""']OnAction[""']\s*,\s*function\s*\(\s*_\s*,\s*action\s*\)(?<body>.*?)(?<close>\n[ \t]*end\))(?=\s*Override\s*\(\s*[""']InteractionUIBase[""']\s*,\s*[""']OnDialogsSelectIndex[""'])");
        if(!overrideMatch.Success || !listener.Success ||
           overrideMatch.Index>=listener.Index ||
           Regex.Matches(s,@"Override\s*\(\s*[""']PlayerPuppet[""']\s*,\s*[""']OnAction[""']").Count!=1 ||
           Regex.Matches(s,@"Observe\s*\(\s*[""']PlayerPuppet[""']\s*,\s*[""']OnAction[""']").Count!=1 ||
           !s.Contains("function ui.init()",StringComparison.Ordinal) ||
           !s.Contains("function ui.suppressVanilla(",StringComparison.Ordinal) ||
           !s.Contains("function ui.maintainVanillaSuppression()",StringComparison.Ordinal) ||
           !s.Contains("function ui.isVanillaDialogActive(",StringComparison.Ordinal))
            throw new InvalidOperationException("Marmur 22.5 UI source ownership changed.");
        var body=listener.Groups["body"].Value;
        foreach(var p in new[] { "shouldBlockWorldAction(getActionDetails(action))",
            "if ui.input or not ui.hubShown then","ChoiceScrollUp","ChoiceScrollDown",
            "ChoiceApply","ui.callbacks[ui.selectedIndex + 1]" })
            if(!body.Contains(p,StringComparison.Ordinal))
                throw new InvalidOperationException("Marmur UI callback changed: "+p);
        var overrideSource=s[overrideMatch.Index..listener.Index];
        foreach(var p in new[] { "getActionDetails(action)",
            "consumeAction(action, wrappedConsumer)",
            "return wrapped(action, wrappedConsumer)","return wrapped(action)" })
            if(!overrideSource.Contains(p,StringComparison.Ordinal))
                throw new InvalidOperationException("Marmur suppression override changed: "+p);
        var gate=Regex.Match(overrideSource,@"\bif\s+action\s+then\b");
        if(!gate.Success || Regex.Matches(overrideSource,@"\bif\s+action\s+then\b").Count!=1)
            throw new InvalidOperationException("Marmur override gate changed.");
        if(!main.Contains("local packageRequire = require",StringComparison.Ordinal) ||
           !main.Contains("registerForEvent(\"onShutdown\"",StringComparison.Ordinal) ||
           !bank.Text.Contains("entry.isDropPoint",StringComparison.Ordinal))
            throw new InvalidOperationException("Marmur ATM popup, package or dialog-close proof changed.");
        // The author's existing GameUI.Listen owns MenuClose; reuse it.
        var menuListener=main.IndexOf("GameUI.Listen(function(state)",StringComparison.Ordinal);
        var eventRow=main.IndexOf("local event = state.event",menuListener,StringComparison.Ordinal);
        if(menuListener<0 || eventRow<menuListener ||
           main.Split("local event = state.event",StringSplitOptions.None).Length!=2)
            throw new InvalidOperationException("Marmur Vendor/Trade lifecycle event changed.");
        const string initAnchor="BANK:initialize(SPAWN)";
        const string tickAnchor="if delta > MARMUR_UPDATE_DELTA_MAX then delta = MARMUR_UPDATE_DELTA_MAX end";
        const string importAnchor="local function nowSeconds()";
        if(main.Split(initAnchor,StringSplitOptions.None).Length!=2 ||
           main.Split(tickAnchor,StringSplitOptions.None).Length!=2 ||
           s.Split(importAnchor,StringSplitOptions.None).Length!=2)
            throw new InvalidOperationException("Marmur source bridge ownership ambiguous.");

        // Preserve every line of the original gameplay callback as the local
        // source-owned function, changing ONLY its registration.
        var replacement="local function __gcetMarmurBankOnAction(_, action)"+
            body+"\n\tend\n\t"+marker+
            "\n\t__gcetWorker.subscribe(WORLD_INTERACTION_ACTIONS, __gcetMarmurBankOnAction, Observe)";
        s=s[..listener.Index]+replacement+s[(listener.Index+listener.Length)..];
        var gateIndex=overrideMatch.Index+gate.Index;
        s=s[..gateIndex]+
            "if action and __gcetWorker.overrideActive(ui, shouldSuppressVanillaNow) then"+
            s[(gateIndex+gate.Length)..];
        s=s.Replace(importAnchor,
            "local __gcetWorker = require(\"GCET_Semantic/marmurbank_ui_worker\")\n"+
            "ui.__gcetDropoffVendorClose = function(state)\n"+
            "    return __gcetWorker.vendorClose(ui, state)\nend\n"+
            "ui.__gcetDropoffTick = function(gameplay, inMenu, delta)\n"+
            "    return __gcetWorker.tick(ui, gameplay, inMenu, delta)\nend\n\n"+importAnchor,
            StringComparison.Ordinal);
        // Existing GameUI observer receives Vendor exit without a new listener.
        const string eventAnchor="local event = state.event";
        main=main.Replace(eventAnchor,eventAnchor+
            "\n\t\tif event == \"MenuClose\" and BANK and BANK.interactionUI and "+
            "BANK.interactionUI.__gcetDropoffVendorClose then"+
            "\n\t\t\tBANK.interactionUI.__gcetDropoffVendorClose(state)\n\t\tend",
            StringComparison.Ordinal);
        main=main.Replace(initAnchor,initAnchor+
            "\n\t\tif BANK.interactionUI then BANK.interactionUI.__gcetBank = BANK end",
            StringComparison.Ordinal);
        main=main.Replace(tickAnchor,tickAnchor+
            "\n\tif BANK and BANK.interactionUI and BANK.interactionUI.__gcetDropoffTick then"+
            "\n\t\tBANK.interactionUI.__gcetDropoffTick(isGameplayActive, isInMenu, delta)\n\tend",
            StringComparison.Ordinal);
        if(!s.Contains(marker,StringComparison.Ordinal) ||
           !s.Contains("__gcetWorker.subscribe",StringComparison.Ordinal) ||
           !main.Contains("__gcetDropoffTick",StringComparison.Ordinal) ||
           !main.Contains("__gcetDropoffVendorClose(state)",StringComparison.Ordinal))
            throw new InvalidOperationException("Marmur worker source postcondition failed.");
        context.Write(ui,ui.Text.Contains("\r\n",StringComparison.Ordinal)
            ?s.Replace("\n","\r\n",StringComparison.Ordinal):s);
        context.Write(init,init.Text.Contains("\r\n",StringComparison.Ordinal)
            ?main.Replace("\n","\r\n",StringComparison.Ordinal):main);
        context.WriteCoordinator("marmurbank_ui_worker.lua", """
-- G-CET semantic coordinator:marmurbank-modern-input-v2
-- No ATM class overrides; reuses author's vanilla dialogue close and bank tick.
local M = {}
local wake = { pending = nil, index = 0 }
local function nearby(ui)
  local bank = ui.__gcetBank
  if not bank or not bank.isInitialized or ui.hubShown or
     ui.suppressVanillaDialogs or ui.clearingVanillaDialogs or
     type(bank.getNearbyAtmIndex) ~= "function" or
     type(bank.openAtmKeypadFromKeybind) ~= "function" then return 0 end
  local ok, opened = pcall(bank.isAccountOpen, bank)
  if not ok or not opened then return 0 end
  local keypad = bank.atmKeypad
  if keypad and type(keypad.isActive) == "function" then
    local activeOk, active = pcall(keypad.isActive, keypad)
    if activeOk and active then return 0 end
  end
  local found, index = pcall(bank.getNearbyAtmIndex, bank, true)
  if not found or type(index) ~= "number" or index < 1 then return 0 end
  for _, entry in ipairs(bank._atmPosCache or {}) do
    if entry.index == index then
      return entry.isDropPoint == true and index or 0
    end
  end
  return 0
end
function M.overrideActive(ui, predicate)
  -- Preserve world/door interaction suppression; idle calls do no name/type decode.
  if ui.suppressVanillaDialogs == true then return true end
  if type(ui.vanillaSuppressPredicate) ~= "function" then return false end
  return predicate()
end
function M.subscribe(actionsMap, callback, observe)
  local actions = {"ChoiceScrollUp", "ChoiceScrollDown", "ChoiceApply"}
  for name in pairs(actionsMap) do
    if name ~= "ChoiceScrollUp" and name ~= "ChoiceScrollDown" and
       name ~= "ChoiceApply" then actions[#actions + 1] = name end
  end
  local ok, engine = pcall(GetMod, "0-Engine")
  if ok and type(engine) == "table" then
    local api = type(engine.GCET) == "table" and
      type(engine.GCET.SubscribeAction) == "function" and engine.GCET or engine
    if type(api.SubscribeAction) == "function" then
      local attached, handle = pcall(api.SubscribeAction,
        { id = "MarmurBank.InteractionUI.OnAction", actions = actions }, callback)
      if attached and handle ~= nil and handle ~= false then return true end
    end
  end
  observe("PlayerPuppet", "OnAction", callback)
  return false
end
function M.vendorClose(ui, state)
  -- Only the vanilla Vendor/Trade menu exit can arm an ATM wake.
  if not state or state.lastMenu ~= "Vendor" or
     (state.lastSubmenu ~= nil and state.lastSubmenu ~= false and
      state.lastSubmenu ~= "Trade") then return end
  local index = nearby(ui)
  wake.index = index
  wake.pending = index > 0 and 0 or nil
end
function M.tick(ui, gameplay, inMenu, delta)
  if wake.pending == nil then return end
  local dt = tonumber(delta) or 0
  wake.pending = wake.pending + math.min(math.max(dt, 0), 1)
  if wake.pending > 1.5 then wake.pending = nil; return end
  if not gameplay or inMenu or ui.hubShown or
     ui.suppressVanillaDialogs or ui.clearingVanillaDialogs or
     wake.pending < 0.35 then return end
  -- The existing source GameUI listener already proved Vendor menu close.
  local index = nearby(ui)
  if index == 0 or index ~= wake.index then wake.pending = nil; return end
  wake.pending = nil
  -- Original Bank checks account, player, vehicle, combat and popup lifecycle.
  pcall(ui.__gcetBank.openAtmKeypadFromKeybind, ui.__gcetBank)
end
return M
""");
        return SemanticInjectionResult.Success(
            "22.5 Bank action registration moved to separate 0-Engine coordinator; "+
            "idle override decode eliminated; nearby Vendor/Trade menu close opens "+
            "the original popup through existing 20Hz timer. Author finance, ATM "+
            "keybind, package handshake and suppression fallback preserved.");
    }
}
