using System;
using System.Collections.Generic;
using JustGone.Core;
using UnityEngine;

namespace JustGone
{
    public interface IModLogger
    {
        void Info(string message);
        void Warn(string message);
        void Error(string message);
        void Error(string message, Exception ex);
        void Debug(string message);
    }

    public sealed class PieceVisualSpec
    {
        public StoneBaseStyle BaseStyle { get; set; } = StoneBaseStyle.Black;
        public Color? Tint { get; set; }
        public string LabelText { get; set; }
        public Color LabelColor { get; set; } = Color.white;
        public float Scale { get; set; } = 1f;
        public bool UseCjkFont { get; set; } = true;

        public PieceVisualSpec Clone()
        {
            return new PieceVisualSpec
            {
                BaseStyle = BaseStyle,
                Tint = Tint,
                LabelText = LabelText,
                LabelColor = LabelColor,
                Scale = Scale,
                UseCjkFont = UseCjkFont
            };
        }
    }

    public interface IPieceLayer : IDisposable
    {
        string LayerName { get; }
        int Count { get; }
        bool Spawn(int pieceId, BoardCoord coord, PieceVisualSpec spec, bool animateDrop = true);
        bool Move(int pieceId, BoardCoord targetCoord, bool animateMove = true);
        bool UpdateVisual(int pieceId, PieceVisualSpec spec);
        bool Remove(int pieceId);
        void SetSelected(int? pieceId);
        void PlayDropSound();
        bool TryGetPieceCoord(int pieceId, out BoardCoord coord);
        void Clear();
    }

    public interface IDisplayShelf : IDisposable
    {
        string ShelfName { get; }
        bool AddItem(string itemId, PieceVisualSpec spec, Vector3 boardRelativeOffset);
        void SetVisible(bool visible);
        void SetItemState(string itemId, bool enabled, bool selected);
        bool TryRaycastItem(Ray ray, out string itemId);
        void Clear();
    }

    public interface IMarkerLayer : IDisposable
    {
        string LayerName { get; }
        int Count { get; }
        void SetMarker(BoardCoord coord, Color color, float diameterScale = 1f, float heightScale = 1f);
        bool RemoveMarker(BoardCoord coord);
        void Clear();
    }

    public interface IGridOverlayLayer : IDisposable
    {
        string LayerName { get; }
        void SetVisible(bool visible);
        void SetCellColor(BoardCoord coord, Color color);
        void ClearCell(BoardCoord coord);
        void ClearAll();
    }

    public sealed class SidebarPanelOptions
    {
        public string Name { get; set; } = "CustomSidebar";
        public string Title { get; set; } = "Panel";
        public string FooterText { get; set; } = string.Empty;
        public bool MirrorNativeHistoryPanel { get; set; } = true;
        public Vector2 AnchorMin { get; set; } = new Vector2(0.035f, 0.145f);
        public Vector2 AnchorMax { get; set; } = new Vector2(0.21f, 0.895f);
        public bool StartExpanded { get; set; } = true;
        public bool StartVisible { get; set; } = true;
    }

    public sealed class SidebarItem
    {
        public string Id { get; set; }
        public string Text { get; set; }
        public float PreferredHeight { get; set; } = 56f;
        public bool Interactable { get; set; } = true;
        public Action OnClick { get; set; }
    }

    public interface ISidebarPanel : IDisposable
    {
        bool IsVisible { get; }
        bool IsExpanded { get; }
        void SetVisible(bool visible);
        void SetExpanded(bool expanded);
        void SetTitle(string title);
        void SetFooterText(string text);
        void SetItems(IReadOnlyList<SidebarItem> items);
        void SetSelectedItem(string itemId);
        void ClearItems();
    }

    public sealed class HistoryListItem
    {
        public string IndexLabel { get; set; }
        public string SummaryText { get; set; }
    }

    public sealed class GameOverButtonOption
    {
        public string Label { get; set; }
        public Action OnClick { get; set; }
    }

    public sealed class GameOverViewOptions
    {
        public string Title { get; set; }
        public string Subtitle { get; set; }
        public string Detail { get; set; }
        public Action OnReturnToMenu { get; set; }
        public IReadOnlyList<GameOverButtonOption> ExtraButtons { get; set; }
    }

    public sealed class SaveDialogResult
    {
        public bool Saved { get; set; }
        public bool Cancelled { get; set; }
        public bool UsedFallback { get; set; }
        public string FilePath { get; set; }
        public string ErrorMessage { get; set; }
    }

    public interface IMenuPageBuilder
    {
        void AddChoiceRow(
            string title,
            string[] options,
            int selectedIndex,
            Action<int> onChanged,
            Action onValueClicked = null,
            Func<int, string> formatLabel = null);

        void AddInputRow(
            string title,
            string initialValue,
            Action<string> onChanged,
            string placeholder = null,
            bool password = false);

        void AddHeaderRow(string title, string hintText, Action onClick);
        void AddStatusRow(string title, string value);
        void AddButtonRow(string title, string[] labels, Action[] actions);
        void AddBottomAction(string label, Action onClick, float width = 260f);
        void ShowTransientNotice(string message, float durationSeconds = 6f);
        void RequestRebuild(bool scrollToTop = false);
    }

    public sealed class MenuModeDescriptor
    {
        public string ModeId { get; set; }
        public string DisplayTitle { get; set; }
        public Func<MenuLaunchValidation> ValidateLaunch { get; set; }
        public Action<IMenuPageBuilder> BuildConfigPage { get; set; }
        public Action OnLaunchRequested { get; set; }
    }

    public interface IMenuModeRegistration : IDisposable
    {
        string ModeId { get; }
        string DisplayTitle { get; }
        int AssignedSelectorIndex { get; }
        bool Enabled { get; set; }
        void RequestPageRebuild(bool scrollToTop = false);
    }

    public sealed class GameModeDefinition
    {
        public string ModeId { get; set; }
        public string DisplayTitle { get; set; }
        public GameEnvironmentKind TargetEnvironment { get; set; } = GameEnvironmentKind.ChineseGarden;
        public BoardSizeKind BoardSize { get; set; } = BoardSizeKind.Nineteen;
        public VanillaGameMode BackingVanillaMode { get; set; } = VanillaGameMode.LocalPractice;
        public bool SuppressVanillaInput { get; set; } = true;
        public bool ClearVanillaStonesOnStart { get; set; } = true;
        public Func<MenuLaunchValidation> ValidateLaunch { get; set; }
        public Action<IMenuPageBuilder> BuildMenuConfigPage { get; set; }
        public Func<ModContext, ModeSessionContext, IGameModeSession> CreateSession { get; set; }
    }

    public sealed class GameModeRegistrationInfo
    {
        public string ModeId { get; }
        public string OwnerModId { get; }
        public string DisplayTitle { get; }
        public int SelectorIndex { get; }

        public GameModeRegistrationInfo(string modeId, string ownerModId, string displayTitle, int selectorIndex)
        {
            ModeId = modeId;
            OwnerModId = ownerModId;
            DisplayTitle = displayTitle;
            SelectorIndex = selectorIndex;
        }
    }

    public interface IGameModeRegistration : IDisposable
    {
        string ModeId { get; }
        string DisplayTitle { get; }
        int AssignedSelectorIndex { get; }
        bool Enabled { get; set; }
        void RequestMenuRebuild(bool scrollToTop = false);
    }

    public sealed class ModeSessionContext
    {
        public string ModeId { get; }
        public ModContext Owner { get; }
        public string SceneName { get; }
        public int BoardSize { get; }
        public GameEnvironmentKind Environment { get; }

        private readonly DisposableTracker _sessionResources = new DisposableTracker();

        internal ModeSessionContext(
            string modeId,
            ModContext owner,
            string sceneName,
            int boardSize,
            GameEnvironmentKind environment)
        {
            ModeId = modeId;
            Owner = owner;
            SceneName = sceneName;
            BoardSize = boardSize;
            Environment = environment;
        }

        /// <summary>
        /// Tracks a resource scoped to this game session; automatically disposed on session end or Failsafe.
        /// </summary>
        public T Track<T>(T disposable) where T : IDisposable => _sessionResources.Track(disposable);

        internal void DisposeResources(Action<Exception> onError = null) => _sessionResources.DisposeAll(onError);
    }

    public interface IGameModeSession
    {
        void OnStart(ModeSessionContext ctx);
        void OnUpdate(ModeSessionContext ctx, float deltaTime);
        void OnEnd(ModeSessionContext ctx);
        bool OnNativeAction(ModeSessionContext ctx, NativeGameAction action);
    }

    // ------------------------------------------------------------------
    // 12 Public Service Interfaces
    // ------------------------------------------------------------------

    public interface IGameService
    {
        bool IsInMatchScene { get; }
        bool IsGameStarted { get; }
        bool IsPaused { get; }
        bool IsGameOver { get; }
        VanillaGameMode CurrentVanillaMode { get; }
        GameEnvironmentKind CurrentEnvironment { get; }
        int CurrentBoardSize { get; }
        void PauseOrResume();
        bool TrySetCamera(VirtualCameraKind camera, float transitionSeconds = 1.5f);
        void ReturnToMainMenu();
    }

    public interface ISceneService
    {
        string CurrentSceneName { get; }
        SceneKind CurrentSceneKind { get; }
        bool IsMainMenu { get; }
        bool IsBoardScene { get; }
        bool LoadScene(string sceneName, bool fadeMusic = true, bool preferLoadingScreen = true);
        bool LoadEnvironmentScene(GameEnvironmentKind environment, bool fadeMusic = true);
    }

    public interface IBoardService
    {
        bool IsAvailable { get; }
        int BoardSize { get; }
        GameEnvironmentKind Environment { get; }
        BoardCoord HoveredCell { get; }
        Vector3 CellToLocal(BoardCoord coord);
        Vector3 CellToWorld(BoardCoord coord);
        bool TryCellToScreen(BoardCoord coord, out Vector2 screenPos);
        bool TryScreenToCell(Vector2 screenPos, out BoardCoord coord);
        bool TryRayToCell(Ray ray, out BoardCoord coord);
        bool TryGetBoardScreenCenter(out Vector2 screenCenter);
        float GetCellSpacingLocal();
        int ClearVanillaStones();
    }

    public interface IPieceService
    {
        IPieceLayer CreateLayer(ModContext owner, string layerName, bool naturalJitter = true);
        IDisplayShelf CreateDisplayShelf(ModContext owner, string shelfName);
    }

    public interface IRenderingService
    {
        IMarkerLayer CreateMarkerLayer(ModContext owner, string layerName);
        IGridOverlayLayer CreateGridOverlay(ModContext owner, string layerName);
        void ShowHoverGhost(StoneBaseStyle style, BoardCoord coord);
        void HideHoverGhost();
    }

    public interface IInputService
    {
        bool IsPointerOverUi { get; }
        bool IsTextInputFocused { get; }
        bool IsVanillaInputSuppressed { get; }
        IDisposable AcquireVanillaInputLock(ModContext owner, string reason);
        IDisposable RegisterMenuWheelAbsorber(ModContext owner, Func<bool> shouldAbsorb);
    }

    public interface IMenuService
    {
        bool IsMainMenuReady { get; }
        IMenuModeRegistration RegisterMode(ModContext owner, MenuModeDescriptor descriptor);
        void ShowMenuNotice(string message, float durationSeconds = 8f);
    }

    public interface IUiService
    {
        void ShowToast(string message, float durationSeconds = 2.8f);
        void ShowStickyError(string message);
        void ClearStickyError();
        ISidebarPanel CreateSidebarPanel(ModContext owner, SidebarPanelOptions options);
        void SetPracticeButtonStates(bool? undoInteractable, bool? saveInteractable, bool? restartInteractable);
        void ClearPracticeButtonOverrides();
        void SetPracticeHistory(IReadOnlyList<HistoryListItem> items, Action<int> onItemClicked, int selectedIndex = -1);
        void ShowGameOver(GameOverViewOptions options);
        void HideGameOver();
        void ShowSaveFileDialog(
            string defaultFileName,
            string extension,
            string content,
            Action<SaveDialogResult> onCompleted,
            string fallbackDirectory = null);
    }

    public interface IGameModeService
    {
        IReadOnlyList<GameModeRegistrationInfo> RegisteredModes { get; }
        string ActiveModeId { get; }
        bool HasActiveMode { get; }
        IGameModeRegistration RegisterGameMode(ModContext owner, GameModeDefinition definition);
        bool TryLaunchMode(string modeId, out string error);
        void AbortActiveMode(string stage, string reason);
    }

    public interface IEventService
    {
        IDisposable OnSceneLoaded(ModContext owner, Action<SceneLoadedEventArgs> handler);
        IDisposable OnGameReady(ModContext owner, Action<GameReadyEventArgs> handler);
        IDisposable OnGameLeaving(ModContext owner, Action<SceneLoadedEventArgs> handler);
        IDisposable OnBoardHoverChanged(ModContext owner, Action<BoardHoverEventArgs> handler);
        IDisposable OnBoardClicked(ModContext owner, Action<BoardClickEventArgs> handler);
        IDisposable OnKeyPressed(ModContext owner, KeyCode key, Action<KeyPressEventArgs> handler);
        IDisposable OnMouseWheel(ModContext owner, Action<MouseWheelEventArgs> handler);
        IDisposable OnPauseChanged(ModContext owner, Action<PauseChangedEventArgs> handler);
        IDisposable OnNativeAction(ModContext owner, Action<NativeActionEventArgs> handler);
    }

    public interface IResourceService
    {
        bool HasNativeStonePrefabs { get; }
        bool HasNativeOwnershipPrefab { get; }
        bool HasCjkFont { get; }
        bool EnsureFontForCharacters(string requiredCharacters);
        string GetModConfigDirectory(ModContext owner);
    }

    public interface ISaveGuardService
    {
        string BackupDirectory { get; }
        bool BeginLaunchOverride(SaveLaunchOverrideSpec spec, out string error);
        bool RestoreLastSnapshot(out string error);
        void RestoreLastSnapshotSafe();
        int ScanIncompleteTransactions();
    }
}
