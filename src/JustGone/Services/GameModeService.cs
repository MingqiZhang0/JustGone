using System;
using System.Collections.Generic;
using JustGone.Core;
using JustGone.Internal;
using UnityEngine;

namespace JustGone.Services
{
    /// <summary>
    /// Custom game mode registration, launch orchestration, active session lifecycle, and native action dispatch.
    /// Extracted and decoupled from WeiXiangQi v0.10.10.0 (WxqLauncher.cs, WxqBootstrap.cs, WxqSession.cs).
    /// </summary>
    internal sealed class GameModeService : IGameModeService
    {
        private sealed class ModeRecord
        {
            public ModContext Owner;
            public GameModeDefinition Definition;
            public IMenuModeRegistration MenuReg;
        }

        private sealed class GameModeRegistration : IGameModeRegistration
        {
            private readonly GameModeService _service;
            private readonly ModeRecord _record;
            private bool _disposed;

            public GameModeRegistration(GameModeService service, ModeRecord record)
            {
                _service = service;
                _record = record;
            }

            public string ModeId => _record.Definition.ModeId;
            public string DisplayTitle => _record.Definition.DisplayTitle;
            public int AssignedSelectorIndex => _record.MenuReg == null ? -1 : _record.MenuReg.AssignedSelectorIndex;

            public bool Enabled
            {
                get => _record.MenuReg != null && _record.MenuReg.Enabled;
                set { if (_record.MenuReg != null) _record.MenuReg.Enabled = value; }
            }

            public void RequestMenuRebuild(bool scrollToTop = false)
            {
                _record.MenuReg?.RequestPageRebuild(scrollToTop);
            }

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                _service.UnregisterMode(_record.Definition.ModeId);
            }
        }

        private readonly MenuService _menu;
        private readonly SceneService _scene;
        private readonly SaveGuardService _saves;
        private readonly BoardService _board;
        private readonly InputService _input;
        private readonly UiService _ui;

        private readonly Dictionary<string, ModeRecord> _modes = new Dictionary<string, ModeRecord>(StringComparer.OrdinalIgnoreCase);

        private string _pendingModeId;
        private string _pendingSceneName;
        private float _lastLaunchAttempt = -999f;

        private ModeRecord _activeRecord;
        private ModeSessionContext _activeContext;
        private IGameModeSession _activeSession;

        public GameModeService(
            MenuService menu,
            SceneService scene,
            SaveGuardService saves,
            BoardService board,
            InputService input,
            UiService ui)
        {
            _menu = menu;
            _scene = scene;
            _saves = saves;
            _board = board;
            _input = input;
            _ui = ui;
        }

        public string ActiveModeId => _activeRecord == null ? null : _activeRecord.Definition.ModeId;
        public bool HasActiveMode => _activeRecord != null && _activeSession != null;
        internal string PendingModeId => _pendingModeId;
        internal string PendingSceneName => _pendingSceneName;

        public IReadOnlyList<GameModeRegistrationInfo> RegisteredModes
        {
            get
            {
                var list = new List<GameModeRegistrationInfo>(_modes.Count);
                foreach (ModeRecord r in _modes.Values)
                {
                    list.Add(new GameModeRegistrationInfo(
                        r.Definition.ModeId,
                        r.Owner == null ? "anonymous" : r.Owner.Info.Id,
                        r.Definition.DisplayTitle,
                        r.MenuReg == null ? -1 : r.MenuReg.AssignedSelectorIndex));
                }
                return list;
            }
        }

        public IGameModeRegistration RegisterGameMode(ModContext owner, GameModeDefinition definition)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            if (string.IsNullOrWhiteSpace(definition.ModeId))
                throw new ArgumentException("GameModeDefinition.ModeId is required.", nameof(definition));
            if (string.IsNullOrWhiteSpace(definition.DisplayTitle))
                throw new ArgumentException("GameModeDefinition.DisplayTitle is required.", nameof(definition));
            if (definition.CreateSession == null)
                throw new ArgumentException("GameModeDefinition.CreateSession is required.", nameof(definition));
            if (_modes.ContainsKey(definition.ModeId))
                throw new InvalidOperationException("Duplicate game mode id: " + definition.ModeId);

            var menuDesc = new MenuModeDescriptor
            {
                ModeId = definition.ModeId,
                DisplayTitle = definition.DisplayTitle,
                ValidateLaunch = definition.ValidateLaunch,
                BuildConfigPage = definition.BuildMenuConfigPage,
                OnLaunchRequested = () =>
                {
                    if (!TryLaunchMode(definition.ModeId, out string err))
                    {
                        _menu.ShowMenuNotice("模式启动失败：" + err, 10f);
                    }
                }
            };

            IMenuModeRegistration menuReg = _menu.RegisterMode(owner, menuDesc);
            var record = new ModeRecord
            {
                Owner = owner,
                Definition = definition,
                MenuReg = menuReg
            };
            _modes[definition.ModeId] = record;

            var reg = new GameModeRegistration(this, record);
            return owner != null ? owner.Track(reg) : reg;
        }

        public bool TryLaunchMode(string modeId, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(modeId) || !_modes.TryGetValue(modeId, out ModeRecord record))
            {
                error = "Unknown game mode: " + modeId;
                return false;
            }

            if (HasActiveMode || !string.IsNullOrEmpty(_pendingModeId))
            {
                error = "Another Just Gone mode is already active or launching.";
                return false;
            }

            if (Time.realtimeSinceStartup - _lastLaunchAttempt < 2f)
            {
                error = "Launch cooldown active; please wait a moment.";
                return false;
            }
            _lastLaunchAttempt = Time.realtimeSinceStartup;

            if (record.Definition.ValidateLaunch != null)
            {
                MenuLaunchValidation v = record.Definition.ValidateLaunch();
                if (v != null && !v.IsValid)
                {
                    error = v.ErrorMessage;
                    return false;
                }
            }

            var spec = new SaveLaunchOverrideSpec
            {
                TargetVanillaMode = record.Definition.BackingVanillaMode,
                TargetEnvironment = record.Definition.TargetEnvironment,
                TargetBoardSize = record.Definition.BoardSize,
                IsSavedGameLoaded = false
            };

            if (!_saves.BeginLaunchOverride(spec, out string saveErr))
            {
                error = "SaveGuard failed: " + saveErr;
                JgLog.Error("Mode launch aborted (" + modeId + "): " + error);
                return false;
            }

            string sceneName = JustGoSceneNames.ForEnvironment(record.Definition.TargetEnvironment);
            _pendingModeId = modeId;
            _pendingSceneName = sceneName;

            if (!_scene.LoadScene(sceneName, fadeMusic: true, preferLoadingScreen: true))
            {
                _pendingModeId = null;
                _pendingSceneName = null;
                _saves.RestoreLastSnapshotSafe();
                error = "Failed loading target scene: " + sceneName;
                return false;
            }

            JgLog.Info("Launching custom mode [" + modeId + "] -> scene=" + sceneName
                       + ", env=" + record.Definition.TargetEnvironment
                       + ", size=" + (int)record.Definition.BoardSize);
            return true;
        }

        public void AbortActiveMode(string stage, string reason)
        {
            JgFailsafe.Abort(stage, reason);
        }

        internal void UnregisterMode(string modeId)
        {
            if (_modes.TryGetValue(modeId, out ModeRecord rec))
            {
                if (_activeRecord == rec) EndActiveSession("mode-unregistered");
                if (string.Equals(_pendingModeId, modeId, StringComparison.OrdinalIgnoreCase))
                {
                    _pendingModeId = null;
                    _pendingSceneName = null;
                }
                rec.MenuReg?.Dispose();
                _modes.Remove(modeId);
            }
        }

        internal void DisarmPending(string reason)
        {
            if (!string.IsNullOrEmpty(_pendingModeId))
            {
                JgLog.Warn("Disarming pending mode [" + _pendingModeId + "]: " + reason);
                _pendingModeId = null;
                _pendingSceneName = null;
            }
        }

        internal void StartPendingSession(string loadedSceneName)
        {
            if (string.IsNullOrEmpty(_pendingModeId)) return;
            string modeId = _pendingModeId;
            _pendingModeId = null;
            _pendingSceneName = null;

            if (!_modes.TryGetValue(modeId, out ModeRecord record))
            {
                JgFailsafe.Abort("mode-start", "Pending mode no longer registered: " + modeId);
                return;
            }

            try
            {
                if (record.Definition.ClearVanillaStonesOnStart)
                    _board.ClearVanillaStones();

                var ctx = new ModeSessionContext(
                    modeId,
                    record.Owner,
                    loadedSceneName,
                    _board.BoardSize,
                    _board.Environment);

                IGameModeSession session = record.Definition.CreateSession(record.Owner, ctx);
                if (session == null)
                    throw new InvalidOperationException("CreateSession returned null for mode " + modeId);

                _activeRecord = record;
                _activeContext = ctx;
                _activeSession = session;
                _input.ActiveModeSuppressesVanillaInput = record.Definition.SuppressVanillaInput;

                WirePracticeButtonsForActiveMode();
                session.OnStart(ctx);
                JgLog.Info("Custom mode session ACTIVE: " + modeId + " (scene=" + loadedSceneName + ", size=" + ctx.BoardSize + ")");
            }
            catch (Exception ex)
            {
                JgLog.Error("Custom mode session start failed (" + modeId + ")", ex);
                JgFailsafe.Abort("mode-start", ex.Message);
            }
        }

        internal void TickActiveSession(float deltaTime)
        {
            if (_activeSession == null || _activeContext == null) return;
            try
            {
                _activeSession.OnUpdate(_activeContext, deltaTime);
            }
            catch (Exception ex)
            {
                JgLog.Error("Custom mode OnUpdate exception (" + ActiveModeId + ")", ex);
            }
        }

        internal bool TryDispatchNativeAction(NativeGameAction action)
        {
            if (_activeSession == null || _activeContext == null) return false;
            try
            {
                return _activeSession.OnNativeAction(_activeContext, action);
            }
            catch (Exception ex)
            {
                JgLog.Error("Custom mode OnNativeAction(" + action + ") failed", ex);
                return true;
            }
        }

        internal void EndActiveSession(string reason)
        {
            if (_activeSession == null && _activeContext == null && _activeRecord == null) return;

            string modeId = ActiveModeId ?? "<unknown>";
            try
            {
                if (_activeSession != null && _activeContext != null)
                    _activeSession.OnEnd(_activeContext);
            }
            catch (Exception ex)
            {
                JgLog.Warn("Custom mode OnEnd warning (" + modeId + "): " + ex.Message);
            }

            try
            {
                _activeContext?.DisposeResources(ex => JgLog.Warn("Session resource dispose error: " + ex.Message));
            }
            catch { }

            _activeSession = null;
            _activeContext = null;
            _activeRecord = null;
            _input.ClearSessionLocks();
            _ui.ClearPracticeButtonOverrides();
            _saves.RestoreLastSnapshotSafe();
            JgLog.Info("Custom mode session ended (" + modeId + ", reason=" + reason + ").");
        }

        private void WirePracticeButtonsForActiveMode()
        {
            GameManager gm = JgGameBridge.TryGetGameManager();
            if (gm == null) return;
            try
            {
                if (gm.PracticePanel != null) gm.PracticePanel.SetActive(true);
                if (gm.PracticeMovePanel != null) gm.PracticeMovePanel.SetActive(true);
                if (gm.BlackMouseOverStone != null) gm.BlackMouseOverStone.SetActive(false);
                if (gm.WhiteMouseOverStone != null) gm.WhiteMouseOverStone.SetActive(false);

                if (gm.UndoButton != null)
                {
                    gm.UndoButton.onClick = new UnityEngine.UI.Button.ButtonClickedEvent();
                    gm.UndoButton.onClick.AddListener(() => TryDispatchNativeAction(NativeGameAction.Undo));
                }
                if (gm.RestartButton != null)
                {
                    gm.RestartButton.onClick = new UnityEngine.UI.Button.ButtonClickedEvent();
                    gm.RestartButton.onClick.AddListener(() => TryDispatchNativeAction(NativeGameAction.Restart));
                }
                if (gm.SaveButton != null)
                {
                    gm.SaveButton.onClick = new UnityEngine.UI.Button.ButtonClickedEvent();
                    gm.SaveButton.onClick.AddListener(() => TryDispatchNativeAction(NativeGameAction.Save));
                }
            }
            catch (Exception ex)
            {
                JgLog.Warn("WirePracticeButtonsForActiveMode warning: " + ex.Message);
            }
        }
    }
}
