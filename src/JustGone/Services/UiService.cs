using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BepInEx;
using Crosstales.FB;
using JustGone.Internal;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace JustGone.Services
{
    /// <summary>
    /// Native UI service providing:
    ///   - Toast / sticky error feedback (extracted from WxqNativeFeedback.cs)
    ///   - Mirror-aligned collapsible ScrollRect sidebar panel (extracted from WxqRecruitRegionPanel.cs)
    ///   - Practice button state overrides, history list, GameOverPanel adapter, and FileBrowser save dialog
    ///     (extracted from WxqNativeUiAdapter.cs)
    /// </summary>
    internal sealed class UiService : IUiService
    {
        private readonly BoardService _board;
        private readonly ResourceService _resources;

        private NativeToastController _toast;
        private bool? _undoOverride;
        private bool? _saveOverride;
        private bool? _restartOverride;
        private TMP_Text _buttonLabelTemplate;

        internal bool HasPracticeButtonOverrides =>
            _undoOverride.HasValue || _saveOverride.HasValue || _restartOverride.HasValue;

        public UiService(BoardService board, ResourceService resources)
        {
            _board = board;
            _resources = resources;
        }

        public void ShowToast(string message, float durationSeconds = 2.8f)
        {
            EnsureToast()?.Show(message, false, durationSeconds);
        }

        public void ShowStickyError(string message)
        {
            EnsureToast()?.Show(message, true, 0f);
        }

        public void ClearStickyError()
        {
            if (_toast != null) _toast.ClearSticky();
        }

        public ISidebarPanel CreateSidebarPanel(ModContext owner, SidebarPanelOptions options)
        {
            GameManager gm = JgGameBridge.TryGetGameManager();
            if (gm == null)
                throw new InvalidOperationException("Cannot create sidebar panel: GameManager is not ready.");

            options = options ?? new SidebarPanelOptions();
            var panel = new NativeSidebarPanel(gm, _resources, owner == null ? "anon" : owner.Info.Id, options);
            return owner != null ? owner.Track(panel) : panel;
        }

        public void SetPracticeButtonStates(bool? undoInteractable, bool? saveInteractable, bool? restartInteractable)
        {
            _undoOverride = undoInteractable;
            _saveOverride = saveInteractable;
            _restartOverride = restartInteractable;
            ApplyPracticeButtonOverrides(JgGameBridge.TryGetGameManager());
        }

        public void ClearPracticeButtonOverrides()
        {
            _undoOverride = null;
            _saveOverride = null;
            _restartOverride = null;
        }

        internal void ApplyPracticeButtonOverrides(GameManager gm)
        {
            if (gm == null || !HasPracticeButtonOverrides) return;
            try
            {
                if (_undoOverride.HasValue && gm.UndoButton != null)
                    gm.UndoButton.interactable = _undoOverride.Value;
                if (_saveOverride.HasValue && gm.SaveButton != null)
                    gm.SaveButton.interactable = _saveOverride.Value;
                if (_restartOverride.HasValue && gm.RestartButton != null)
                    gm.RestartButton.interactable = _restartOverride.Value;
            }
            catch { }
        }

        public void SetPracticeHistory(IReadOnlyList<HistoryListItem> items, Action<int> onItemClicked, int selectedIndex = -1)
        {
            GameManager gm = JgGameBridge.TryGetGameManager();
            if (gm == null || gm.PracticeMovePanel == null || gm.PracticeMovePrefab == null) return;

            try
            {
                if (gm.PracticePanel != null && !gm.PracticePanel.activeSelf) gm.PracticePanel.SetActive(true);
                if (!gm.PracticeMovePanel.activeSelf) gm.PracticeMovePanel.SetActive(true);

                Transform panel = gm.PracticeMovePanel.transform;
                for (int i = panel.childCount - 1; i >= 1; i--)
                    UnityEngine.Object.Destroy(panel.GetChild(i).gameObject);

                if (items != null)
                {
                    TMP_FontAsset cjk = _resources.GetCjkFontInternal();
                    for (int i = 0; i < items.Count; i++)
                    {
                        HistoryListItem item = items[i];
                        GameObject go = UnityEngine.Object.Instantiate(gm.PracticeMovePrefab, panel, false);
                        PracticeMove pm = go.GetComponent<PracticeMove>();
                        if (pm == null) { UnityEngine.Object.Destroy(go); continue; }

                        if (pm.RankText != null)
                        {
                            pm.RankText.text = string.IsNullOrEmpty(item.IndexLabel) ? (i + 1) + "." : item.IndexLabel;
                            pm.RankText.color = (i == selectedIndex) ? new Color(0.95f, 0.82f, 0.55f) : Color.white;
                        }
                        if (pm.InfoText != null)
                        {
                            if (cjk != null) pm.InfoText.font = cjk;
                            pm.InfoText.fontSize = 14f;
                            pm.InfoText.enableWordWrapping = false;
                            pm.InfoText.overflowMode = TextOverflowModes.Ellipsis;
                            pm.InfoText.text = item.SummaryText ?? string.Empty;
                        }
                        if (pm.PieceImage != null) pm.PieceImage.gameObject.SetActive(false);
                        if (pm.Button != null)
                        {
                            pm.Button.interactable = true;
                            int captured = i;
                            pm.Button.onClick = new Button.ButtonClickedEvent();
                            if (onItemClicked != null)
                                pm.Button.onClick.AddListener(() => onItemClicked(captured));
                        }
                    }
                }

                if (gm.PracticeScrollRect != null)
                {
                    Canvas.ForceUpdateCanvases();
                    gm.PracticeScrollRect.verticalNormalizedPosition = 0f;
                }
            }
            catch (Exception ex)
            {
                JgLog.Warn("SetPracticeHistory failed: " + ex.Message);
            }
        }

        public void ShowGameOver(GameOverViewOptions options)
        {
            GameManager gm = JgGameBridge.TryGetGameManager();
            if (gm == null || options == null) return;

            try
            {
                JgGameBridge.SetGameOverFlag(gm, true);
                if (gm.GameOverPanel != null)
                {
                    gm.GameOverPanel.SetActive(true);
                    if (gm.GameOverPanel.transform is RectTransform prt)
                    {
                        prt.anchorMin = Vector2.zero;
                        prt.anchorMax = Vector2.one;
                        prt.pivot = new Vector2(0.5f, 0.5f);
                        prt.offsetMin = Vector2.zero;
                        prt.offsetMax = Vector2.zero;
                    }
                }
                if (gm.GameOverSubPanel != null) gm.GameOverSubPanel.SetActive(true);
                if (gm.XPProgressBar != null) gm.XPProgressBar.gameObject.SetActive(false);
                if (gm.RankScoreProgressBar != null) gm.RankScoreProgressBar.gameObject.SetActive(false);
                if (gm.ScoreTextWhenResigned != null) gm.ScoreTextWhenResigned.gameObject.SetActive(false);

                JgGameBridge.TrySetCamera(gm, Core.VirtualCameraKind.GameOver, 1.5f);

                var panel = gm.GameOverPanel == null ? null : gm.GameOverPanel.transform as RectTransform;
                if (panel == null) return;

                var layout = panel.Find("JG_ResultLayout") as RectTransform;
                if (layout == null)
                {
                    var go = new GameObject("JG_ResultLayout", typeof(RectTransform), typeof(BoardCenterResultLayout));
                    layout = (RectTransform)go.transform;
                    layout.SetParent(panel, false);
                    layout.anchorMin = layout.anchorMax = layout.pivot = new Vector2(0.5f, 0.5f);
                    layout.sizeDelta = new Vector2(900f, 350f);
                }

                layout.SetAsLastSibling();
                layout.GetComponent<BoardCenterResultLayout>().Bind(_board, panel);
                _buttonLabelTemplate = gm.ResultText != null ? gm.ResultText : gm.WarningMessage;

                TMP_FontAsset cjk = _resources.GetCjkFontInternal();
                SetResultLine(layout, gm.ResultText, cjk, "Title", options.Title ?? string.Empty, 55f, 110f, 80f);
                SetResultLine(layout, gm.ScoreText, cjk, "Subtitle", options.Subtitle ?? string.Empty, 25f, 28f, 60f);
                SetResultLine(layout, gm.ScoreText, cjk, "Detail", options.Detail ?? string.Empty, 22f, -42f, 65f);
                if (gm.ResultText != null) gm.ResultText.gameObject.SetActive(false);
                if (gm.ScoreText != null) gm.ScoreText.gameObject.SetActive(false);

                WireGameOverButtons(gm, layout, options, cjk);
            }
            catch (Exception ex)
            {
                JgLog.Warn("ShowGameOver failed: " + ex.Message);
            }
        }

        public void HideGameOver()
        {
            GameManager gm = JgGameBridge.TryGetGameManager();
            if (gm == null) return;
            try
            {
                if (gm.GameOverPanel != null) gm.GameOverPanel.SetActive(false);
                if (gm.ResultText != null) gm.ResultText.gameObject.SetActive(true);
                if (gm.ScoreText != null) gm.ScoreText.gameObject.SetActive(true);
                if (gm.ScoreTextWhenResigned != null) gm.ScoreTextWhenResigned.gameObject.SetActive(true);
                JgGameBridge.SetGameOverFlag(gm, false);
            }
            catch (Exception ex)
            {
                JgLog.Warn("HideGameOver failed: " + ex.Message);
            }
        }

        public void ShowSaveFileDialog(
            string defaultFileName,
            string extension,
            string content,
            Action<SaveDialogResult> onCompleted,
            string fallbackDirectory = null)
        {
            string ext = string.IsNullOrWhiteSpace(extension) ? ".txt" : (extension.StartsWith(".") ? extension : "." + extension);
            string baseName = string.IsNullOrWhiteSpace(defaultFileName)
                ? "justgone_" + DateTime.Now.ToString("yyyyMMdd_HHmmss")
                : defaultFileName;

            try
            {
                FileBrowser.Instance.SaveFileAsync(path =>
                {
                    if (string.IsNullOrEmpty(path))
                    {
                        onCompleted?.Invoke(new SaveDialogResult { Cancelled = true });
                        return;
                    }
                    try
                    {
                        if (!path.EndsWith(ext, StringComparison.OrdinalIgnoreCase)) path += ext;
                        File.WriteAllText(path, content ?? string.Empty, new UTF8Encoding(false));
                        onCompleted?.Invoke(new SaveDialogResult { Saved = true, FilePath = path });
                    }
                    catch (Exception ex)
                    {
                        onCompleted?.Invoke(new SaveDialogResult { ErrorMessage = ex.Message });
                    }
                }, baseName, ext);
            }
            catch (Exception ex)
            {
                JgLog.Warn("Native FileBrowser unavailable (" + ex.Message + "); using fallback path.");
                try
                {
                    string folder = string.IsNullOrWhiteSpace(fallbackDirectory)
                        ? Path.Combine(Paths.ConfigPath, JustGoneApi.FrameworkGuid, "exports")
                        : fallbackDirectory;
                    Directory.CreateDirectory(folder);
                    string path = Path.Combine(folder, baseName + ext);
                    File.WriteAllText(path, content ?? string.Empty, new UTF8Encoding(false));
                    onCompleted?.Invoke(new SaveDialogResult { Saved = true, UsedFallback = true, FilePath = path });
                }
                catch (Exception ex2)
                {
                    onCompleted?.Invoke(new SaveDialogResult { ErrorMessage = ex2.Message });
                }
            }
        }

        internal void OnSceneChanged()
        {
            _toast = null;
            ClearPracticeButtonOverrides();
        }

        private NativeToastController EnsureToast()
        {
            if (_toast != null && _toast.gameObject != null) return _toast;
            GameManager gm = JgGameBridge.TryGetGameManager();
            if (gm == null) return null;

            TMP_Text source = gm.WarningMessage != null ? gm.WarningMessage : gm.ResultText;
            if (source == null) return null;
            Canvas canvas = source.GetComponentInParent<Canvas>(true);
            if (canvas == null)
            {
                GameObject canvasGo = GameObject.Find("Canvas");
                if (canvasGo != null) canvas = canvasGo.GetComponent<Canvas>();
            }
            if (canvas == null) return null;

            var host = new GameObject("JG_NativeToastHost");
            host.transform.SetParent(canvas.transform, false);
            _toast = host.AddComponent<NativeToastController>();
            _toast.Build(source, canvas, _resources.GetCjkFontInternal());
            return _toast;
        }

        private static void SetResultLine(
            RectTransform layout,
            TMP_Text template,
            TMP_FontAsset cjk,
            string name,
            string text,
            float fontSize,
            float y,
            float height)
        {
            if (template == null) return;
            Transform existing = layout.Find(name);
            TMP_Text label = existing == null
                ? UnityEngine.Object.Instantiate(template, layout, false)
                : existing.GetComponent<TMP_Text>();
            label.name = name;
            label.gameObject.SetActive(true);
            if (cjk != null) label.font = cjk;
            label.text = text;
            label.alignment = TextAlignmentOptions.Center;
            label.fontSize = fontSize;
            label.enableWordWrapping = true;
            label.raycastTarget = false;
            RectTransform rt = label.rectTransform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.localScale = Vector3.one;
            rt.anchoredPosition = new Vector2(0f, y);
            rt.sizeDelta = new Vector2(900f, height);
        }

        private void WireGameOverButtons(GameManager gm, Transform layer, GameOverViewOptions options, TMP_FontAsset cjk)
        {
            Button menu = null;
            Button[] existing = gm.GameOverPanel.GetComponentsInChildren<Button>(true);
            for (int i = 0; i < existing.Length; i++)
            {
                if (existing[i] != null && existing[i].name == "MenuButton") { menu = existing[i]; break; }
            }
            if (menu != null)
            {
                menu.gameObject.SetActive(true);
                if (menu.transform is RectTransform mrt)
                {
                    mrt.anchorMin = new Vector2(1f, 0f);
                    mrt.anchorMax = new Vector2(1f, 0f);
                    mrt.pivot = new Vector2(1f, 0f);
                    mrt.anchoredPosition = new Vector2(-60f, 40f);
                }
                menu.onClick = new Button.ButtonClickedEvent();
                Action backAction = options.OnReturnToMenu ?? (() => JustGoneApi.Game.ReturnToMainMenu());
                menu.onClick.AddListener(() => backAction());
            }

            for (int i = layer.childCount - 1; i >= 0; i--)
            {
                Transform child = layer.GetChild(i);
                if (child.name.StartsWith("JG_GO_Btn_", StringComparison.Ordinal))
                    UnityEngine.Object.Destroy(child.gameObject);
            }

            if (options.ExtraButtons == null || options.ExtraButtons.Count == 0) return;
            Button source = gm.RestartButton != null ? gm.RestartButton : menu;
            if (source == null) return;

            int count = options.ExtraButtons.Count;
            float spacing = 250f;
            float startX = -((count - 1) * spacing) * 0.5f;
            for (int i = 0; i < count; i++)
            {
                GameOverButtonOption opt = options.ExtraButtons[i];
                GameObject clone = UnityEngine.Object.Instantiate(source.gameObject, layer, false);
                clone.name = "JG_GO_Btn_" + i;
                Button b = clone.GetComponent<Button>();
                if (b == null) { UnityEngine.Object.Destroy(clone); continue; }
                if (clone.transform is RectTransform rt)
                {
                    rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
                    rt.anchoredPosition = new Vector2(startX + i * spacing, -125f);
                    rt.sizeDelta = new Vector2(220f, 56f);
                    rt.localScale = Vector3.one;
                }
                b.interactable = true;
                if (b.targetGraphic != null) b.targetGraphic.raycastTarget = true;
                b.onClick = new Button.ButtonClickedEvent();
                if (opt.OnClick != null) b.onClick.AddListener(() => opt.OnClick());
                ApplyButtonLabel(b, opt.Label ?? string.Empty, cjk);
                clone.SetActive(true);
            }
        }

        private void ApplyButtonLabel(Button b, string text, TMP_FontAsset cjk)
        {
            TMP_Text[] old = b.GetComponentsInChildren<TMP_Text>(true);
            for (int i = 0; i < old.Length; i++)
                if (old[i] != null) old[i].gameObject.SetActive(false);

            if (_buttonLabelTemplate == null) return;
            GameObject go = UnityEngine.Object.Instantiate(_buttonLabelTemplate.gameObject, b.transform, false);
            go.name = "JG_Label";
            TMP_Text label = go.GetComponent<TMP_Text>();
            if (label == null) return;
            if (cjk != null) label.font = cjk;
            label.gameObject.SetActive(true);
            label.text = text;
            label.alignment = TextAlignmentOptions.Center;
            label.fontSize = 22f;
            label.color = new Color(0.16f, 0.11f, 0.08f, 1f);
            label.enableWordWrapping = false;
            label.raycastTarget = false;
            RectTransform tr = label.rectTransform;
            tr.anchorMin = Vector2.zero;
            tr.anchorMax = Vector2.one;
            tr.offsetMin = new Vector2(8f, 4f);
            tr.offsetMax = new Vector2(-8f, -4f);
            tr.localScale = Vector3.one;
            label.transform.SetAsLastSibling();
        }

        private sealed class NativeToastController : MonoBehaviour
        {
            private GameObject _root;
            private TMP_Text _label;
            private CanvasGroup _group;
            private bool _sticky;
            private float _hideAt;
            private string _lastText = string.Empty;
            private float _lastShownAt = -10f;

            internal void Build(TMP_Text source, Canvas canvas, TMP_FontAsset cjk)
            {
                GameObject clone = UnityEngine.Object.Instantiate(source.gameObject, canvas.transform, false);
                clone.name = "JG_NativeToast";
                clone.SetActive(true);
                _root = clone;
                _label = clone.GetComponent<TMP_Text>() ?? clone.GetComponentInChildren<TMP_Text>(true);
                if (_label == null)
                {
                    UnityEngine.Object.Destroy(clone);
                    _root = null;
                    return;
                }

                if (clone.transform is RectTransform rt)
                {
                    rt.anchorMin = new Vector2(0.5f, 1f);
                    rt.anchorMax = new Vector2(0.5f, 1f);
                    rt.pivot = new Vector2(0.5f, 1f);
                    rt.anchoredPosition = new Vector2(0f, -118f);
                    rt.sizeDelta = new Vector2(900f, 80f);
                    rt.localScale = Vector3.one;
                }
                clone.transform.SetAsLastSibling();

                Animator[] animators = clone.GetComponentsInChildren<Animator>(true);
                for (int i = 0; i < animators.Length; i++)
                    if (animators[i] != null) animators[i].enabled = false;
                Graphic[] graphics = clone.GetComponentsInChildren<Graphic>(true);
                for (int i = 0; i < graphics.Length; i++)
                    if (graphics[i] != null) graphics[i].raycastTarget = false;

                _group = clone.GetComponent<CanvasGroup>() ?? clone.AddComponent<CanvasGroup>();
                _group.alpha = 0f;
                _group.interactable = false;
                _group.blocksRaycasts = false;

                if (cjk != null) _label.font = cjk;
                _label.alignment = TextAlignmentOptions.Center;
                _label.enableWordWrapping = true;
                _label.overflowMode = TextOverflowModes.Overflow;
                _label.fontSize = 24f;
                _label.margin = new Vector4(18f, 6f, 18f, 6f);
                _label.text = string.Empty;
                clone.SetActive(false);
            }

            internal void Show(string text, bool sticky, float duration)
            {
                if (_root == null || _label == null || _group == null) return;
                text = (text ?? string.Empty).Trim();
                if (text.Length == 0) return;

                float now = Time.unscaledTime;
                if (!sticky && _sticky) return;
                if (text == _lastText && now - _lastShownAt < 0.6f)
                {
                    _hideAt = now + duration;
                    return;
                }

                _lastText = text;
                _lastShownAt = now;
                _sticky = sticky;
                _label.text = text;
                _label.color = sticky
                    ? new Color(1f, 0.56f, 0.42f, 1f)
                    : new Color(0.96f, 0.91f, 0.76f, 1f);
                _root.SetActive(true);
                _group.alpha = 1f;
                _hideAt = sticky ? float.MaxValue : now + Mathf.Max(1.2f, duration);
            }

            internal void ClearSticky()
            {
                _sticky = false;
                if (_group != null) _group.alpha = 0f;
                if (_root != null) _root.SetActive(false);
            }

            private void Update()
            {
                if (_root == null || _group == null) return;
                if (_group.alpha <= 0.001f || _sticky) return;
                if (Time.unscaledTime < _hideAt) return;
                float a = _group.alpha - Time.unscaledDeltaTime * 3.5f;
                if (a <= 0f)
                {
                    _group.alpha = 0f;
                    _root.SetActive(false);
                }
                else _group.alpha = a;
            }

            private void OnDestroy()
            {
                if (_root != null) UnityEngine.Object.Destroy(_root);
                _root = null;
            }
        }

        private sealed class BoardCenterResultLayout : MonoBehaviour
        {
            private BoardService _board;
            private RectTransform _panel;
            private Canvas _canvas;

            internal void Bind(BoardService board, RectTransform panel)
            {
                _board = board;
                _panel = panel;
                _canvas = panel.GetComponentInParent<Canvas>();
                LateUpdate();
            }

            private void LateUpdate()
            {
                if (_board == null || _panel == null || !_board.TryGetBoardScreenCenter(out Vector2 screen)) return;
                Camera canvasCamera = _canvas == null || _canvas.renderMode == RenderMode.ScreenSpaceOverlay
                    ? null : _canvas.worldCamera;
                if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_panel, screen, canvasCamera, out Vector2 local))
                    return;
                var layout = (RectTransform)transform;
                layout.anchoredPosition = local - _panel.rect.center;
                float scale = Mathf.Min(1f, _panel.rect.width * 0.9f / 900f, _panel.rect.height * 0.8f / 350f);
                if (scale > 0f) layout.localScale = Vector3.one * scale;
            }
        }

        private sealed class NativeSidebarPanel : ISidebarPanel
        {
            private static readonly Color RowColor = new Color(0.22f, 0.16f, 0.11f, 0.72f);
            private static readonly Color SelectedRowColor = new Color(0.57f, 0.40f, 0.22f, 0.85f);

            private readonly RectTransform _root;
            private readonly RectTransform _viewport;
            private readonly RectTransform _list;
            private readonly RectTransform _footerRect;
            private readonly TMP_Text _template;
            private readonly TMP_FontAsset _cjk;
            private readonly TMP_Text _header;
            private readonly TMP_Text _footer;
            private readonly RectTransform _nativeScroll;
            private readonly bool _mirrorHistory;
            private readonly Vector3[] _corners = new Vector3[4];
            private readonly Dictionary<string, Image> _rowImages = new Dictionary<string, Image>(StringComparer.Ordinal);

            private string _baseTitle;
            private bool _expanded;
            private bool _visible;
            private string _selectedId;
            private bool _disposed;

            public bool IsVisible => _visible;
            public bool IsExpanded => _expanded;

            public NativeSidebarPanel(GameManager gm, ResourceService resources, string ownerId, SidebarPanelOptions options)
            {
                Canvas canvas = gm.PracticeMovePanel != null
                    ? gm.PracticeMovePanel.GetComponentInParent<Canvas>()
                    : GameObject.Find("Canvas")?.GetComponent<Canvas>();
                _template = gm.ResultText != null ? gm.ResultText : gm.WarningMessage;
                if (canvas == null || _template == null)
                    throw new InvalidOperationException("Native Canvas or TMP_Text template unavailable for sidebar.");

                _cjk = resources.GetCjkFontInternal();
                _mirrorHistory = options.MirrorNativeHistoryPanel;
                _nativeScroll = gm.PracticeScrollRect == null ? null : gm.PracticeScrollRect.transform as RectTransform;
                _baseTitle = string.IsNullOrEmpty(options.Title) ? "Panel" : options.Title;
                _expanded = options.StartExpanded;
                _visible = options.StartVisible;

                _root = CreateRect("JG_Sidebar_" + ownerId + "_" + (options.Name ?? "panel"), canvas.transform);
                Place(_root, options.AnchorMin, options.AnchorMax);

                _header = AddButton(_root, FormatHeaderTitle(), () => SetExpanded(!_expanded), out Image headerImg);
                var headerRect = (RectTransform)headerImg.transform;
                Place(headerRect, new Vector2(0f, 1f), Vector2.one);
                headerRect.offsetMin = new Vector2(0f, -64f);
                _header.fontSize = 24f;
                _header.alignment = TextAlignmentOptions.Left;
                _header.margin = new Vector4(14f, 0f, 0f, 0f);

                _viewport = CreateRect("Viewport", _root);
                Place(_viewport, Vector2.zero, Vector2.one);
                _viewport.offsetMin = new Vector2(0f, 54f);
                _viewport.offsetMax = new Vector2(0f, -64f);

                Image bg = _viewport.gameObject.AddComponent<Image>();
                bg.color = new Color(0.12f, 0.09f, 0.06f, 0.56f);
                _viewport.gameObject.AddComponent<RectMask2D>();

                _list = CreateRect("Items", _viewport);
                _list.anchorMin = new Vector2(0f, 1f);
                _list.anchorMax = Vector2.one;
                _list.pivot = new Vector2(0.5f, 1f);
                _list.sizeDelta = Vector2.zero;
                _list.anchoredPosition = Vector2.zero;

                VerticalLayoutGroup vlg = _list.gameObject.AddComponent<VerticalLayoutGroup>();
                vlg.padding = new RectOffset(6, 6, 6, 6);
                vlg.spacing = 4f;
                vlg.childForceExpandHeight = false;
                _list.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

                ScrollRect sr = _viewport.gameObject.AddComponent<ScrollRect>();
                sr.horizontal = false;
                sr.content = _list;
                sr.viewport = _viewport;
                if (gm.PracticeScrollRect != null)
                {
                    sr.scrollSensitivity = gm.PracticeScrollRect.scrollSensitivity;
                    sr.movementType = gm.PracticeScrollRect.movementType;
                    sr.inertia = gm.PracticeScrollRect.inertia;
                }

                _footer = AddButton(_root, options.FooterText ?? string.Empty, null, out Image footerImg);
                footerImg.GetComponent<Button>().interactable = false;
                footerImg.raycastTarget = false;
                _footer.fontSize = 16f;
                _footer.enableWordWrapping = true;
                _footerRect = (RectTransform)footerImg.transform;
                Place(_footerRect, Vector2.zero, new Vector2(1f, 0f));
                _footerRect.offsetMax = new Vector2(0f, 52f);

                AlignIfMirrored();
                SetExpanded(_expanded);
                SetVisible(_visible);
            }

            public void SetVisible(bool visible)
            {
                if (_disposed || _root == null) return;
                _visible = visible;
                _root.gameObject.SetActive(visible);
                if (visible) AlignIfMirrored();
            }

            public void SetExpanded(bool expanded)
            {
                if (_disposed) return;
                _expanded = expanded;
                if (_viewport != null) _viewport.gameObject.SetActive(expanded);
                if (_footerRect != null) _footerRect.gameObject.SetActive(expanded);
                if (_header != null) _header.text = FormatHeaderTitle();
            }

            public void SetTitle(string title)
            {
                _baseTitle = title ?? string.Empty;
                if (_header != null) _header.text = FormatHeaderTitle();
            }

            public void SetFooterText(string text)
            {
                if (_footer != null) _footer.text = text ?? string.Empty;
            }

            public void SetItems(IReadOnlyList<SidebarItem> items)
            {
                if (_disposed || _list == null) return;
                ClearItems();
                if (items == null) return;

                for (int i = 0; i < items.Count; i++)
                {
                    SidebarItem item = items[i];
                    if (item == null) continue;
                    string id = string.IsNullOrEmpty(item.Id) ? "item_" + i : item.Id;
                    Action cb = item.OnClick;
                    TMP_Text label = AddButton(_list, item.Text ?? string.Empty, cb, out Image rowImg);
                    label.alignment = TextAlignmentOptions.Left;
                    label.margin = new Vector4(12f, 0f, 12f, 0f);
                    label.enableAutoSizing = true;
                    label.fontSizeMin = 13f;
                    label.fontSizeMax = 20f;
                    label.enableWordWrapping = true;
                    rowImg.GetComponent<Button>().interactable = item.Interactable;
                    rowImg.gameObject.AddComponent<LayoutElement>().preferredHeight = Mathf.Max(32f, item.PreferredHeight);
                    _rowImages[id] = rowImg;
                }

                _list.anchoredPosition = Vector2.zero;
                RefreshRowColors();
            }

            public void SetSelectedItem(string itemId)
            {
                _selectedId = itemId;
                RefreshRowColors();
            }

            public void ClearItems()
            {
                if (_list != null)
                {
                    for (int i = _list.childCount - 1; i >= 0; i--)
                    {
                        Transform child = _list.GetChild(i);
                        child.gameObject.SetActive(false);
                        UnityEngine.Object.Destroy(child.gameObject);
                    }
                }
                _rowImages.Clear();
            }

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                _rowImages.Clear();
                if (_root != null) UnityEngine.Object.Destroy(_root.gameObject);
            }

            private string FormatHeaderTitle() => _baseTitle + (_expanded ? "  \u25be" : "  \u25b8");

            private void RefreshRowColors()
            {
                foreach (KeyValuePair<string, Image> kv in _rowImages)
                {
                    if (kv.Value == null) continue;
                    bool sel = !string.IsNullOrEmpty(_selectedId)
                        && string.Equals(kv.Key, _selectedId, StringComparison.Ordinal);
                    kv.Value.color = sel ? SelectedRowColor : RowColor;
                }
            }

            private void AlignIfMirrored()
            {
                if (!_mirrorHistory || _nativeScroll == null || !(_root.parent is RectTransform canvas)) return;
                _nativeScroll.GetWorldCorners(_corners);
                Vector2 lower = canvas.InverseTransformPoint(_corners[0]);
                Vector2 upper = canvas.InverseTransformPoint(_corners[2]);
                Rect bounds = canvas.rect;
                if (bounds.width <= 1f || bounds.height <= 1f) return;
                float left = (bounds.xMax - upper.x) / bounds.width;
                float right = (bounds.xMax - lower.x) / bounds.width;
                float bottom = (lower.y - bounds.yMin) / bounds.height;
                float top = (upper.y - bounds.yMin + 70f) / bounds.height;
                if (left < 0f || right > 0.28f || right - left < 0.07f || top > 1f || top - bottom < 0.28f)
                    return;
                _root.anchorMin = new Vector2(left, bottom);
                _root.anchorMax = new Vector2(right, top);
            }

            private TMP_Text AddButton(Transform parent, string title, Action click, out Image image)
            {
                RectTransform rect = CreateRect("JG_Row", parent);
                image = rect.gameObject.AddComponent<Image>();
                image.color = RowColor;
                Button btn = rect.gameObject.AddComponent<Button>();
                btn.targetGraphic = image;
                if (click != null) btn.onClick.AddListener(() => click());

                TMP_Text text = UnityEngine.Object.Instantiate(_template, rect, false);
                text.gameObject.SetActive(true);
                if (_cjk != null) text.font = _cjk;
                text.enableAutoSizing = false;
                text.fontSize = 18f;
                text.alignment = TextAlignmentOptions.Center;
                text.enableWordWrapping = false;
                text.overflowMode = TextOverflowModes.Ellipsis;
                text.color = new Color(0.97f, 0.91f, 0.76f);
                text.raycastTarget = false;
                text.text = title;
                Place(text.rectTransform, Vector2.zero, Vector2.one);
                return text;
            }

            private static RectTransform CreateRect(string name, Transform parent)
            {
                var rect = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
                rect.SetParent(parent, false);
                return rect;
            }

            private static void Place(RectTransform rect, Vector2 min, Vector2 max)
            {
                rect.anchorMin = min;
                rect.anchorMax = max;
                rect.offsetMin = rect.offsetMax = Vector2.zero;
                rect.localScale = Vector3.one;
            }
        }
    }
}
