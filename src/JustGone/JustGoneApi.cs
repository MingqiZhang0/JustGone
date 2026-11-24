using System;
using System.Collections.Generic;
using JustGone.Core;
using JustGone.Internal;
using JustGone.Services;

namespace JustGone
{
    /// <summary>
    /// Static public entry point for Just Gone — The Just Go Modding Extension.
    /// Third-party MODs interact exclusively through JustGoneApi and their scoped ModContext,
    /// without referencing Assembly-CSharp or writing duplicate Harmony hooks.
    /// </summary>
    public static class JustGoneApi
    {
        public const string FrameworkGuid = "dev.justgone.framework";
        public const string FrameworkName = "Just Gone";
        public const string FrameworkVersion = "0.1.0";

        private static readonly object RegistryLock = new object();
        private static readonly Dictionary<string, ModContext> Mods = new Dictionary<string, ModContext>(StringComparer.OrdinalIgnoreCase);

        internal static readonly ResourceService ResourcesInternal = new ResourceService();
        internal static readonly EventService EventsInternal = new EventService();
        internal static readonly InputService InputInternal = new InputService();
        internal static readonly SaveGuardService SavesInternal = new SaveGuardService();
        internal static readonly GameService GameInternal = new GameService();
        internal static readonly SceneService SceneInternal = new SceneService();
        internal static readonly BoardService BoardInternal = new BoardService();
        internal static readonly PieceService PiecesInternal = new PieceService(BoardInternal, ResourcesInternal);
        internal static readonly RenderingService RenderingInternal = new RenderingService(BoardInternal);
        internal static readonly UiService UiInternal = new UiService(BoardInternal, ResourcesInternal);
        internal static readonly MenuService MenuInternal = new MenuService();
        internal static readonly GameModeService ModesInternal = new GameModeService(
            MenuInternal, SceneInternal, SavesInternal, BoardInternal, InputInternal, UiInternal);

        /// <summary>True once Just Gone Harmony patches and runtime bootstrap have initialized cleanly.</summary>
        public static bool IsReady { get; internal set; }

        public static IGameService Game => GameInternal;
        public static ISceneService Scene => SceneInternal;
        public static IBoardService Board => BoardInternal;
        public static IPieceService Pieces => PiecesInternal;
        public static IRenderingService Rendering => RenderingInternal;
        public static IInputService Input => InputInternal;
        public static IMenuService Menu => MenuInternal;
        public static IUiService UI => UiInternal;
        public static IGameModeService Modes => ModesInternal;
        public static IEventService Events => EventsInternal;
        public static IResourceService Resources => ResourcesInternal;
        public static ISaveGuardService Saves => SavesInternal;

        /// <summary>
        /// Registers a third-party MOD and returns its isolated ModContext.
        /// Dispose the returned context in OnDestroy() to automatically release all owned resources.
        /// </summary>
        public static ModContext RegisterMod(string id, string displayName, string version)
        {
            var info = new ModInfo(id, displayName, version);
            lock (RegistryLock)
            {
                if (Mods.ContainsKey(info.Id))
                    throw new InvalidOperationException("A MOD with id '" + info.Id + "' is already registered.");

                var ctx = new ModContext(info, UnregisterModInternal);
                Mods[info.Id] = ctx;
                JgLog.Info("MOD registered: " + info);
                return ctx;
            }
        }

        public static bool TryGetMod(string id, out ModContext context)
        {
            context = null;
            if (string.IsNullOrWhiteSpace(id)) return false;
            lock (RegistryLock)
            {
                return Mods.TryGetValue(id.Trim(), out context);
            }
        }

        public static IReadOnlyList<ModInfo> RegisteredMods
        {
            get
            {
                lock (RegistryLock)
                {
                    var list = new List<ModInfo>(Mods.Count);
                    foreach (ModContext ctx in Mods.Values) list.Add(ctx.Info);
                    return list;
                }
            }
        }

        private static void UnregisterModInternal(string id)
        {
            lock (RegistryLock)
            {
                if (Mods.Remove(id))
                    JgLog.Info("MOD unregistered: " + id);
            }
        }
    }
}
