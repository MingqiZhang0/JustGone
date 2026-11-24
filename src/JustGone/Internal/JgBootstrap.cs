using System;
using System.Collections;
using JustGone.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace JustGone.Internal
{
    /// <summary>
    /// Persistent DontDestroyOnLoad runtime driver.
    /// Extracted and generalized from WeiXiangQi v0.10.10.0 (WxqBootstrap.cs and WxqSession.cs Update loop).
    /// Handles:
    ///   - Scene load / unload tracking and GameManager readiness waiting
    ///   - Board hover / click / mouse wheel / key / pause event polling and dispatch
    ///   - SessionGuard monitoring for active custom modes
    ///   - Failsafe return-to-menu coroutine with F10/Esc manual escape fallback
    /// </summary>
    internal sealed class JgBootstrap : MonoBehaviour
    {
        private const float InitTimeoutSeconds = 20f;

        private static JgBootstrap _instance;
        private static bool _previousRunInBackground;

        private bool _inBoardScene;
        private string _activeBoardSceneName;
        private bool _gameReadyDispatched;
        private bool _wasPaused;
        private BoardCoord _lastHover = BoardCoord.Invalid;
        private float _gmLostAt = -1f;
        private bool _retryMenuPrompt;

        internal static void EnsureCreated()
        {
            if (_instance != null) return;
            _previousRunInBackground = Application.runInBackground;
            var go = new GameObject("JustGone_Bootstrap");
            try
            {
                UnityEngine.Object.DontDestroyOnLoad(go);
                _instance = go.AddComponent<JgBootstrap>();
                JgLog.Info("Bootstrap created (DontDestroyOnLoad).");
            }
            catch
            {
                if (_instance != null) SceneManager.sceneLoaded -= _instance.OnSceneLoaded;
                _instance = null;
                go.SetActive(false);
                UnityEngine.Object.Destroy(go);
                Application.runInBackground = _previousRunInBackground;
                throw;
            }
        }

        internal static void DestroyIfCreated()
        {
            if (_instance == null) return;
            try
            {
                SceneManager.sceneLoaded -= _instance.OnSceneLoaded;
                GameObject go = _instance.gameObject;
                _instance = null;
                if (go != null)
                {
                    go.SetActive(false);
                    UnityEngine.Object.Destroy(go);
                }
            }
            catch { }
            Application.runInBackground = _previousRunInBackground;
        }

        internal static void ScheduleReturnToMenu(string stage, string reason)
        {
            if (_instance != null)
                _instance.StartCoroutine(_instance.ReturnToMenuRoutine(stage, reason));
        }

        private void Awake()
        {
            Application.runInBackground = true;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            if (_instance == this) _instance = null;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            string sceneName = scene.name ?? string.Empty;
            SceneKind kind = JustGoSceneNames.Classify(sceneName);
            JgLog.Info("Scene loaded: " + sceneName + " (" + kind + ")");

            if (_inBoardScene)
            {
                JustGoneApi.ModesInternal.EndActiveSession("scene-changed:" + sceneName);
                JustGoneApi.EventsInternal.DispatchGameLeaving(new SceneLoadedEventArgs(_activeBoardSceneName));
            }

            _inBoardScene = false;
            _activeBoardSceneName = null;
            _gameReadyDispatched = false;
            _wasPaused = false;
            _lastHover = BoardCoord.Invalid;
            _gmLostAt = -1f;
            JustGoneApi.BoardInternal.SetHoveredCell(BoardCoord.Invalid);
            JustGoneApi.UiInternal.OnSceneChanged();

            if (kind == SceneKind.MainMenu)
            {
                JustGoneApi.ModesInternal.DisarmPending("returned-to-menu");
                JgFailsafe.Clear();
                _retryMenuPrompt = false;
                JustGoneApi.MenuInternal.OnMainMenuEntered(JustGoneApi.ResourcesInternal);
            }
            else if (JustGoSceneNames.IsBoardScene(kind))
            {
                _inBoardScene = true;
                _activeBoardSceneName = sceneName;
                StartCoroutine(WaitForGameReadyRoutine(sceneName));
            }

            JustGoneApi.EventsInternal.DispatchSceneLoaded(new SceneLoadedEventArgs(sceneName));
        }

        private IEnumerator WaitForGameReadyRoutine(string sceneName)
        {
            float t0 = Time.realtimeSinceStartup;
            GameManager gm = null;

            while (gm == null && Time.realtimeSinceStartup - t0 < InitTimeoutSeconds)
            {
                gm = JgGameBridge.TryGetGameManager();
                if (SceneManager.GetActiveScene().name != sceneName) yield break;
                yield return null;
            }

            if (gm == null)
            {
                if (!string.IsNullOrEmpty(JustGoneApi.ModesInternal.PendingModeId))
                    JgFailsafe.Abort("bootstrap", "GameManager.m_Instance never appeared in " + sceneName);
                yield break;
            }

            while (!JgGameBridge.IsGameStarted(gm))
            {
                if (Time.realtimeSinceStartup - t0 > InitTimeoutSeconds)
                {
                    if (!string.IsNullOrEmpty(JustGoneApi.ModesInternal.PendingModeId))
                        JgFailsafe.Abort("bootstrap", "GameManager.Start() did not complete within " + InitTimeoutSeconds + "s");
                    yield break;
                }
                if (SceneManager.GetActiveScene().name != sceneName) yield break;
                yield return null;
            }

            // If a Just Gone custom mode is armed for this scene, start it before broadcasting GameReady
            string pendingScene = JustGoneApi.ModesInternal.PendingSceneName;
            if (!string.IsNullOrEmpty(JustGoneApi.ModesInternal.PendingModeId)
                && (string.IsNullOrEmpty(pendingScene) || string.Equals(pendingScene, sceneName, StringComparison.Ordinal)))
            {
                JustGoneApi.ModesInternal.StartPendingSession(sceneName);
            }

            _gameReadyDispatched = true;
            _wasPaused = JustGoneApi.Game.IsPaused;

            var readyArgs = new GameReadyEventArgs(
                sceneName,
                JgGameBridge.GetVanillaMode(gm),
                JgGameBridge.GetEnvironment(gm),
                JgGameBridge.GetBoardSize(gm),
                JustGoneApi.ModesInternal.ActiveModeId);

            JustGoneApi.EventsInternal.DispatchGameReady(readyArgs);
        }

        private void Update()
        {
            if (_retryMenuPrompt)
            {
                if (Input.GetKeyDown(KeyCode.F10) || Input.GetKeyDown(KeyCode.Escape))
                {
                    JgLog.Info("FAILSAFE: manual menu return triggered (F10/Esc).");
                    JustGoneApi.Game.ReturnToMainMenu();
                }
            }

            bool textFocused = JustGoneApi.InputInternal.IsTextInputFocused;
            JustGoneApi.EventsInternal.PollKeySubscriptions(textFocused);

            if (!_inBoardScene || !_gameReadyDispatched) return;

            if (!CheckGameManagerHealth()) return;

            bool paused = JustGoneApi.Game.IsPaused;
            if (paused != _wasPaused)
            {
                _wasPaused = paused;
                JustGoneApi.EventsInternal.DispatchPauseChanged(new PauseChangedEventArgs(paused));
            }

            PollBoardAndPointerEvents();
            JustGoneApi.ModesInternal.TickActiveSession(Time.deltaTime);
        }

        private bool CheckGameManagerHealth()
        {
            bool alive = JgGameBridge.TryGetGameManager() != null;
            if (alive)
            {
                _gmLostAt = -1f;
                return true;
            }

            if (!JustGoneApi.ModesInternal.HasActiveMode) return false;

            if (_gmLostAt < 0f)
            {
                _gmLostAt = Time.realtimeSinceStartup;
                JgLog.Warn("GameManager lost during active mode; starting SessionGuard grace window...");
            }

            Scene activeScene = SceneManager.GetActiveScene();
            bool expectedScene = activeScene.IsValid() && activeScene.isLoaded
                && string.Equals(activeScene.name, _activeBoardSceneName, StringComparison.Ordinal);

            float lostFor = Time.realtimeSinceStartup - _gmLostAt;
            SessionGuardDecision decision = SessionGuard.Decide(
                false, expectedScene, expectedScene, lostFor, SessionGuard.DefaultGraceSeconds);

            switch (decision)
            {
                case SessionGuardDecision.WaitForSceneChange:
                    return false;
                case SessionGuardDecision.TeardownNormal:
                    JustGoneApi.ModesInternal.EndActiveSession("scene-unloading");
                    return false;
                case SessionGuardDecision.AbortFailsafe:
                    JgFailsafe.Abort("session-guard", "GameManager became invalid while " + _activeBoardSceneName + " remained loaded");
                    return false;
                default:
                    return false;
            }
        }

        private void PollBoardAndPointerEvents()
        {
            if (!JustGoneApi.BoardInternal.IsAvailable) return;

            BoardCoord currentHover = BoardCoord.Invalid;
            JustGoneApi.BoardInternal.TryScreenToCell(Input.mousePosition, out currentHover);
            if (currentHover != _lastHover)
            {
                BoardCoord prev = _lastHover;
                _lastHover = currentHover;
                JustGoneApi.BoardInternal.SetHoveredCell(currentHover);
                JustGoneApi.EventsInternal.DispatchBoardHover(
                    new BoardHoverEventArgs(prev, currentHover, JustGoneApi.BoardInternal.BoardSize));
            }

            bool overUi = JustGoneApi.InputInternal.IsPointerOverUi;

            for (int btn = 0; btn <= 2; btn++)
            {
                if (Input.GetMouseButtonDown(btn))
                {
                    JustGoneApi.EventsInternal.DispatchBoardClick(
                        new BoardClickEventArgs(currentHover, btn, JustGoneApi.BoardInternal.BoardSize, overUi));
                }
            }

            float wheel = Input.mouseScrollDelta.y;
            if (Mathf.Abs(wheel) > 0.01f)
            {
                JustGoneApi.EventsInternal.DispatchMouseWheel(new MouseWheelEventArgs(wheel, overUi));
            }
        }

        private IEnumerator ReturnToMenuRoutine(string stage, string reason)
        {
            yield return new WaitForSecondsRealtime(2.0f);
            if (SceneManager.GetActiveScene().name == JustGoSceneNames.MainScene) yield break;

            JgLog.Info("FAILSAFE: returning to MainScene (" + stage + ")");
            JustGoneApi.Game.ReturnToMainMenu();

            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < 10f)
            {
                if (SceneManager.GetActiveScene().name == JustGoSceneNames.MainScene) yield break;
                yield return null;
            }

            _retryMenuPrompt = true;
            JustGoneApi.UiInternal.ShowStickyError("自动返回主菜单失败，请按 F10 或 Esc 返回主菜单。(" + reason + ")");
            JgLog.Error("FAILSAFE: automatic return timed out; manual F10/Esc exit enabled.");
        }
    }
}
