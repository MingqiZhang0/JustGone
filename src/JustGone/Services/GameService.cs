using System;
using JustGone.Core;
using JustGone.Internal;
using UnityEngine.SceneManagement;

namespace JustGone.Services
{
    /// <summary>
    /// High-level game state, pause, camera, and menu return API.
    /// Extracted from WeiXiangQi v0.10.10.0 (WxqSession.cs, WxqBootstrap.cs, WxqNativeUiAdapter.cs).
    /// </summary>
    internal sealed class GameService : IGameService
    {
        public bool IsInMatchScene => JgGameBridge.TryGetGameManager() != null;

        public bool IsGameStarted => JgGameBridge.IsGameStarted(JgGameBridge.TryGetGameManager());

        public bool IsPaused
        {
            get
            {
                GameManager gm = JgGameBridge.TryGetGameManager();
                if (gm == null) return false;
                try { return gm.IsGamePaused(); }
                catch { return false; }
            }
        }

        public bool IsGameOver => JgGameBridge.IsGameOver(JgGameBridge.TryGetGameManager());

        public VanillaGameMode CurrentVanillaMode => JgGameBridge.GetVanillaMode(JgGameBridge.TryGetGameManager());

        public GameEnvironmentKind CurrentEnvironment => JgGameBridge.GetEnvironment(JgGameBridge.TryGetGameManager());

        public int CurrentBoardSize => JgGameBridge.GetBoardSize(JgGameBridge.TryGetGameManager());

        public void PauseOrResume()
        {
            GameManager gm = JgGameBridge.TryGetGameManager();
            if (gm == null) return;
            try { gm.PauseOrResumeGame(); }
            catch (Exception ex) { JgLog.Warn("PauseOrResume failed: " + ex.Message); }
        }

        public bool TrySetCamera(VirtualCameraKind camera, float transitionSeconds = 1.5f)
        {
            GameManager gm = JgGameBridge.TryGetGameManager();
            return JgGameBridge.TrySetCamera(gm, camera, transitionSeconds);
        }

        public void ReturnToMainMenu()
        {
            try
            {
                GameManager gm = JgGameBridge.TryGetGameManager();
                if (gm != null)
                {
                    gm.BackToMainScene();
                    return;
                }
            }
            catch (Exception ex)
            {
                JgLog.Warn("BackToMainScene failed (" + ex.Message + "); falling back to SceneManager.");
            }

            try
            {
                SceneManager.LoadScene(JustGoSceneNames.MainScene);
            }
            catch (Exception ex)
            {
                JgLog.Error("Failed returning to MainScene", ex);
            }
        }
    }
}
