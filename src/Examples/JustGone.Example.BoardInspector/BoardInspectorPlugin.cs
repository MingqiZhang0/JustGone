using System;
using System.Collections.Generic;
using BepInEx;
using JustGone;
using JustGone.Core;
using UnityEngine;

namespace JustGone.Example.BoardInspector
{
    /// <summary>
    /// Example MOD #1: Board Inspector.
    /// Demonstrates querying board dimensions, logical/GTP/world/screen coordinates, mouse hover/click events,
    /// scene transitions, native markers, and a collapsible native sidebar using ONLY JustGoneApi
    /// (zero references to Assembly-CSharp or Harmony).
    /// </summary>
    [BepInPlugin("example.justgone.boardinspector", "Just Gone Example: Board Inspector", "0.1.0")]
    [BepInDependency(JustGoneApi.FrameworkGuid)]
    public sealed class BoardInspectorPlugin : BaseUnityPlugin
    {
        private ModContext _ctx;
        private ISidebarPanel _sidebar;
        private IMarkerLayer _markers;
        private bool _panelVisible = true;
        private BoardCoord _lastClicked = BoardCoord.Invalid;
        private string _sceneSummary = "MainMenu";
        private readonly List<string> _recentEvents = new List<string>();

        private void Awake()
        {
            _ctx = JustGoneApi.RegisterMod("example.justgone.boardinspector", "Board Inspector", "0.1.0");
            _ctx.Log.Info("Board Inspector loaded (F8 toggles panel in board scenes).");

            JustGoneApi.Events.OnSceneLoaded(_ctx, OnSceneLoaded);
            JustGoneApi.Events.OnGameReady(_ctx, OnGameReady);
            JustGoneApi.Events.OnGameLeaving(_ctx, OnGameLeaving);
            JustGoneApi.Events.OnBoardHoverChanged(_ctx, OnHoverChanged);
            JustGoneApi.Events.OnBoardClicked(_ctx, OnBoardClicked);
            JustGoneApi.Events.OnKeyPressed(_ctx, KeyCode.F8, OnToggleKey);
        }

        private void OnDestroy()
        {
            DisposeSessionUi();
            _ctx?.Dispose();
        }

        private void OnSceneLoaded(SceneLoadedEventArgs e)
        {
            _sceneSummary = e.SceneName + " (" + e.SceneKind + ")";
            _ctx.Log.Info("Scene loaded: " + _sceneSummary);
        }

        private void OnGameReady(GameReadyEventArgs e)
        {
            DisposeSessionUi();
            _lastClicked = BoardCoord.Invalid;
            _recentEvents.Clear();

            string modeLabel = string.IsNullOrEmpty(e.ActiveCustomModeId)
                ? "Vanilla:" + e.VanillaMode
                : "Custom:" + e.ActiveCustomModeId;
            _sceneSummary = e.Environment + " | " + e.BoardSize + "x" + e.BoardSize + " | " + modeLabel;

            _markers = JustGoneApi.Rendering.CreateMarkerLayer(_ctx, "inspector_probe");
            _sidebar = JustGoneApi.UI.CreateSidebarPanel(_ctx, new SidebarPanelOptions
            {
                Name = "BoardInspector",
                Title = "棋盘检查器 (F8)",
                FooterText = "悬停查看格点 · 左键探测坐标",
                MirrorNativeHistoryPanel = true,
                StartExpanded = true,
                StartVisible = _panelVisible
            });

            PushEvent("就绪: " + _sceneSummary);
            RefreshPanel(BoardCoord.Invalid);
            JustGoneApi.UI.ShowToast("棋盘检查器已就绪 · 按 F8 切换面板", 2.5f);
        }

        private void OnGameLeaving(SceneLoadedEventArgs e)
        {
            DisposeSessionUi();
        }

        private void OnHoverChanged(BoardHoverEventArgs e)
        {
            if (_sidebar == null || !_panelVisible) return;
            RefreshPanel(e.Current);
        }

        private void OnBoardClicked(BoardClickEventArgs e)
        {
            if (e.PointerOverUi || e.MouseButton != 0 || !e.Coord.IsValid) return;

            _lastClicked = e.Coord;
            _markers?.Clear();
            _markers?.SetMarker(e.Coord, new Color(0.20f, 0.85f, 1.0f, 1.0f), diameterScale: 0.9f);

            string gtp = e.Coord.ToGtp(e.BoardSize);
            string screenText = JustGoneApi.Board.TryCellToScreen(e.Coord, out Vector2 sp)
                ? Mathf.RoundToInt(sp.x) + "," + Mathf.RoundToInt(sp.y)
                : "n/a";
            PushEvent("点击 " + e.Coord + " / " + gtp + " @px(" + screenText + ")");
            RefreshPanel(JustGoneApi.Board.HoveredCell);
        }

        private void OnToggleKey(KeyPressEventArgs e)
        {
            if (e.IsTextInputFocused || _sidebar == null) return;
            _panelVisible = !_panelVisible;
            _sidebar.SetVisible(_panelVisible);
            if (!_panelVisible) _markers?.Clear();
        }

        private void PushEvent(string line)
        {
            _recentEvents.Insert(0, line);
            while (_recentEvents.Count > 6) _recentEvents.RemoveAt(_recentEvents.Count - 1);
        }

        private void RefreshPanel(BoardCoord hover)
        {
            if (_sidebar == null) return;
            int size = JustGoneApi.Board.BoardSize;
            string hoverText = hover.InBounds(size)
                ? "悬停: " + hover + " (" + hover.ToGtp(size) + ")"
                : "悬停: 棋盘外";

            var items = new List<SidebarItem>
            {
                new SidebarItem { Id = "scene", Text = _sceneSummary, PreferredHeight = 52f, Interactable = false },
                new SidebarItem { Id = "hover", Text = hoverText, PreferredHeight = 46f, Interactable = false },
                new SidebarItem
                {
                    Id = "clear",
                    Text = _lastClicked.IsValid
                        ? "清除探测标记 " + _lastClicked.ToGtp(size)
                        : "探测标记: 无",
                    PreferredHeight = 46f,
                    Interactable = _lastClicked.IsValid,
                    OnClick = () =>
                    {
                        _lastClicked = BoardCoord.Invalid;
                        _markers?.Clear();
                        RefreshPanel(JustGoneApi.Board.HoveredCell);
                    }
                }
            };

            for (int i = 0; i < _recentEvents.Count; i++)
            {
                items.Add(new SidebarItem
                {
                    Id = "log_" + i,
                    Text = _recentEvents[i],
                    PreferredHeight = 44f,
                    Interactable = false
                });
            }

            _sidebar.SetItems(items);
        }

        private void DisposeSessionUi()
        {
            _markers?.Dispose();
            _markers = null;
            _sidebar?.Dispose();
            _sidebar = null;
        }
    }
}
