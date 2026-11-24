using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using BepInEx;
using JustGone.Core;
using JustGone.Internal;
using UnityEngine;

namespace JustGone.Services
{
    /// <summary>
    /// Transactional Compare-And-Set guard protecting vanilla SaveFileOfficial_beta.es3 when launching custom modes.
    /// Extracted and generalized from WeiXiangQi v0.10.10.0 (WxqEs3Guard.cs + WxqTxn.cs + WxqAtomicConfigFile.cs).
    /// Backups are isolated under BepInEx/config/dev.justgone.framework/es3-guard/.
    /// </summary>
    internal sealed class SaveGuardService : ISaveGuardService
    {
        private const string KeyGameMode = "GameMode";
        private const string KeyEnvironment = "GameEnvironmentIndex";
        private const string KeyBoardSize = "BoardSizeIndex";

        private sealed class TxnRecord
        {
            public string Id;
            public SaveTxnState State;
            public int BeforeGameMode, BeforeEnvironment, BeforeBoardSize;
            public bool BeforeSavedLoaded;
            public int AfterGameMode, AfterEnvironment, AfterBoardSize;
            public bool AfterSavedLoaded;
            public string SavePath, SaveCopy, SaveSha, JsonPath;
            public string CreatedUtc, UpdatedUtc;
        }

        private TxnRecord _last;

        public string BackupDirectory => Path.Combine(Paths.ConfigPath, JustGoneApi.FrameworkGuid, "es3-guard");

        public bool BeginLaunchOverride(SaveLaunchOverrideSpec spec, out string error)
        {
            error = null;
            if (spec == null) spec = new SaveLaunchOverrideSpec();

            var t = new TxnRecord
            {
                Id = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture)
                     + "_" + Guid.NewGuid().ToString("N").Substring(0, 8),
                CreatedUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                AfterGameMode = (int)spec.TargetVanillaMode,
                AfterEnvironment = (int)spec.TargetEnvironment,
                AfterBoardSize = JustGoSceneNames.ToEs3BoardSizeIndex(spec.TargetBoardSize),
                AfterSavedLoaded = spec.IsSavedGameLoaded
            };

            // 1) Snapshot current ES3 keys
            try
            {
                t.BeforeGameMode = Utils.LoadGameData(KeyGameMode, 0);
                t.BeforeEnvironment = Utils.LoadGameData(KeyEnvironment, 0);
                t.BeforeBoardSize = Utils.LoadGameData(KeyBoardSize, 0);
                t.BeforeSavedLoaded = Utils.LoadGameData(Constants.IS_SAVED_GAME_LOADED, false);
            }
            catch (Exception ex)
            {
                error = "Failed reading vanilla ES3 keys (no modifications made): " + ex.Message;
                return false;
            }

            // 2) Copy save file + verify length & SHA-256 + write prepared txn.json
            try
            {
                Directory.CreateDirectory(BackupDirectory);
                t.SavePath = Path.Combine(Application.persistentDataPath, Constants.SAVE_FILE_NAME);
                if (File.Exists(t.SavePath))
                {
                    t.SaveSha = ComputeSha256(t.SavePath);
                    t.SaveCopy = Path.Combine(BackupDirectory, t.Id + ".es3.bak");
                    File.Copy(t.SavePath, t.SaveCopy, true);
                    if (!File.Exists(t.SaveCopy)) throw new IOException("ES3 backup file was not created.");
                    if (new FileInfo(t.SaveCopy).Length != new FileInfo(t.SavePath).Length)
                        throw new IOException("ES3 backup size mismatch.");
                    if (!string.Equals(ComputeSha256(t.SaveCopy), t.SaveSha, StringComparison.OrdinalIgnoreCase))
                        throw new IOException("ES3 backup SHA-256 mismatch.");
                }

                t.JsonPath = Path.Combine(BackupDirectory, t.Id + ".txn.json");
                t.State = SaveTxnState.Prepared;
                WriteTxn(t);

                TxnRecord verified = ReadTxn(t.JsonPath);
                if (verified == null || verified.State != SaveTxnState.Prepared)
                    throw new IOException("Transaction file readback verification failed.");
            }
            catch (Exception ex)
            {
                error = "ES3 backup/prepare failed (no modifications made): " + ex.Message;
                return false;
            }

            // 3) Apply keys transactionally with CAS rollback on any failure
            t.State = SaveTxnState.Applying;
            WriteTxn(t);
            try
            {
                Utils.SaveGameData(KeyGameMode, t.AfterGameMode);
                Utils.SaveGameData(KeyEnvironment, t.AfterEnvironment);
                Utils.SaveGameData(KeyBoardSize, t.AfterBoardSize);
                Utils.SaveGameData(Constants.IS_SAVED_GAME_LOADED, t.AfterSavedLoaded);
            }
            catch (Exception ex)
            {
                bool restored = RestoreCas(t, SaveTxnState.RolledBack, out string rollbackErr);
                error = "ES3 write failed: " + ex.Message + " | rollback=" + (restored ? "ok" : rollbackErr);
                JgLog.Error("SaveGuard transaction failed: " + error);
                return false;
            }

            t.State = SaveTxnState.Committed;
            WriteTxn(t);
            _last = t;
            JgLog.Info("SaveGuard txn " + t.Id + " committed (mode " + t.BeforeGameMode + "->" + t.AfterGameMode
                       + ", env " + t.BeforeEnvironment + "->" + t.AfterEnvironment
                       + ", sizeIdx " + t.BeforeBoardSize + "->" + t.AfterBoardSize + ")");
            return true;
        }

        public bool RestoreLastSnapshot(out string error)
        {
            if (_last == null)
            {
                error = "No active SaveGuard transaction to restore.";
                return false;
            }
            return RestoreCas(_last, SaveTxnState.RolledBack, out error);
        }

        public void RestoreLastSnapshotSafe()
        {
            try
            {
                if (_last == null) return;
                if (!RestoreLastSnapshot(out string err))
                    JgLog.Warn("SaveGuard restore skipped: " + err);
            }
            catch (Exception ex)
            {
                JgLog.Warn("SaveGuard restore exception: " + ex.Message);
            }
        }

        public int ScanIncompleteTransactions()
        {
            int incomplete = 0;
            try
            {
                if (!Directory.Exists(BackupDirectory)) return 0;
                string[] files = Directory.GetFiles(BackupDirectory, "*.txn.json");
                for (int i = 0; i < files.Length; i++)
                {
                    TxnRecord t = ReadTxn(files[i]);
                    if (t == null || SaveTransactionRules.IsTerminal(t.State)) continue;

                    incomplete++;
                    int curMode = Utils.LoadGameData(KeyGameMode, 0);
                    int curEnv = Utils.LoadGameData(KeyEnvironment, 0);
                    int curSize = Utils.LoadGameData(KeyBoardSize, 0);
                    bool curSaved = Utils.LoadGameData(Constants.IS_SAVED_GAME_LOADED, false);

                    int mw = 0, mb = 0, other = 0;
                    Tally(curMode, t.AfterGameMode, t.BeforeGameMode, ref mw, ref mb, ref other);
                    Tally(curEnv, t.AfterEnvironment, t.BeforeEnvironment, ref mw, ref mb, ref other);
                    Tally(curSize, t.AfterBoardSize, t.BeforeBoardSize, ref mw, ref mb, ref other);
                    Tally(curSaved, t.AfterSavedLoaded, t.BeforeSavedLoaded, ref mw, ref mb, ref other);

                    SaveTxnState decision = SaveTransactionRules.Reconcile(t.State, mw, mb, other, 4);
                    JgLog.Warn("SaveGuard incomplete txn " + t.Id + " -> " + SaveTransactionRules.Describe(decision));

                    if (decision == SaveTxnState.Recovered)
                    {
                        RestoreCas(t, SaveTxnState.Recovered, out string err);
                        if (!string.IsNullOrEmpty(err)) JgLog.Warn("SaveGuard recovery warning: " + err);
                    }
                    else
                    {
                        t.State = decision;
                        WriteTxn(t);
                    }
                }
            }
            catch (Exception ex)
            {
                JgLog.Warn("SaveGuard incomplete transaction scan failed: " + ex.Message);
            }
            return incomplete;
        }

        private bool RestoreCas(TxnRecord t, SaveTxnState finalState, out string error)
        {
            error = null;
            if (t == null) { error = "txn == null"; return false; }

            var written = new List<string>();
            var skipped = new List<string>();
            var failed = new List<string>();

            try
            {
                int curMode = Utils.LoadGameData(KeyGameMode, 0);
                int curEnv = Utils.LoadGameData(KeyEnvironment, 0);
                int curSize = Utils.LoadGameData(KeyBoardSize, 0);
                bool curSaved = Utils.LoadGameData(Constants.IS_SAVED_GAME_LOADED, false);

                CasInt(curMode, t.AfterGameMode, t.BeforeGameMode, KeyGameMode, written, skipped, failed);
                CasInt(curEnv, t.AfterEnvironment, t.BeforeEnvironment, KeyEnvironment, written, skipped, failed);
                CasInt(curSize, t.AfterBoardSize, t.BeforeBoardSize, KeyBoardSize, written, skipped, failed);
                CasBool(curSaved, t.AfterSavedLoaded, t.BeforeSavedLoaded, Constants.IS_SAVED_GAME_LOADED, written, skipped, failed);
            }
            catch (Exception ex)
            {
                error = "ES3 read failed during CAS rollback: " + ex.Message;
                return false;
            }

            t.State = (written.Count == 0 && skipped.Count == 4 && failed.Count == 0)
                ? SaveTxnState.SkippedNewerProgress
                : finalState;
            WriteTxn(t);

            JgLog.Info("SaveGuard CAS restore: restored=[" + string.Join(",", written.ToArray())
                       + "] skipped=[" + string.Join(",", skipped.ToArray())
                       + "] failed=[" + string.Join(",", failed.ToArray()) + "]");

            if (failed.Count > 0)
            {
                error = string.Join("; ", failed.ToArray());
                return false;
            }
            return true;
        }

        private static void CasInt(int cur, int writtenVal, int beforeVal, string key, List<string> w, List<string> s, List<string> f)
        {
            if (!SaveTransactionRules.ShouldRestore(writtenVal, cur)) { s.Add(key); return; }
            try { Utils.SaveGameData(key, beforeVal); w.Add(key); }
            catch (Exception ex) { f.Add(key + ":" + ex.Message); }
        }

        private static void CasBool(bool cur, bool writtenVal, bool beforeVal, string key, List<string> w, List<string> s, List<string> f)
        {
            if (!SaveTransactionRules.ShouldRestore(writtenVal, cur)) { s.Add(key); return; }
            try { Utils.SaveGameData(key, beforeVal); w.Add(key); }
            catch (Exception ex) { f.Add(key + ":" + ex.Message); }
        }

        private static void Tally(int cur, int after, int before, ref int mw, ref int mb, ref int other)
        {
            if (cur == after) mw++;
            else if (cur == before) mb++;
            else other++;
        }

        private static void Tally(bool cur, bool after, bool before, ref int mw, ref int mb, ref int other)
        {
            if (cur == after) mw++;
            else if (cur == before) mb++;
            else other++;
        }

        private static void WriteTxn(TxnRecord t)
        {
            t.UpdatedUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
            var dict = new Dictionary<string, object>
            {
                ["txnId"] = t.Id,
                ["state"] = SaveTransactionRules.Describe(t.State),
                ["createdUtc"] = t.CreatedUtc,
                ["updatedUtc"] = t.UpdatedUtc,
                ["beforeGameMode"] = t.BeforeGameMode,
                ["beforeEnvironment"] = t.BeforeEnvironment,
                ["beforeBoardSize"] = t.BeforeBoardSize,
                ["beforeSavedLoaded"] = t.BeforeSavedLoaded,
                ["afterGameMode"] = t.AfterGameMode,
                ["afterEnvironment"] = t.AfterEnvironment,
                ["afterBoardSize"] = t.AfterBoardSize,
                ["afterSavedLoaded"] = t.AfterSavedLoaded,
                ["saveFile"] = t.SavePath ?? string.Empty,
                ["saveCopy"] = t.SaveCopy ?? string.Empty,
                ["saveSha256"] = t.SaveSha ?? string.Empty
            };
            AtomicFileWriter.WriteAllTextAtomic(t.JsonPath, MiniJson.Serialize(dict));
        }

        private static TxnRecord ReadTxn(string path)
        {
            try
            {
                string json = File.ReadAllText(path, Encoding.UTF8);
                if (!MiniJson.TryParse(json, out object root) || root == null) return null;
                return new TxnRecord
                {
                    JsonPath = path,
                    Id = MiniJson.GetString(root, "txnId") ?? string.Empty,
                    State = SaveTransactionRules.Parse(MiniJson.GetString(root, "state")),
                    CreatedUtc = MiniJson.GetString(root, "createdUtc") ?? string.Empty,
                    UpdatedUtc = MiniJson.GetString(root, "updatedUtc") ?? string.Empty,
                    BeforeGameMode = MiniJson.GetInt(root, 0, "beforeGameMode"),
                    BeforeEnvironment = MiniJson.GetInt(root, 0, "beforeEnvironment"),
                    BeforeBoardSize = MiniJson.GetInt(root, 0, "beforeBoardSize"),
                    BeforeSavedLoaded = MiniJson.GetBool(root, false, "beforeSavedLoaded"),
                    AfterGameMode = MiniJson.GetInt(root, 7, "afterGameMode"),
                    AfterEnvironment = MiniJson.GetInt(root, 3, "afterEnvironment"),
                    AfterBoardSize = MiniJson.GetInt(root, 0, "afterBoardSize"),
                    AfterSavedLoaded = MiniJson.GetBool(root, false, "afterSavedLoaded"),
                    SavePath = MiniJson.GetString(root, "saveFile") ?? string.Empty,
                    SaveCopy = MiniJson.GetString(root, "saveCopy") ?? string.Empty,
                    SaveSha = MiniJson.GetString(root, "saveSha256") ?? string.Empty
                };
            }
            catch
            {
                return null;
            }
        }

        private static string ComputeSha256(string path)
        {
            using (FileStream fs = File.OpenRead(path))
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(fs);
                var sb = new StringBuilder(hash.Length * 2);
                for (int i = 0; i < hash.Length; i++) sb.Append(hash[i].ToString("x2"));
                return sb.ToString();
            }
        }
    }
}
