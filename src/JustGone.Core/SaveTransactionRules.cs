using System;

namespace JustGone.Core
{
    /// <summary>
    /// ES3 transaction lifecycle state persisted to txn.json for crash reconciliation.
    /// Generalized from WeiXiangQi v0.10.10.0 (shared_p4/WxqTxn.cs WxqTxnState and WxqTxnRules).
    /// </summary>
    public enum SaveTxnState
    {
        Unknown = 0,
        Prepared,
        Applying,
        Committed,
        RolledBack,
        Recovered,
        SkippedNewerProgress
    }

    /// <summary>
    /// Pure-logic Compare-And-Set rules protecting vanilla SaveFileOfficial_beta.es3.
    /// Never overwrites keys modified by the player after the transaction started.
    /// </summary>
    public static class SaveTransactionRules
    {
        public static bool ShouldRestore(int written, int current) => current == written;

        public static bool ShouldRestore(bool written, bool current) => current == written;

        public static bool ShouldRestore(string written, string current) =>
            string.Equals(written, current, StringComparison.Ordinal);

        public static bool IsTerminal(SaveTxnState state)
        {
            return state == SaveTxnState.Committed
                || state == SaveTxnState.RolledBack
                || state == SaveTxnState.Recovered
                || state == SaveTxnState.SkippedNewerProgress;
        }

        public static SaveTxnState Reconcile(
            SaveTxnState recorded,
            int matchesWritten,
            int matchesBefore,
            int other,
            int totalKeys = 4)
        {
            if (IsTerminal(recorded)) return recorded;
            if (totalKeys <= 0) return SaveTxnState.SkippedNewerProgress;

            if (other > 0) return SaveTxnState.SkippedNewerProgress;
            if (matchesWritten == totalKeys) return SaveTxnState.Recovered;
            if (matchesBefore == totalKeys) return SaveTxnState.RolledBack;
            return SaveTxnState.SkippedNewerProgress;
        }

        public static string Describe(SaveTxnState state)
        {
            switch (state)
            {
                case SaveTxnState.Prepared: return "prepared";
                case SaveTxnState.Applying: return "applying";
                case SaveTxnState.Committed: return "committed";
                case SaveTxnState.RolledBack: return "rolledback";
                case SaveTxnState.Recovered: return "recovered";
                case SaveTxnState.SkippedNewerProgress: return "skipped-newer-progress";
                default: return "unknown";
            }
        }

        public static SaveTxnState Parse(string text)
        {
            if (string.IsNullOrEmpty(text)) return SaveTxnState.Unknown;
            switch (text.Trim().ToLowerInvariant())
            {
                case "prepared": return SaveTxnState.Prepared;
                case "applying": return SaveTxnState.Applying;
                case "committed": return SaveTxnState.Committed;
                case "rolledback": return SaveTxnState.RolledBack;
                case "recovered": return SaveTxnState.Recovered;
                case "skipped-newer-progress": return SaveTxnState.SkippedNewerProgress;
                default: return SaveTxnState.Unknown;
            }
        }
    }
}
