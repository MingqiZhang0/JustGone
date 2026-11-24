using System;
using JustGone.Core;
using JustGone.Internal;
using Michsky.LSS;
using UnityEngine.SceneManagement;

namespace JustGone.Services
{
    /// <summary>
    /// Scene query and safe scene loading (with native music fade and LoadingScreenManager support).
    /// Extracted from WeiXiangQi v0.10.10.0 (WxqLauncher.cs).
    /// </summary>
    internal sealed class SceneService : ISceneService
    {
        public string CurrentSceneName
        {
            get
            {
                try { return SceneManager.GetActiveScene().name ?? string.Empty; }
                catch { return string.Empty; }
            }
        }

        public SceneKind CurrentSceneKind => JustGoSceneNames.Classify(CurrentSceneName);

        public bool IsMainMenu => CurrentSceneKind == SceneKind.MainMenu;

        public bool IsBoardScene => JustGoSceneNames.IsBoardScene(CurrentSceneKind);

        public bool LoadEnvironmentScene(GameEnvironmentKind environment, bool fadeMusic = true)
        {
            return LoadScene(JustGoSceneNames.ForEnvironment(environment), fadeMusic, true);
        }

        public bool LoadScene(string sceneName, bool fadeMusic = true, bool preferLoadingScreen = true)
        {
            if (string.IsNullOrWhiteSpace(sceneName)) return false;

            if (fadeMusic)
            {
                try
                {
                    if (GameMusic.gameMusicInstance != null)
                        GameMusic.gameMusicInstance.TriggerFadeOut();
                }
                catch (Exception ex)
                {
                    JgLog.Debug("GameMusic fade skipped: " + ex.Message);
                }
            }

            if (preferLoadingScreen)
            {
                try
                {
                    if (LoadingScreenManager.m_Instance != null)
                    {
                        LoadingScreenManager.m_Instance.LoadScene(sceneName);
                        return true;
                    }
                    JgLog.Warn("LoadingScreenManager.m_Instance == null; falling back to SceneManager.LoadScene.");
                }
                catch (Exception ex)
                {
                    JgLog.Warn("LoadingScreenManager.LoadScene failed: " + ex.Message);
                }
            }

            try
            {
                SceneManager.LoadScene(sceneName);
                return true;
            }
            catch (Exception ex)
            {
                JgLog.Error("SceneManager.LoadScene(" + sceneName + ") failed", ex);
                return false;
            }
        }
    }
}
