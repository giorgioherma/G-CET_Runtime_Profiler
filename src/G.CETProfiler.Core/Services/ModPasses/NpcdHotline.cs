using System.Text;
using System.Text.RegularExpressions;

namespace GCETRuntimeProfiler.Core.Services;

// Mod-owned source transformations. Shared proof/replace helpers remain in SemanticInjectors.cs.
internal static partial class SemanticInjectors
{
    private static SemanticInjectionResult ApplyNpcdHotline(
        SemanticPatchContext context)
    {
        var init = context.FindFile(
            "init.lua",
            "processTaskQueue()",
            "sms.processSmsQueue(delta)");

        var initText = ReplaceOnce(
            init.Text,
            "\tprocessTaskQueue()\n\tsms.processSmsQueue(delta)\n",
            "\tif queuedTasks.isTaskQueued then processTaskQueue() end\n" +
            "\tif sms.hasPending and sms.hasPending() then sms.processSmsQueue(delta) end\n",
            "NPCD exact pending-work gates");
        context.Write(init, initText);

        var sms = context.FindFile(
            "sms.lua",
            "local smsQueue = {}",
            "function sms.processSmsQueue(delta)");

        var smsText = ReplaceOnce(
            sms.Text,
            "local currTime, nextTime = 0, 0\nfunction sms.processSmsQueue(delta)\n",
            "local currTime, nextTime = 0, 0\n" +
            "function sms.hasPending()\n" +
            "\treturn smsQueue ~= nil and #smsQueue > 0\n" +
            "end\n\n" +
            "function sms.processSmsQueue(delta)\n",
            "NPCD SMS pending query");
        context.Write(sms, smsText);

        return SemanticInjectionResult.Success(
            "Skipped only the task/SMS workers when their queues are empty; all interaction/UI timing and the author's existing 0.5 s police/subscription core remain untouched.");
    }

}
