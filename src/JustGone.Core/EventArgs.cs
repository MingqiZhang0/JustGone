using System;

namespace JustGone.Core
{
    public sealed class SceneLoadedEventArgs : EventArgs
    {
        public string SceneName { get; }
        public SceneKind SceneKind { get; }

        public SceneLoadedEventArgs(string sceneName)
        {
            SceneName = sceneName ?? string.Empty;
            SceneKind = JustGoSceneNames.Classify(SceneName);
        }
    }

    public sealed class GameReadyEventArgs : EventArgs
    {
        public string SceneName { get; }
        public SceneKind SceneKind { get; }
        public VanillaGameMode VanillaMode { get; }
        public GameEnvironmentKind Environment { get; }
        public int BoardSize { get; }
        public string ActiveCustomModeId { get; }

        public GameReadyEventArgs(
            string sceneName,
            VanillaGameMode vanillaMode,
            GameEnvironmentKind environment,
            int boardSize,
            string activeCustomModeId)
        {
            SceneName = sceneName ?? string.Empty;
            SceneKind = JustGoSceneNames.Classify(SceneName);
            VanillaMode = vanillaMode;
            Environment = environment;
            BoardSize = boardSize;
            ActiveCustomModeId = activeCustomModeId;
        }
    }

    public sealed class BoardHoverEventArgs : EventArgs
    {
        public BoardCoord Previous { get; }
        public BoardCoord Current { get; }
        public int BoardSize { get; }

        public BoardHoverEventArgs(BoardCoord previous, BoardCoord current, int boardSize)
        {
            Previous = previous;
            Current = current;
            BoardSize = boardSize;
        }
    }

    public sealed class BoardClickEventArgs : EventArgs
    {
        public BoardCoord Coord { get; }
        public int MouseButton { get; }
        public int BoardSize { get; }
        public bool PointerOverUi { get; }
        public bool Consumed { get; private set; }

        public BoardClickEventArgs(BoardCoord coord, int mouseButton, int boardSize, bool pointerOverUi)
        {
            Coord = coord;
            MouseButton = mouseButton;
            BoardSize = boardSize;
            PointerOverUi = pointerOverUi;
        }

        public void Consume() => Consumed = true;
    }

    public sealed class KeyPressEventArgs : EventArgs
    {
        public int KeyCodeValue { get; }
        public string KeyName { get; }
        public bool IsTextInputFocused { get; }
        public bool Consumed { get; private set; }

        public KeyPressEventArgs(int keyCodeValue, string keyName, bool isTextInputFocused)
        {
            KeyCodeValue = keyCodeValue;
            KeyName = keyName ?? string.Empty;
            IsTextInputFocused = isTextInputFocused;
        }

        public void Consume() => Consumed = true;
    }

    public sealed class MouseWheelEventArgs : EventArgs
    {
        public float DeltaY { get; }
        public bool PointerOverUi { get; }
        public bool Consumed { get; private set; }

        public MouseWheelEventArgs(float deltaY, bool pointerOverUi)
        {
            DeltaY = deltaY;
            PointerOverUi = pointerOverUi;
        }

        public void Consume() => Consumed = true;
    }

    public sealed class PauseChangedEventArgs : EventArgs
    {
        public bool IsPaused { get; }

        public PauseChangedEventArgs(bool isPaused)
        {
            IsPaused = isPaused;
        }
    }

    public sealed class NativeActionEventArgs : EventArgs
    {
        public NativeGameAction Action { get; }
        public bool Consumed { get; private set; }

        public NativeActionEventArgs(NativeGameAction action)
        {
            Action = action;
        }

        public void Consume() => Consumed = true;
    }

    /// <summary>
    /// Validation outcome returned before launching a custom menu mode.
    /// </summary>
    public sealed class MenuLaunchValidation
    {
        public bool IsValid { get; }
        public string ErrorMessage { get; }

        private MenuLaunchValidation(bool isValid, string errorMessage)
        {
            IsValid = isValid;
            ErrorMessage = errorMessage ?? string.Empty;
        }

        public static MenuLaunchValidation Ok() => new MenuLaunchValidation(true, string.Empty);

        public static MenuLaunchValidation Fail(string reason) =>
            new MenuLaunchValidation(false, string.IsNullOrWhiteSpace(reason) ? "Launch configuration is incomplete." : reason);
    }

    /// <summary>
    /// Launch parameters written transactionally via SaveGuard before loading a Just Go board scene.
    /// </summary>
    public sealed class SaveLaunchOverrideSpec
    {
        public VanillaGameMode TargetVanillaMode { get; set; } = VanillaGameMode.LocalPractice;
        public GameEnvironmentKind TargetEnvironment { get; set; } = GameEnvironmentKind.ChineseGarden;
        public BoardSizeKind TargetBoardSize { get; set; } = BoardSizeKind.Nineteen;
        public bool IsSavedGameLoaded { get; set; } = false;
    }
}
