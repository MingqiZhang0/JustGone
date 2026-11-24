using System;
using BepInEx.Logging;

namespace JustGone.Internal
{
    /// <summary>
    /// Exception-safe logging facade extracted from WeiXiangQi v0.10.10.0 (WxqLog.cs).
    /// Logging failures never disrupt game or MOD execution.
    /// </summary>
    internal static class JgLog
    {
        private const string FrameworkPrefix = "[JustGone] ";
        private static ManualLogSource _source;
        internal static bool VerboseEnabled;
        internal static string LastError = string.Empty;

        internal static void Attach(ManualLogSource source, bool verbose = false)
        {
            _source = source;
            VerboseEnabled = verbose;
        }

        internal static void Info(string message) => WriteInfo(FrameworkPrefix + message);
        internal static void Warn(string message) => WriteWarn(FrameworkPrefix + message);

        internal static void Error(string message)
        {
            LastError = message ?? string.Empty;
            WriteError(FrameworkPrefix + LastError);
        }

        internal static void Error(string message, Exception ex)
        {
            LastError = (message ?? string.Empty) + " :: " + (ex == null ? "(null)" : ex.ToString());
            WriteError(FrameworkPrefix + LastError);
        }

        internal static void Debug(string message)
        {
            if (VerboseEnabled) WriteInfo(FrameworkPrefix + "[dbg] " + message);
        }

        internal static void WriteInfo(string message)
        {
            try { _source?.LogInfo(message); } catch { }
        }

        internal static void WriteWarn(string message)
        {
            try { _source?.LogWarning(message); } catch { }
        }

        internal static void WriteError(string message)
        {
            try { _source?.LogError(message); } catch { }
        }

        internal sealed class ModLogger : IModLogger
        {
            private readonly string _prefix;

            public ModLogger(string modId)
            {
                _prefix = "[JG:" + modId + "] ";
            }

            public void Info(string message) => WriteInfo(_prefix + message);
            public void Warn(string message) => WriteWarn(_prefix + message);
            public void Error(string message) => WriteError(_prefix + message);
            public void Error(string message, Exception ex) =>
                WriteError(_prefix + message + " :: " + (ex == null ? "(null)" : ex.ToString()));
            public void Debug(string message)
            {
                if (VerboseEnabled) WriteInfo(_prefix + "[dbg] " + message);
            }
        }
    }
}
