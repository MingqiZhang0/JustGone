using System;

namespace JustGone.Internal
{
    /// <summary>
    /// Unified failsafe recovery when a Just Gone custom mode fails to initialize or loses GameManager unexpectedly.
    /// Extracted and generalized from WeiXiangQi v0.10.10.0 (WxqFailsafe.cs).
    /// </summary>
    internal static class JgFailsafe
    {
        internal static bool InProgress { get; private set; }
        internal static string LastError { get; private set; } = string.Empty;

        internal static void Abort(string stage, string reason)
        {
            if (InProgress)
            {
                JgLog.Warn("FAILSAFE already in progress; ignoring duplicate abort (" + stage + ": " + reason + ")");
                return;
            }
            InProgress = true;
            LastError = stage + ": " + reason;
            JgLog.Error("FAILSAFE [" + stage + "] " + reason);

            // 1) Lock vanilla input immediately so player cannot accidentally place stones in a broken scene
            JustGoneApi.InputInternal.FailsafeInputLocked = true;

            // 2) Tear down active custom mode and restore ES3 CAS snapshot
            try { JustGoneApi.ModesInternal.DisarmPending("failsafe:" + stage); } catch { }
            try { JustGoneApi.ModesInternal.EndActiveSession("failsafe:" + stage); } catch { }

            // 3) Notify and schedule safe return to MainScene
            try { JustGoneApi.UiInternal.ShowStickyError("Just Gone 模式已安全中止：" + reason); } catch { }
            JgBootstrap.ScheduleReturnToMenu(stage, reason);
        }

        internal static void Clear()
        {
            InProgress = false;
            JustGoneApi.InputInternal.FailsafeInputLocked = false;
        }
    }
}
