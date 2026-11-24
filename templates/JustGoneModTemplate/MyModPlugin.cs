using BepInEx;
using JustGone;
using JustGone.Core;
using UnityEngine;

namespace MyJustGoneMod
{
    /// <summary>
    /// Minimal standalone Just Gone MOD template.
    /// Zero references to Assembly-CSharp or 0Harmony.
    /// </summary>
    [BepInPlugin("author.justgone.template", "My Just Gone Mod", "0.1.0")]
    [BepInDependency(JustGoneApi.FrameworkGuid)]
    public sealed class MyModPlugin : BaseUnityPlugin
    {
        private ModContext _ctx;
        private IMarkerLayer _markers;

        private void Awake()
        {
            _ctx = JustGoneApi.RegisterMod("author.justgone.template", "My Just Gone Mod", "0.1.0");
            _ctx.Log.Info("Template MOD initialized.");

            JustGoneApi.Events.OnGameReady(_ctx, OnGameReady);
            JustGoneApi.Events.OnBoardClicked(_ctx, OnBoardClicked);
            JustGoneApi.Events.OnGameLeaving(_ctx, _ => CleanupBoardResources());
        }

        private void OnGameReady(GameReadyEventArgs e)
        {
            CleanupBoardResources();
            _markers = JustGoneApi.Rendering.CreateMarkerLayer(_ctx, "template_markers");
            JustGoneApi.UI.ShowToast("My Just Gone Mod ready (" + e.BoardSize + "x" + e.BoardSize + ")", 2.5f);
        }

        private void OnBoardClicked(BoardClickEventArgs e)
        {
            if (e.PointerOverUi || e.MouseButton != 0 || !e.Coord.IsValid) return;
            _markers?.Clear();
            _markers?.SetMarker(e.Coord, Color.cyan, diameterScale: 0.85f);
            JustGoneApi.UI.ShowToast("Clicked " + e.Coord.ToGtp(e.BoardSize) + " " + e.Coord, 1.8f);
        }

        private void CleanupBoardResources()
        {
            _markers?.Dispose();
            _markers = null;
        }

        private void OnDestroy()
        {
            CleanupBoardResources();
            _ctx?.Dispose();
        }
    }
}
