using System;

namespace JustGone.Core
{
    /// <summary>
    /// Supported board dimensions in Just Go (maps to GoEnum.GoBoardSize without exposing Assembly-CSharp).
    /// </summary>
    public enum BoardSizeKind
    {
        Nine = 9,
        Thirteen = 13,
        Nineteen = 19
    }

    /// <summary>
    /// Native Just Go board environments (numeric values match GoEnum.GameEnvironment).
    /// </summary>
    public enum GameEnvironmentKind
    {
        Commentary = 0,
        Tatami = 1,
        MatchRoom = 2,
        ChineseGarden = 3
    }

    /// <summary>
    /// Native Just Go game modes (numeric values match GoEnum.GameMode).
    /// </summary>
    public enum VanillaGameMode
    {
        LocalSingle = 0,
        LocalMulti = 1,
        LocalCareerMode = 2,
        LocalReplay = 3,
        LocalTsumego = 4,
        OnlineMultiOgs = 5,
        OnlineMultiSteam = 6,
        LocalPractice = 7
    }

    /// <summary>
    /// Known Just Go scene classifications.
    /// </summary>
    public enum SceneKind
    {
        Unknown = 0,
        MainMenu = 1,
        ChineseGarden = 2,
        JapaneseRoom = 3,
        CommentaryRoom = 4,
        MatchRoom = 5,
        TutorialJapaneseRoom = 6,
        TutorialChineseGarden = 7
    }

    /// <summary>
    /// Native stone prefab style used as base model for custom pieces.
    /// </summary>
    public enum StoneBaseStyle
    {
        Black = 0,
        White = 1
    }

    /// <summary>
    /// Native Just Go virtual camera presets (numeric values match GoEnum.VirtualCameraType).
    /// </summary>
    public enum VirtualCameraKind
    {
        StartCamera1 = 0,
        StartCamera2 = 1,
        StartCameraReplay = 2,
        EndCameraTsumego = 3,
        GameOver = 4
    }

    /// <summary>
    /// Native Practice UI actions that can be observed or intercepted by custom game modes.
    /// </summary>
    public enum NativeGameAction
    {
        Undo = 0,
        Restart = 1,
        Save = 2
    }

    /// <summary>
    /// Conversion helpers for board sizes, environments, and scene names.
    /// </summary>
    public static class JustGoSceneNames
    {
        public const string MainScene = "MainScene";
        public const string ChineseGarden = "GameScene_ChineseGarden";
        public const string JapaneseRoom = "GameScene_JapaneseRoom";
        public const string CommentaryRoom = "GameScene_CommentaryRoom";
        public const string MatchRoom = "GameScene_MatchRoom";
        public const string TutorialJapaneseRoom = "Tutorial_JapaneseRoom";
        public const string TutorialChineseGarden = "Tutorial_ChineseGarden";

        public static string ForEnvironment(GameEnvironmentKind env)
        {
            switch (env)
            {
                case GameEnvironmentKind.Commentary: return CommentaryRoom;
                case GameEnvironmentKind.Tatami: return JapaneseRoom;
                case GameEnvironmentKind.MatchRoom: return MatchRoom;
                case GameEnvironmentKind.ChineseGarden: return ChineseGarden;
                default: return ChineseGarden;
            }
        }

        public static SceneKind Classify(string sceneName)
        {
            if (string.IsNullOrEmpty(sceneName)) return SceneKind.Unknown;
            switch (sceneName)
            {
                case MainScene: return SceneKind.MainMenu;
                case ChineseGarden: return SceneKind.ChineseGarden;
                case JapaneseRoom: return SceneKind.JapaneseRoom;
                case CommentaryRoom: return SceneKind.CommentaryRoom;
                case MatchRoom: return SceneKind.MatchRoom;
                case TutorialJapaneseRoom: return SceneKind.TutorialJapaneseRoom;
                case TutorialChineseGarden: return SceneKind.TutorialChineseGarden;
                default: return SceneKind.Unknown;
            }
        }

        public static bool IsBoardScene(SceneKind kind)
        {
            return kind == SceneKind.ChineseGarden
                || kind == SceneKind.JapaneseRoom
                || kind == SceneKind.CommentaryRoom
                || kind == SceneKind.MatchRoom
                || kind == SceneKind.TutorialJapaneseRoom
                || kind == SceneKind.TutorialChineseGarden;
        }

        /// <summary>
        /// Maps BoardSizeKind to the ES3 BoardSizeIndex convention verified in WeiXiangQi v0.10.10.0
        /// (0 = 19x19, 1 = 13x13, 2 = 9x9 in Local Play selector ordering).
        /// </summary>
        public static int ToEs3BoardSizeIndex(BoardSizeKind size)
        {
            switch (size)
            {
                case BoardSizeKind.Nineteen: return 0;
                case BoardSizeKind.Thirteen: return 1;
                case BoardSizeKind.Nine: return 2;
                default: return 0;
            }
        }

        public static BoardSizeKind BoardSizeFromInt(int size)
        {
            if (size == 9) return BoardSizeKind.Nine;
            if (size == 13) return BoardSizeKind.Thirteen;
            return BoardSizeKind.Nineteen;
        }
    }
}
