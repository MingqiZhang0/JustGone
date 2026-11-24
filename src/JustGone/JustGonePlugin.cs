using System;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using JustGone.Internal;
using UnityEngine;

namespace JustGone
{
    /// <summary>
    /// BepInEx 5 entry point for Just Gone — The Just Go Modding Extension.
    /// Applies Harmony patches with strict transactional rollback (UnpatchSelf only, never UnpatchAll)
    /// learned from WeiXiangQi v0.10.10.0 (WXQ-P2-002).
    /// </summary>
    [BepInPlugin(JustGoneApi.FrameworkGuid, JustGoneApi.FrameworkName, JustGoneApi.FrameworkVersion)]
    public sealed class JustGonePlugin : BaseUnityPlugin
    {
        private ConfigEntry<bool> _verbose;
        private Harmony _harmony;

        private void Awake()
        {
            _verbose = Config.Bind("Diagnostics", "Verbose", false, "Enable verbose Just Gone framework diagnostics.");
            JgLog.Attach(Logger, _verbose.Value);

            JgLog.Info("Initializing Just Gone v" + JustGoneApi.FrameworkVersion
                       + " | unity=" + Application.unityVersion
                       + " | game=" + Application.version);

            try
            {
                _harmony = new Harmony(JustGoneApi.FrameworkGuid);
                _harmony.PatchAll(typeof(JustGonePlugin).Assembly);
                JgLog.Info("Harmony patches applied (owner=" + JustGoneApi.FrameworkGuid + ").");

                int incomplete = JustGoneApi.SavesInternal.ScanIncompleteTransactions();
                if (incomplete > 0)
                    JgLog.Warn("Reconciled " + incomplete + " incomplete ES3 SaveGuard transaction(s).");

                JgBootstrap.EnsureCreated();
                JustGoneApi.IsReady = true;
                JgLog.Info("Just Gone ready.");
            }
            catch (Exception ex)
            {
                JustGoneApi.IsReady = false;
                JgLog.Error("Just Gone initialization failed; rolling back framework-owned patches and bootstrap.", ex);
                try { JgBootstrap.DestroyIfCreated(); }
                catch (Exception bEx) { JgLog.Error("Bootstrap rollback failed", bEx); }
                try
                {
                    if (_harmony != null)
                    {
                        _harmony.UnpatchSelf();
                        JgLog.Info("Rollback removed all patches owned by " + JustGoneApi.FrameworkGuid + ".");
                    }
                }
                catch (Exception hEx) { JgLog.Error("Harmony UnpatchSelf rollback failed", hEx); }
            }
        }

        private void OnDestroy()
        {
            JustGoneApi.IsReady = false;
            try { JgBootstrap.DestroyIfCreated(); } catch { }
            try { _harmony?.UnpatchSelf(); } catch { }
        }
    }
}
