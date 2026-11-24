using System;
using System.Collections.Generic;
using BepInEx;
using JustGone;
using JustGone.Core;
using UnityEngine;

namespace JustGone.Example.CustomModeAndUi
{
    /// <summary>
    /// Example MOD #2: Custom Game Mode + Native UI Extension ("自由标记沙盒").
    /// Demonstrates:
    ///   - Registering a new mode directly into Just Go's native Local Play selector
    ///   - Building a native BasicPanel configuration page (choices, text input, status, bottom action)
    ///   - Taking over a board scene safely with SaveGuard protection and vanilla input suppression
    ///   - Spawning native black/white/tinted labeled stones via IPieceLayer, star-point markers via IMarkerLayer,
    ///     a collapsible native sidebar, and proxying native Practice Undo/Restart/Save actions
    /// Uses ONLY JustGoneApi (zero references to Assembly-CSharp or Harmony).
    /// </summary>
    [BepInPlugin("example.justgone.customsandbox", "Just Gone Example: Custom Mode & UI", "0.1.0")]
    [BepInDependency(JustGoneApi.FrameworkGuid)]
    public sealed class CustomModeUiPlugin : BaseUnityPlugin
    {
        private const string ModeId = "example.sandbox.marker";

        private ModContext _ctx;
        private IGameModeRegistration _modeReg;

        // Menu-configurable options
        private int _sizeChoiceIndex; // 0=19, 1=13, 2=9
        private int _envChoiceIndex = 3; // 3=ChineseGarden
        private string _customStampGlyph = "\u661f"; // 星

        private static readonly BoardSizeKind[] Sizes = { BoardSizeKind.Nineteen, BoardSizeKind.Thirteen, BoardSizeKind.Nine };
        private static readonly string[] SizeLabels = { "19 路标准棋盘", "13 路中盘棋盘", "9 路快速棋盘" };
        private static readonly string[] EnvLabels = { "解说室", "日式榻榻米", "对局室", "中式庭院" };

        private void Awake()
        {
            _ctx = JustGoneApi.RegisterMod("example.justgone.customsandbox", "Custom Mode & UI Example", "0.1.0");
            RegisterSandboxMode();
            _ctx.Log.Info("Custom Mode & UI Example registered.");
        }

        private void OnDestroy()
        {
            _ctx?.Dispose();
        }

        private void RegisterSandboxMode()
        {
            var definition = new GameModeDefinition
            {
                ModeId = ModeId,
                DisplayTitle = "自由标记沙盒 (JG)",
                TargetEnvironment = (GameEnvironmentKind)_envChoiceIndex,
                BoardSize = Sizes[_sizeChoiceIndex],
                SuppressVanillaInput = true,
                ClearVanillaStonesOnStart = true,
                ValidateLaunch = () =>
                {
                    if (_customStampGlyph != null && _customStampGlyph.Length > 2)
                        return MenuLaunchValidation.Fail("自定义标记文字最多 2 个字符。");
                    return MenuLaunchValidation.Ok();
                },
                BuildMenuConfigPage = BuildConfigPage,
                CreateSession = (owner, sessionCtx) => new SandboxSession(owner, _customStampGlyph)
            };

            _modeReg = JustGoneApi.Modes.RegisterGameMode(_ctx, definition);
            SyncDefinition(definition);
        }

        private GameModeDefinition _liveDefinition;

        private void SyncDefinition(GameModeDefinition def)
        {
            _liveDefinition = def;
            _liveDefinition.BoardSize = Sizes[_sizeChoiceIndex];
            _liveDefinition.TargetEnvironment = (GameEnvironmentKind)_envChoiceIndex;
        }

        private void BuildConfigPage(IMenuPageBuilder page)
        {
            page.AddChoiceRow("沙盒路数", SizeLabels, _sizeChoiceIndex, idx =>
            {
                _sizeChoiceIndex = idx;
                if (_liveDefinition != null) _liveDefinition.BoardSize = Sizes[idx];
                page.RequestRebuild(false);
            });

            page.AddChoiceRow("对局场景", EnvLabels, _envChoiceIndex, idx =>
            {
                _envChoiceIndex = idx;
                if (_liveDefinition != null) _liveDefinition.TargetEnvironment = (GameEnvironmentKind)idx;
                page.RequestRebuild(false);
            });

            page.AddInputRow("标记文字", _customStampGlyph, text =>
            {
                _customStampGlyph = (text ?? string.Empty).Trim();
            }, placeholder: "输入1-2个字符（如 星）");

            page.AddStatusRow("当前配置", SizeLabels[_sizeChoiceIndex] + " · " + EnvLabels[_envChoiceIndex]);

            page.AddBottomAction("恢复默认", () =>
            {
                _sizeChoiceIndex = 0;
                _envChoiceIndex = 3;
                _customStampGlyph = "\u661f";
                if (_liveDefinition != null) SyncDefinition(_liveDefinition);
                page.ShowTransientNotice("已恢复默认沙盒配置。", 4f);
                page.RequestRebuild(true);
            });
        }

        // ------------------------------------------------------------------
        // Playable Custom Mode Session
        // ------------------------------------------------------------------

        private sealed class SandboxSession : IGameModeSession
        {
            private enum BrushKind { BlackStone, WhiteStone, CrimsonStamp, Eraser }

            private sealed class PlacedEntry
            {
                public int PieceId;
                public BoardCoord Coord;
                public BrushKind Brush;
                public string Label;
            }

            private readonly ModContext _owner;
            private readonly string _stampGlyph;
            private readonly List<PlacedEntry> _history = new List<PlacedEntry>();
            private readonly Dictionary<int, PlacedEntry> _byCell = new Dictionary<int, PlacedEntry>();

            private IPieceLayer _pieces;
            private IMarkerLayer _markers;
            private ISidebarPanel _sidebar;
            private BrushKind _brush = BrushKind.BlackStone;
            private int _nextPieceId = 1;
            private int _boardSize = 19;

            public SandboxSession(ModContext owner, string stampGlyph)
            {
                _owner = owner;
                _stampGlyph = string.IsNullOrEmpty(stampGlyph) ? "\u661f" : stampGlyph;
            }

            public void OnStart(ModeSessionContext ctx)
            {
                _boardSize = ctx.BoardSize;
                _pieces = ctx.Track(JustGoneApi.Pieces.CreateLayer(_owner, "sandbox_pieces", naturalJitter: true));
                _markers = ctx.Track(JustGoneApi.Rendering.CreateMarkerLayer(_owner, "sandbox_markers"));

                _sidebar = ctx.Track(JustGoneApi.UI.CreateSidebarPanel(_owner, new SidebarPanelOptions
                {
                    Name = "SandboxTools",
                    Title = "沙盒画笔",
                    FooterText = "左键落子/擦除 · 原生按钮可悔棋/重开/导出",
                    MirrorNativeHistoryPanel = true,
                    StartExpanded = true,
                    StartVisible = true
                }));

                ctx.Track(JustGoneApi.Events.OnBoardClicked(_owner, OnBoardClicked));
                ctx.Track(JustGoneApi.Events.OnBoardHoverChanged(_owner, OnHoverChanged));

                MarkStarPoints();
                RebuildSidebar();
                RefreshNativeUi();
                JustGoneApi.UI.ShowToast("自由标记沙盒已启动 (" + _boardSize + "x" + _boardSize + ")", 3f);
            }

            public void OnUpdate(ModeSessionContext ctx, float deltaTime)
            {
                // All interaction is event-driven via JustGoneApi.Events; no polling needed.
            }

            public void OnEnd(ModeSessionContext ctx)
            {
                JustGoneApi.Rendering.HideHoverGhost();
            }

            public bool OnNativeAction(ModeSessionContext ctx, NativeGameAction action)
            {
                switch (action)
                {
                    case NativeGameAction.Undo:
                        UndoLast();
                        return true;
                    case NativeGameAction.Restart:
                        ResetBoard();
                        return true;
                    case NativeGameAction.Save:
                        ExportJson();
                        return true;
                    default:
                        return false;
                }
            }

            private void OnHoverChanged(BoardHoverEventArgs e)
            {
                if (_brush == BrushKind.Eraser || !e.Current.InBounds(_boardSize) || _byCell.ContainsKey(e.Current.ToKey(_boardSize)))
                {
                    JustGoneApi.Rendering.HideHoverGhost();
                    return;
                }
                StoneBaseStyle ghost = _brush == BrushKind.BlackStone ? StoneBaseStyle.Black : StoneBaseStyle.White;
                JustGoneApi.Rendering.ShowHoverGhost(ghost, e.Current);
            }

            private void OnBoardClicked(BoardClickEventArgs e)
            {
                if (e.PointerOverUi || e.MouseButton != 0 || !e.Coord.InBounds(_boardSize)) return;

                int key = e.Coord.ToKey(_boardSize);
                if (_brush == BrushKind.Eraser)
                {
                    if (_byCell.TryGetValue(key, out PlacedEntry existing))
                    {
                        _pieces.Remove(existing.PieceId);
                        _byCell.Remove(key);
                        _history.Remove(existing);
                        RefreshNativeUi();
                        JustGoneApi.UI.ShowToast("已移除 " + e.Coord.ToGtp(_boardSize), 1.8f);
                    }
                    return;
                }

                if (_byCell.ContainsKey(key))
                {
                    JustGoneApi.UI.ShowToast("该交叉点已有棋子（可切换橡皮擦移除）", 1.8f);
                    return;
                }

                int id = _nextPieceId++;
                PieceVisualSpec spec = BuildSpec(_brush, _stampGlyph);
                if (_pieces.Spawn(id, e.Coord, spec, animateDrop: true))
                {
                    _pieces.PlayDropSound();
                    var entry = new PlacedEntry { PieceId = id, Coord = e.Coord, Brush = _brush, Label = spec.LabelText };
                    _byCell[key] = entry;
                    _history.Add(entry);
                    JustGoneApi.Rendering.HideHoverGhost();
                    RefreshNativeUi();
                }
            }

            private static PieceVisualSpec BuildSpec(BrushKind brush, string stampGlyph)
            {
                switch (brush)
                {
                    case BrushKind.BlackStone:
                        return new PieceVisualSpec { BaseStyle = StoneBaseStyle.Black, Scale = 0.92f };
                    case BrushKind.WhiteStone:
                        return new PieceVisualSpec { BaseStyle = StoneBaseStyle.White, Scale = 0.92f };
                    default:
                        return new PieceVisualSpec
                        {
                            BaseStyle = StoneBaseStyle.White,
                            Tint = new Color(0.75f, 0.14f, 0.12f, 1f),
                            LabelText = stampGlyph,
                            LabelColor = Color.white,
                            Scale = 1.0f,
                            UseCjkFont = true
                        };
                }
            }

            private void UndoLast()
            {
                if (_history.Count == 0)
                {
                    JustGoneApi.UI.ShowToast("没有可撤销的棋子", 1.8f);
                    return;
                }
                PlacedEntry last = _history[_history.Count - 1];
                _history.RemoveAt(_history.Count - 1);
                _byCell.Remove(last.Coord.ToKey(_boardSize));
                _pieces.Remove(last.PieceId);
                RefreshNativeUi();
                JustGoneApi.UI.ShowToast("已撤销 " + last.Coord.ToGtp(_boardSize), 1.8f);
            }

            private void ResetBoard()
            {
                _pieces.Clear();
                _history.Clear();
                _byCell.Clear();
                RefreshNativeUi();
                JustGoneApi.UI.ShowToast("沙盒棋盘已清空", 2f);
            }

            private void ExportJson()
            {
                var items = new List<object>();
                for (int i = 0; i < _history.Count; i++)
                {
                    PlacedEntry e = _history[i];
                    items.Add(new Dictionary<string, object>
                    {
                        ["step"] = i + 1,
                        ["x"] = e.Coord.X,
                        ["y"] = e.Coord.Y,
                        ["gtp"] = e.Coord.ToGtp(_boardSize),
                        ["brush"] = e.Brush.ToString(),
                        ["label"] = e.Label ?? string.Empty
                    });
                }
                string json = MiniJson.Serialize(new Dictionary<string, object>
                {
                    ["mode"] = ModeId,
                    ["boardSize"] = _boardSize,
                    ["count"] = _history.Count,
                    ["entries"] = items
                });

                JustGoneApi.UI.ShowSaveFileDialog(
                    "justgone_sandbox_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"),
                    ".json",
                    json,
                    res =>
                    {
                        if (res.Saved)
                            JustGoneApi.UI.ShowToast("沙盒记录已保存: " + System.IO.Path.GetFileName(res.FilePath), 3f);
                        else if (!res.Cancelled)
                            JustGoneApi.UI.ShowStickyError("保存失败: " + res.ErrorMessage);
                    },
                    JustGoneApi.Resources.GetModConfigDirectory(_owner));
            }

            private void MarkStarPoints()
            {
                _markers.Clear();
                int[] coords = _boardSize == 19 ? new[] { 3, 9, 15 }
                    : (_boardSize == 13 ? new[] { 3, 6, 9 } : new[] { 2, 4, 6 });
                var starColor = new Color(1.0f, 0.85f, 0.20f, 1.0f);
                for (int i = 0; i < coords.Length; i++)
                    for (int j = 0; j < coords.Length; j++)
                        _markers.SetMarker(new BoardCoord(coords[i], coords[j]), starColor, diameterScale: 0.45f);
            }

            private void RebuildSidebar()
            {
                var items = new List<SidebarItem>
                {
                    new SidebarItem { Id = "black", Text = "黑子画笔", OnClick = () => SelectBrush(BrushKind.BlackStone, "black") },
                    new SidebarItem { Id = "white", Text = "白子画笔", OnClick = () => SelectBrush(BrushKind.WhiteStone, "white") },
                    new SidebarItem { Id = "stamp", Text = "朱红标记子 [" + _stampGlyph + "]", OnClick = () => SelectBrush(BrushKind.CrimsonStamp, "stamp") },
                    new SidebarItem { Id = "eraser", Text = "橡皮擦", OnClick = () => SelectBrush(BrushKind.Eraser, "eraser") },
                    new SidebarItem { Id = "undo", Text = "撤销上一步", OnClick = UndoLast },
                    new SidebarItem { Id = "clear", Text = "清空棋盘", OnClick = ResetBoard }
                };
                _sidebar.SetItems(items);
                _sidebar.SetSelectedItem("black");
            }

            private void SelectBrush(BrushKind brush, string id)
            {
                _brush = brush;
                _sidebar.SetSelectedItem(id);
                if (brush == BrushKind.Eraser) JustGoneApi.Rendering.HideHoverGhost();
            }

            private void RefreshNativeUi()
            {
                JustGoneApi.UI.SetPracticeButtonStates(
                    undoInteractable: _history.Count > 0,
                    saveInteractable: _history.Count > 0,
                    restartInteractable: true);

                var list = new List<HistoryListItem>(_history.Count);
                for (int i = 0; i < _history.Count; i++)
                {
                    PlacedEntry e = _history[i];
                    list.Add(new HistoryListItem
                    {
                        IndexLabel = (i + 1) + ".",
                        SummaryText = e.Brush + " " + e.Coord.ToGtp(_boardSize)
                    });
                }
                JustGoneApi.UI.SetPracticeHistory(list, idx =>
                {
                    if (idx >= 0 && idx < _history.Count)
                        _pieces.SetSelected(_history[idx].PieceId);
                });
            }
        }
    }
}
