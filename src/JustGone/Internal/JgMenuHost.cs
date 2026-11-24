using System;
using System.Collections;
using System.Collections.Generic;
using JustGone.Core;
using JustGone.Services;
using Michsky.UI.ModernUIPack;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using ContentType = TMPro.TMP_InputField.ContentType;
using LineType = TMPro.TMP_InputField.LineType;
using OnChangeEvent = TMPro.TMP_InputField.OnChangeEvent;
using ButtonClickedEvent = UnityEngine.UI.Button.ButtonClickedEvent;
using MovementType = UnityEngine.UI.ScrollRect.MovementType;
using Direction = UnityEngine.UI.Scrollbar.Direction;
using ScrollbarVisibility = UnityEngine.UI.ScrollRect.ScrollbarVisibility;
using SelectorItem = Michsky.UI.ModernUIPack.HorizontalSelector.Item;
using SelectorEvent = Michsky.UI.ModernUIPack.HorizontalSelector.SelectorEvent;

namespace JustGone.Internal
{
    /// <summary>
    /// MainScene Local Play Mode Selection host and native BasicPanel config page renderer.
    /// Extracted and decoupled from WeiXiangQi v0.10.10.0 (WxqMenuConfigPanel.cs).
    ///
    /// Coexistence guarantees:
    ///   - Appends registered Just Gone mode items to ModeSelection_LM via CreateNewItem without modifying
    ///     vanilla indices [0, 1] or any unregistered third-party items (such as WeiXiangQi at index 2).
    ///   - Captures canonical BasicPanel/List geometry once before wrapping in ScrollRect.
    ///   - Tears down ScrollRect and restores vanilla row visibility whenever leaving a Just Gone mode.
    /// </summary>
    internal sealed class JgMenuHost : MonoBehaviour
    {
        private const string TaggedSelector = "ModeSelection_LM";
        private const string RowPrefix = "JG_MENU_";

        internal static JgMenuHost Current { get; private set; }

        private sealed class NativeRow
        {
            public GameObject Root;
            public TMP_Text Title;
            public Transform Selector;
            public HorizontalSelector SelectorComponent;
            public TMP_Text Value;
            public Button Prev;
            public Button Next;
            public RectTransform Right;
        }

        private sealed class PageBuilder : IMenuPageBuilder
        {
            private readonly JgMenuHost _host;
            private readonly List<Action<Transform, TMP_Text>> _bottomActions = new List<Action<Transform, TMP_Text>>();

            public PageBuilder(JgMenuHost host)
            {
                _host = host;
            }

            public void AddChoiceRow(
                string title,
                string[] options,
                int selectedIndex,
                Action<int> onChanged,
                Action onValueClicked = null,
                Func<int, string> formatLabel = null)
            {
                _host.CreateChoiceRow(title, options, selectedIndex, onChanged, onValueClicked, formatLabel);
            }

            public void AddInputRow(
                string title,
                string initialValue,
                Action<string> onChanged,
                string placeholder = null,
                bool password = false)
            {
                _host.CreateInputRow(title, initialValue, password, onChanged, placeholder);
            }

            public void AddHeaderRow(string title, string hintText, Action onClick)
            {
                _host.CreateHeaderRow(title, hintText, onClick);
            }

            public void AddStatusRow(string title, string value)
            {
                _host.CreateStatusRow(title, value);
            }

            public void AddButtonRow(string title, string[] labels, Action[] actions)
            {
                _host.CreateButtonRow(title, labels, actions);
            }

            public void AddBottomAction(string label, Action onClick, float width = 260f)
            {
                _bottomActions.Add((parent, template) => _host.QueueBottomAction(parent, template, label, width, onClick));
            }

            public void ShowTransientNotice(string message, float durationSeconds = 6f)
            {
                _host.ShowMenuNotice(message, durationSeconds);
            }

            public void RequestRebuild(bool scrollToTop = false)
            {
                _host.RequestRebuild(scrollToTop);
            }

            internal void CommitBottomActions()
            {
                if (_bottomActions.Count == 0) return;
                _host.BuildBottomActionBar(_bottomActions);
            }
        }

        private MenuService _menuService;
        private ResourceService _resources;

        private HorizontalSelector _selector;
        private RectTransform _basicPanel;
        private RectTransform _list;
        private RectTransform _scrollRoot;
        private ScrollRect _scrollRect;
        private Scrollbar _nativeScrollbar;
        private GameObject _wheelCatcher;
        private RectTransform _bottomActionsRoot;
        private GameObject _rowTemplate;
        private GameObject _inputTemplate;
        private GameObject _buttonTemplate;

        private readonly List<GameObject> _rows = new List<GameObject>();
        private readonly List<KeyValuePair<TMP_InputField, string>> _deferredInputs = new List<KeyValuePair<TMP_InputField, string>>();
        private readonly List<Action> _pendingBottomActionBuilders = new List<Action>();
        private float _bottomActionOffsetAccum;

        private bool _canonicalCaptured;
        private Transform _originalListParent;
        private int _originalSibling;
        private Vector2 _originalAnchorMin, _originalAnchorMax, _originalPivot, _originalSize, _originalPos;
        private ContentSizeFitter _listFitter;
        private int _originalListFit = -1;
        private float _canonicalViewportHeight;

        private string _activeModeId;
        private bool _visible;
        private bool _built;
        private bool _rebuildPending;
        private bool _pendingForceTop;
        private bool _suppressCallbacks;
        private int _insertIndex;
        private int _uiGeneration;
        private int _pageGeneration;
        private int _layoutSerial;
        private float _lastContentHeight;
        private int _screenW, _screenH;
        private float _nextSyncCheck;

        internal bool IsMainMenuReady => _selector != null && _basicPanel != null && _list != null;

        internal static JgMenuHost EnsureCreated(MenuService menuService, ResourceService resources)
        {
            if (Current != null) return Current;
            var go = new GameObject("JG_MenuHost");
            JgMenuHost host = go.AddComponent<JgMenuHost>();
            host._menuService = menuService;
            host._resources = resources;
            Current = host;
            return host;
        }

        internal static bool ShouldAbsorbMenuWheelStatic()
        {
            return Current != null && Current.ShouldAbsorbWheelInternal();
        }

        internal void RequestRebuild(bool scrollToTop)
        {
            if (scrollToTop) _pendingForceTop = true;
            if (!_visible || !_built) return;
            if (!_rebuildPending)
            {
                _rebuildPending = true;
                StartCoroutine(RebuildDeferred(++_layoutSerial));
            }
        }

        internal void ShowMenuNotice(string message, float durationSeconds)
        {
            if (string.IsNullOrWhiteSpace(message)) return;
            Transform hostParent = _basicPanel != null ? (Transform)_basicPanel : null;
            if (hostParent == null)
            {
                GameObject canvasGo = GameObject.Find("Canvas");
                if (canvasGo != null) hostParent = canvasGo.transform;
            }
            if (hostParent == null) return;

            TMP_Text template = FindAnyLabelTemplate();
            if (template == null) return;

            Transform old = hostParent.Find("JG_MenuNotice");
            if (old != null) UnityEngine.Object.Destroy(old.gameObject);

            TMP_Text notice = UnityEngine.Object.Instantiate(template, hostParent, false);
            notice.gameObject.name = "JG_MenuNotice";
            DisableLocalizeComponents(notice.gameObject);
            TMP_FontAsset cjk = _resources?.GetCjkFontInternal();
            if (cjk != null) notice.font = cjk;
            notice.gameObject.SetActive(true);
            notice.text = message;
            notice.raycastTarget = false;
            notice.alignment = TextAlignmentOptions.Center;
            notice.fontSize = 22f;
            notice.enableWordWrapping = true;
            notice.color = new Color(0.98f, 0.90f, 0.72f, 1f);

            RectTransform rt = notice.rectTransform;
            rt.anchorMin = new Vector2(0.5f, 0f);
            rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.anchoredPosition = new Vector2(0f, 110f);
            rt.sizeDelta = new Vector2(900f, 70f);
            rt.localScale = Vector3.one;
            notice.transform.SetAsLastSibling();

            StartCoroutine(DestroyNoticeAfter(notice.gameObject, Mathf.Max(1.5f, durationSeconds)));
        }

        private static IEnumerator DestroyNoticeAfter(GameObject go, float seconds)
        {
            yield return new WaitForSecondsRealtime(seconds);
            if (go != null) UnityEngine.Object.Destroy(go);
        }

        private void OnDestroy()
        {
            if (_visible) TeardownNativeUi();
            if (Current == this) Current = null;
        }

        private void Update()
        {
            if (_menuService == null) return;

            if (_selector == null)
            {
                _selector = FindTaggedSelector();
                if (_selector != null)
                {
                    ResolveBasicPanel();
                    CaptureCanonicalGeometryIfPossible();
                    SyncRegisteredModesIntoSelector();
                }
            }
            if (_selector == null) return;

            CaptureCanonicalGeometryIfPossible();

            if (Time.unscaledTime >= _nextSyncCheck)
            {
                _nextSyncCheck = Time.unscaledTime + 0.5f;
                SyncRegisteredModesIntoSelector();
            }

            if (_screenW == 0 && _screenH == 0)
            {
                _screenW = Screen.width;
                _screenH = Screen.height;
            }
            else if (Screen.width != _screenW || Screen.height != _screenH)
            {
                _screenW = Screen.width;
                _screenH = Screen.height;
                if (_visible) RequestRebuild(true);
            }

            if (_basicPanel == null) return;

            string currentTitle = GetCurrentSelectorTitle();
            MenuRouteEntry matched = _menuService.RouteTable.FindByAssignedIndex(_selector.index, currentTitle);
            bool shouldShow = _basicPanel.gameObject.activeSelf
                && _basicPanel.gameObject.activeInHierarchy
                && matched != null
                && matched.Enabled;

            string targetModeId = shouldShow ? matched.ModeId : null;

            // LocalizationManager.GetTermTranslation returns empty for custom non-localized titles; restore display title.
            if (shouldShow && _selector.label != null && string.IsNullOrEmpty(_selector.label.text))
            {
                TMP_FontAsset cjk = _resources?.GetCjkFontInternal();
                if (cjk != null) _selector.label.font = cjk;
                _selector.label.text = matched.DisplayTitle;
            }

            if (shouldShow && (!_visible || !string.Equals(_activeModeId, targetModeId, StringComparison.Ordinal)))
            {
                if (_visible) TeardownNativeUi();
                _visible = true;
                _activeModeId = targetModeId;
                _pageGeneration++;
                StartCoroutine(EnterBasicPageRoutine(_pageGeneration, targetModeId));
            }
            else if (!shouldShow && _visible)
            {
                _visible = false;
                _activeModeId = null;
                _pageGeneration++;
                TeardownNativeUi();
            }
        }

        internal void SyncRegisteredModesIntoSelector()
        {
            if (_selector == null || _menuService == null) return;
            if (_selector.itemList == null) _selector.itemList = new List<SelectorItem>();

            IReadOnlyList<MenuRouteEntry> entries = _menuService.RouteTable.Entries;
            bool addedAny = false;

            for (int i = 0; i < entries.Count; i++)
            {
                MenuRouteEntry entry = entries[i];
                int existingIndex = FindItemIndexByTitle(_selector, entry.DisplayTitle);
                if (existingIndex >= _menuService.RouteTable.VanillaModeCount)
                {
                    entry.AssignedSelectorIndex = existingIndex;
                    continue;
                }

                // Append to end of itemList so vanilla [0,1] and WeiXiangQi [2] keep their exact indices!
                try
                {
                    _selector.CreateNewItem(entry.DisplayTitle);
                }
                catch
                {
                    var item = new SelectorItem
                    {
                        itemTitle = entry.DisplayTitle,
                        onValueChanged = new UnityEvent()
                    };
                    _selector.itemList.Add(item);
                }

                int assigned = FindItemIndexByTitle(_selector, entry.DisplayTitle);
                if (assigned >= _menuService.RouteTable.VanillaModeCount)
                {
                    // Ensure ModernUIPack ForwardClick/PreviousClick never hits NRE on null onValueChanged
                    if (_selector.itemList[assigned] != null && _selector.itemList[assigned].onValueChanged == null)
                        _selector.itemList[assigned].onValueChanged = new UnityEvent();

                    entry.AssignedSelectorIndex = assigned;
                    addedAny = true;
                    JgLog.Info("Registered menu mode \"" + entry.DisplayTitle + "\" (" + entry.ModeId + ") at selector index " + assigned);
                }
            }

            for (int i = 0; i < _selector.itemList.Count; i++)
            {
                if (_selector.itemList[i] != null && _selector.itemList[i].onValueChanged == null)
                    _selector.itemList[i].onValueChanged = new UnityEvent();
            }

            if (addedAny)
            {
                try { _selector.UpdateIndicators(); } catch { }
                try { _selector.UpdateUI(); } catch { }
            }
        }

        private string GetCurrentSelectorTitle()
        {
            if (_selector == null || _selector.itemList == null) return null;
            int idx = _selector.index;
            if (idx < 0 || idx >= _selector.itemList.Count) return null;
            return _selector.itemList[idx]?.itemTitle;
        }

        private static int FindItemIndexByTitle(HorizontalSelector selector, string title)
        {
            if (selector == null || selector.itemList == null) return -1;
            for (int i = 0; i < selector.itemList.Count; i++)
            {
                if (selector.itemList[i] != null && string.Equals(selector.itemList[i].itemTitle, title, StringComparison.Ordinal))
                    return i;
            }
            return -1;
        }

        private static HorizontalSelector FindTaggedSelector()
        {
            HorizontalSelector[] all = UnityEngine.Object.FindObjectsOfType<HorizontalSelector>();
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != null && all[i].selectorTag == TaggedSelector) return all[i];
            }
            return null;
        }

        private void ResolveBasicPanel()
        {
            if (_selector == null) return;
            for (Transform cur = _selector.transform; cur != null; cur = cur.parent)
            {
                if (cur.name == "BasicPanel")
                {
                    _basicPanel = cur as RectTransform;
                    break;
                }
            }
            if (_basicPanel == null)
            {
                GameObject found = GameObject.Find("Canvas/Main Panels/Local Play Mode Windows/FriendsPlay/Content/BasicPanel");
                if (found != null) _basicPanel = found.transform as RectTransform;
            }
            if (_basicPanel == null) return;

            Transform listT = _basicPanel.Find("List")
                ?? _basicPanel.Find("JG_MenuScroll/List")
                ?? _basicPanel.Find("WXQ_AIConfigScroll/List");
            _list = listT as RectTransform;
            if (_list == null) return;

            Transform boardSize = _list.Find("Board Size");
            if (boardSize != null) _rowTemplate = boardSize.gameObject;

            if (_inputTemplate == null)
            {
                GameObject inp = GameObject.Find("Canvas/Main Panels/Local Play Mode Windows/VSComputer/Content/BasicPanel/List/PlayerName/Input Field");
                if (inp != null) _inputTemplate = inp;
                else
                {
                    TMP_InputField[] allInputs = Resources.FindObjectsOfTypeAll<TMP_InputField>();
                    for (int i = 0; i < allInputs.Length; i++)
                    {
                        if (allInputs[i] != null && allInputs[i].name == "Input Field"
                            && JgGameBridge.PathOf(allInputs[i].transform).Contains("PlayerName/Input Field"))
                        {
                            _inputTemplate = allInputs[i].gameObject;
                            break;
                        }
                    }
                }
            }

            if (_buttonTemplate == null)
            {
                GameObject btn = GameObject.Find("Canvas/Main Panels/Local Play Mode Windows/FriendsPlay/Content/EnvironmentPanel/Buttons/Start");
                if (btn != null) _buttonTemplate = btn;
            }
        }

        private void CaptureCanonicalGeometryIfPossible()
        {
            if (_canonicalCaptured || _basicPanel == null || _list == null) return;
            if (_list.parent != _basicPanel) return;
            float h = _list.rect.height;
            if (h < 50f) return;

            _originalListParent = _list.parent;
            _originalSibling = _list.GetSiblingIndex();
            _originalAnchorMin = _list.anchorMin;
            _originalAnchorMax = _list.anchorMax;
            _originalPivot = _list.pivot;
            _originalSize = _list.sizeDelta;
            _originalPos = _list.anchoredPosition;
            _listFitter = _list.GetComponent<ContentSizeFitter>();
            if (_listFitter != null) _originalListFit = (int)_listFitter.verticalFit;
            _canonicalViewportHeight = h;
            _canonicalCaptured = true;
        }

        private IEnumerator EnterBasicPageRoutine(int generation, string modeId)
        {
            float start = Time.realtimeSinceStartup;
            CanvasGroup group = _basicPanel.GetComponent<CanvasGroup>();
            if (group != null) group.interactable = false;
            try
            {
                while (_visible && generation == _pageGeneration && (!IsBasicPageSettled() || !IsListAvailableForHost()))
                {
                    if (Time.realtimeSinceStartup - start > 3f) break;
                    yield return null;
                }
                if (!_visible || generation != _pageGeneration || _basicPanel == null) yield break;
                BuildNativeUi(modeId);
            }
            finally
            {
                if (group != null) group.interactable = true;
            }
        }

        private bool IsListAvailableForHost()
        {
            ResolveBasicPanel();
            if (_basicPanel == null || _list == null) return false;
            return _list.parent == _basicPanel || (_scrollRoot != null && _list.parent == _scrollRoot);
        }

        private bool IsBasicPageSettled()
        {
            if (_basicPanel == null || !_basicPanel.gameObject.activeInHierarchy) return false;
            CanvasGroup group = _basicPanel.GetComponent<CanvasGroup>();
            if (group != null && group.alpha < 0.985f) return false;

            Animator anim = _basicPanel.GetComponent<Animator>();
            if (anim != null && anim.isActiveAndEnabled && anim.runtimeAnimatorController != null)
            {
                if (anim.IsInTransition(0)) return false;
                AnimatorStateInfo st = anim.GetCurrentAnimatorStateInfo(0);
                if (st.length > 0.01f && st.normalizedTime < 1f) return false;
            }
            return true;
        }

        private void BuildNativeUi(string modeId)
        {
            ResolveBasicPanel();
            if (_list == null || _rowTemplate == null) return;

            HideVanillaOptionRows();
            EnsureScroll();
            RebuildRowsNow(modeId, true);
        }

        private void HideVanillaOptionRows()
        {
            if (_list == null) return;
            for (int i = 0; i < _list.childCount; i++)
            {
                GameObject go = _list.GetChild(i).gameObject;
                if (go.name.StartsWith(RowPrefix, StringComparison.Ordinal)) continue;
                go.SetActive(go.name == "Game Header" || go.name == "Mode Selection");
            }
        }

        private void RestoreVanillaModeRows(int modeIndex)
        {
            if (_list == null || modeIndex < 0 || modeIndex > 1) return;
            bool freePlacement = modeIndex == 1;
            for (int i = 0; i < _list.childCount; i++)
            {
                GameObject go = _list.GetChild(i).gameObject;
                string n = go.name;
                if (n.StartsWith(RowPrefix, StringComparison.Ordinal)) continue;
                if (n == "Game Header" || n == "Mode Selection" || n == "Board Size") go.SetActive(true);
                else if (n == "Rule" || n == "Timer" || n == "Handicap") go.SetActive(!freePlacement);
            }
        }

        private void EnsureScroll()
        {
            if (_scrollRoot != null) return;
            if (!_canonicalCaptured) CaptureCanonicalGeometryIfPossible();
            if (!_canonicalCaptured)
            {
                _originalListParent = _list.parent;
                _originalSibling = _list.GetSiblingIndex();
                _originalAnchorMin = _list.anchorMin;
                _originalAnchorMax = _list.anchorMax;
                _originalPivot = _list.pivot;
                _originalSize = _list.sizeDelta;
                _originalPos = _list.anchoredPosition;
                _listFitter = _list.GetComponent<ContentSizeFitter>();
                if (_listFitter != null) _originalListFit = (int)_listFitter.verticalFit;
                _canonicalViewportHeight = _list.rect.height;
                _canonicalCaptured = true;
            }

            if (_listFitter != null) _listFitter.verticalFit = ContentSizeFitter.FitMode.Unconstrained;

            var scrollGo = new GameObject("JG_MenuScroll", typeof(RectTransform), typeof(RectMask2D), typeof(ScrollRect));
            _scrollRoot = scrollGo.GetComponent<RectTransform>();
            _scrollRoot.SetParent(_basicPanel, false);
            _scrollRoot.anchorMin = _originalAnchorMin;
            _scrollRoot.anchorMax = _originalAnchorMax;
            _scrollRoot.pivot = _originalPivot;
            _scrollRoot.anchoredPosition = _originalPos;
            _scrollRoot.sizeDelta = _originalSize;
            _scrollRoot.localScale = Vector3.one;
            _scrollRoot.SetSiblingIndex(_originalSibling);

            _list.SetParent(_scrollRoot, false);
            _list.anchorMin = new Vector2(0f, 1f);
            _list.anchorMax = new Vector2(1f, 1f);
            _list.pivot = new Vector2(0.5f, 1f);
            _list.anchoredPosition = Vector2.zero;
            _list.sizeDelta = new Vector2(0f, _canonicalViewportHeight);
            _list.localScale = Vector3.one;

            _scrollRect = scrollGo.GetComponent<ScrollRect>();
            _scrollRect.content = _list;
            _scrollRect.viewport = _scrollRoot;
            _scrollRect.horizontal = false;
            _scrollRect.vertical = false;
            _scrollRect.movementType = MovementType.Clamped;
            _scrollRect.scrollSensitivity = 32f;
            _scrollRect.inertia = true;

            _wheelCatcher = new GameObject("JG_ScrollWheel", typeof(RectTransform), typeof(Image));
            _wheelCatcher.transform.SetParent(_scrollRoot, false);
            var wrt = (RectTransform)_wheelCatcher.transform;
            wrt.anchorMin = Vector2.zero;
            wrt.anchorMax = Vector2.one;
            wrt.offsetMin = wrt.offsetMax = Vector2.zero;
            Image wh = _wheelCatcher.GetComponent<Image>();
            wh.color = new Color(0f, 0f, 0f, 0f);
            wh.raycastTarget = true;
            _wheelCatcher.transform.SetAsFirstSibling();
            _wheelCatcher.SetActive(false);

            CloneNativeScrollbar();
            Transform arrow = _basicPanel.Find("Arrow");
            if (arrow != null) arrow.SetAsLastSibling();
        }

        private void CloneNativeScrollbar()
        {
            Scrollbar template = FindNativeScrollbarTemplate();
            if (template == null || _scrollRoot == null || _scrollRect == null) return;

            GameObject clone = UnityEngine.Object.Instantiate(template.gameObject, _scrollRoot, false);
            clone.name = "JG_NativeScrollbar";
            clone.SetActive(true);
            if (clone.transform is RectTransform rt)
            {
                rt.anchorMin = new Vector2(1f, 0f);
                rt.anchorMax = new Vector2(1f, 1f);
                rt.pivot = new Vector2(1f, 0.5f);
                rt.anchoredPosition = Vector2.zero;
                rt.sizeDelta = new Vector2(12f, 0f);
                rt.localScale = Vector3.one;
            }
            Scrollbar sb = clone.GetComponent<Scrollbar>();
            if (sb == null) { UnityEngine.Object.Destroy(clone); return; }

            Component[] all = clone.GetComponentsInChildren<Component>(true);
            for (int i = 0; i < all.Length; i++)
            {
                Component c = all[i];
                if (c == null || c is RectTransform || c is CanvasRenderer || c is Image || c is Scrollbar) continue;
                if (c is Behaviour b) b.enabled = false;
                UnityEngine.Object.Destroy(c);
            }

            sb.direction = Direction.BottomToTop;
            sb.onValueChanged = new Scrollbar.ScrollEvent();
            Navigation nav = sb.navigation;
            nav.mode = Navigation.Mode.None;
            sb.navigation = nav;
            _nativeScrollbar = sb;
            _scrollRect.verticalScrollbar = sb;
            _scrollRect.verticalScrollbarVisibility = ScrollbarVisibility.AutoHide;
            clone.SetActive(false);
        }

        private static Scrollbar FindNativeScrollbarTemplate()
        {
            Scrollbar best = null;
            int bestScore = int.MinValue;
            Scrollbar[] all = Resources.FindObjectsOfTypeAll<Scrollbar>();
            for (int i = 0; i < all.Length; i++)
            {
                Scrollbar sb = all[i];
                if (sb == null) continue;
                string path = JgGameBridge.PathOf(sb.transform);
                if (path.Contains("JG_") || path.Contains("WXQ_")) continue;
                int score = 0;
                if (sb.gameObject.activeInHierarchy) score += 4;
                if (path.Contains("Local Play Mode Windows")) score += 16;
                if (path.IndexOf("Scroll View", StringComparison.OrdinalIgnoreCase) >= 0) score += 4;
                if (path.IndexOf("Dropdown", StringComparison.OrdinalIgnoreCase) >= 0) score -= 8;
                if (score > bestScore) { bestScore = score; best = sb; }
            }
            return best;
        }

        private void TeardownNativeUi()
        {
            _uiGeneration++;
            _rebuildPending = false;
            _deferredInputs.Clear();
            ClearRows();

            if (_scrollRoot != null && _list != null)
            {
                _list.SetParent(_originalListParent, false);
                _list.anchorMin = _originalAnchorMin;
                _list.anchorMax = _originalAnchorMax;
                _list.pivot = _originalPivot;
                _list.sizeDelta = _originalSize;
                _list.anchoredPosition = _originalPos;
                _list.localScale = Vector3.one;
                _list.SetSiblingIndex(_originalSibling);
                if (_listFitter != null && _originalListFit >= 0)
                    _listFitter.verticalFit = (ContentSizeFitter.FitMode)_originalListFit;
                if (_wheelCatcher != null) UnityEngine.Object.Destroy(_wheelCatcher);
                UnityEngine.Object.Destroy(_scrollRoot.gameObject);
                _scrollRoot = null;
                _scrollRect = null;
                _wheelCatcher = null;
                _nativeScrollbar = null;
                Canvas.ForceUpdateCanvases();
            }

            RestoreVanillaModeRows(_selector == null ? -1 : _selector.index);
            _built = false;
        }

        private void ClearRows()
        {
            if (_bottomActionsRoot != null)
            {
                _bottomActionsRoot.gameObject.SetActive(false);
                UnityEngine.Object.Destroy(_bottomActionsRoot.gameObject);
                _bottomActionsRoot = null;
            }
            for (int i = 0; i < _rows.Count; i++)
            {
                if (_rows[i] != null)
                {
                    _rows[i].SetActive(false);
                    UnityEngine.Object.Destroy(_rows[i]);
                }
            }
            _rows.Clear();
        }

        private IEnumerator RebuildDeferred(int serial)
        {
            yield return null;
            if (serial != _layoutSerial || !_visible) { _rebuildPending = false; yield break; }
            while (_visible && !IsBasicPageSettled()) yield return null;
            if (!_visible || serial != _layoutSerial) yield break;
            _rebuildPending = false;
            bool forceTop = _pendingForceTop;
            _pendingForceTop = false;
            RebuildRowsNow(_activeModeId, forceTop);
        }

        private void RebuildRowsNow(string modeId, bool forceTop)
        {
            if (_list == null || _rowTemplate == null || string.IsNullOrEmpty(modeId)) return;

            _suppressCallbacks = true;
            int generation = ++_uiGeneration;
            float prevOffset = Mathf.Max(0f, _list.anchoredPosition.y);
            ClearRows();
            _deferredInputs.Clear();

            int insertBase = 0;
            Transform modeSel = _list.Find("Mode Selection");
            if (modeSel != null) insertBase = modeSel.GetSiblingIndex();
            _insertIndex = insertBase + 1;

            var builder = new PageBuilder(this);
            try
            {
                _menuService.InvokeBuildConfigPage(modeId, builder);
                builder.CommitBottomActions();
            }
            catch (Exception ex)
            {
                JgLog.Error("Menu config page build failed for mode " + modeId, ex);
            }

            _built = true;
            ApplyContentAndScroll(forceTop, prevOffset);

            var pending = new List<KeyValuePair<TMP_InputField, string>>(_deferredInputs);
            _deferredInputs.Clear();
            StartCoroutine(ApplyDeferredInputs(generation, pending));
        }

        private void ApplyContentAndScroll(bool forceTop, float previousOffset)
        {
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(_list);
            float preferred = LayoutUtility.GetPreferredHeight(_list);
            float viewportHeight = _canonicalViewportHeight > 1f
                ? _canonicalViewportHeight
                : (_scrollRoot == null ? preferred : _scrollRoot.rect.height);
            if (viewportHeight < 1f) viewportHeight = preferred;
            float contentHeight = preferred > 1f ? preferred : viewportHeight;

            _list.sizeDelta = new Vector2(0f, Mathf.Ceil(contentHeight));
            _lastContentHeight = contentHeight;
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(_list);

            bool scrollable = preferred > viewportHeight + 0.5f;
            if (_scrollRect != null)
            {
                _scrollRect.vertical = scrollable;
                _scrollRect.enabled = scrollable;
                _scrollRect.verticalScrollbar = scrollable ? _nativeScrollbar : null;
            }
            if (_wheelCatcher != null) _wheelCatcher.SetActive(scrollable);
            if (_nativeScrollbar != null) _nativeScrollbar.gameObject.SetActive(scrollable);

            if (!scrollable || forceTop)
            {
                _list.anchoredPosition = Vector2.zero;
                if (_scrollRect != null) _scrollRect.verticalNormalizedPosition = 1f;
            }
            else
            {
                float maxOffset = Mathf.Max(0f, contentHeight - viewportHeight);
                float offset = Mathf.Clamp(previousOffset, 0f, maxOffset);
                _list.anchoredPosition = new Vector2(_list.anchoredPosition.x, offset);
                if (_scrollRect != null && maxOffset > 0.5f)
                    _scrollRect.verticalNormalizedPosition = 1f - (offset / maxOffset);
            }

            Canvas.ForceUpdateCanvases();
            if (_scrollRect != null)
            {
                _scrollRect.StopMovement();
                _scrollRect.velocity = Vector2.zero;
            }
        }

        private IEnumerator ApplyDeferredInputs(int generation, List<KeyValuePair<TMP_InputField, string>> pending)
        {
            yield return null;
            yield return null;
            if (generation != _uiGeneration) yield break;
            for (int i = 0; i < pending.Count; i++)
            {
                TMP_InputField field = pending[i].Key;
                if (field == null) continue;
                string text = pending[i].Value ?? string.Empty;
                field.SetTextWithoutNotify(text);
                if (field.textComponent != null)
                {
                    field.textComponent.text = text;
                    RefreshText(field.textComponent);
                }
                field.ForceLabelUpdate();
            }
            if (generation == _uiGeneration) _suppressCallbacks = false;
        }

        private bool ShouldAbsorbWheelInternal()
        {
            if (!_visible || !_built || _scrollRoot == null || _list == null) return false;
            if (_scrollRect == null || !_scrollRect.enabled || !_scrollRect.vertical) return false;
            float delta = Input.mouseScrollDelta.y;
            if (Mathf.Abs(delta) < 0.01f) return false;
            float viewportH = _canonicalViewportHeight > 1f ? _canonicalViewportHeight : _scrollRoot.rect.height;
            float contentH = Mathf.Max(_list.rect.height, _lastContentHeight);
            float maxOffset = Mathf.Max(0f, contentH - viewportH);
            if (maxOffset < 0.5f) return false;

            Canvas canvas = _scrollRoot.GetComponentInParent<Canvas>();
            Camera cam = canvas == null ? null : canvas.worldCamera;
            if (!RectTransformUtility.RectangleContainsScreenPoint(_scrollRoot, Input.mousePosition, cam))
                return false;

            float offset = Mathf.Max(0f, _list.anchoredPosition.y);
            return delta < 0f ? offset < maxOffset - 0.5f : offset > 0.5f;
        }

        // ------------------------------------------------------------------
        // Native Row Builders (extracted from WxqMenuConfigPanel.cs)
        // ------------------------------------------------------------------

        private NativeRow NewRow(string title)
        {
            GameObject go = UnityEngine.Object.Instantiate(_rowTemplate, _list, false);
            go.name = RowPrefix + title;
            go.SetActive(true);
            go.transform.SetSiblingIndex(_insertIndex++);

            var row = new NativeRow { Root = go };
            Transform titleT = go.transform.Find("Title");
            if (titleT != null)
            {
                DisableLocalizeComponents(titleT.gameObject);
                row.Title = titleT.GetComponent<TMP_Text>();
                if (row.Title != null)
                {
                    TMP_FontAsset cjk = _resources?.GetCjkFontInternal();
                    if (cjk != null) row.Title.font = cjk;
                    row.Title.text = title;
                }
            }

            row.Selector = go.transform.Find("Horizontal Selector");
            if (row.Selector != null)
            {
                row.SelectorComponent = row.Selector.GetComponent<HorizontalSelector>();
                if (row.SelectorComponent != null) row.SelectorComponent.enabled = false;
                row.Prev = row.Selector.Find("Prev")?.GetComponent<Button>();
                row.Next = row.Selector.Find("Next")?.GetComponent<Button>();
                Transform helper = row.Selector.Find("Text Helper");
                if (helper != null) helper.gameObject.SetActive(false);

                var rightGo = new GameObject("JG_Right", typeof(RectTransform));
                row.Right = rightGo.GetComponent<RectTransform>();
                row.Right.SetParent(go.transform, false);
                var selRt = (RectTransform)row.Selector;
                row.Right.anchorMin = selRt.anchorMin;
                row.Right.anchorMax = selRt.anchorMax;
                row.Right.pivot = selRt.pivot;
                row.Right.anchoredPosition = selRt.anchoredPosition;
                row.Right.sizeDelta = selRt.sizeDelta;
                row.Right.localScale = Vector3.one;
                rightGo.SetActive(false);
            }

            _rows.Add(go);
            return row;
        }

        private void CreateChoiceRow(
            string title,
            string[] options,
            int selected,
            Action<int> onChanged,
            Action onValueClicked,
            Func<int, string> formatLabel)
        {
            if (options == null || options.Length == 0) return;
            NativeRow row = NewRow(title);
            if (row.Selector == null || row.SelectorComponent == null) return;

            row.Selector.gameObject.SetActive(true);
            TMP_Text[] oldTexts = row.Selector.GetComponentsInChildren<TMP_Text>(true);
            for (int i = 0; i < oldTexts.Length; i++)
                if (oldTexts[i] != null) oldTexts[i].gameObject.SetActive(false);

            TMP_Text display = CreateCenteredLabel(row, onValueClicked);
            HorizontalSelector sel = row.SelectorComponent;
            int idx = Mathf.Clamp(selected, 0, options.Length - 1);
            sel.enableIndicators = false;
            sel.saveValue = false;
            sel.invokeAtStart = false;
            sel.defaultIndex = idx;
            sel.itemList = new List<SelectorItem>();
            for (int i = 0; i < options.Length; i++)
            {
                sel.itemList.Add(new SelectorItem { itemTitle = options[i], onValueChanged = new UnityEvent() });
            }
            sel.enabled = true;
            sel.SetupSelector();

            BindArrow(row.Prev, sel.PreviousClick);
            BindArrow(row.Next, sel.ForwardClick);
            sel.selectorEvent = new SelectorEvent();
            sel.selectorEvent.AddListener(next =>
            {
                if (display != null)
                {
                    display.text = formatLabel != null ? formatLabel(next) : options[next];
                    RefreshText(display);
                }
                if (!_suppressCallbacks) onChanged?.Invoke(next);
            });

            if (display != null)
            {
                display.text = formatLabel != null ? formatLabel(idx) : options[idx];
                RefreshText(display);
            }
        }

        private void CreateInputRow(string title, string value, bool password, Action<string> onChanged, string placeholder)
        {
            NativeRow row = NewRow(title);
            if (row.Selector != null) row.Selector.gameObject.SetActive(false);
            if (row.Right == null || _inputTemplate == null) return;
            row.Right.gameObject.SetActive(true);

            GameObject inputGo = UnityEngine.Object.Instantiate(_inputTemplate, row.Right, false);
            inputGo.name = "JG_Input";
            inputGo.SetActive(true);
            var rt = (RectTransform)inputGo.transform;
            rt.anchorMin = new Vector2(0f, 0.5f);
            rt.anchorMax = new Vector2(1f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(-8f, 40f);
            rt.localScale = Vector3.one;

            TMP_InputField field = inputGo.GetComponentInChildren<TMP_InputField>(true);
            if (field == null) { UnityEngine.Object.Destroy(inputGo); return; }

            TMP_FontAsset cjk = _resources?.GetCjkFontInternal();
            field.interactable = true;
            field.contentType = password ? ContentType.Password : ContentType.Standard;
            field.lineType = LineType.SingleLine;
            field.characterLimit = password ? 256 : 512;
            field.SetTextWithoutNotify(value ?? string.Empty);

            if (field.textComponent != null)
            {
                if (cjk != null) field.textComponent.font = cjk;
                field.textComponent.text = value ?? string.Empty;
                field.textComponent.alignment = TextAlignmentOptions.Center;
                field.textComponent.margin = new Vector4(8f, 0f, 8f, 0f);
            }

            TMP_Text ph = field.placeholder as TMP_Text;
            if (ph == null)
            {
                TMP_Text[] all = inputGo.GetComponentsInChildren<TMP_Text>(true);
                for (int i = 0; i < all.Length; i++)
                    if (all[i] != null && all[i].name == "Placeholder") { ph = all[i]; break; }
            }
            if (ph != null)
            {
                if (cjk != null) ph.font = cjk;
                field.placeholder = ph;
                ph.alignment = TextAlignmentOptions.Center;
                if (!string.IsNullOrEmpty(placeholder)) ph.text = placeholder;
                ph.gameObject.SetActive(string.IsNullOrEmpty(value));
                RefreshText(ph);
            }

            field.onValueChanged = new OnChangeEvent();
            if (onChanged != null)
                field.onValueChanged.AddListener(v => { if (!_suppressCallbacks) onChanged(v ?? string.Empty); });

            field.ForceLabelUpdate();
            _deferredInputs.Add(new KeyValuePair<TMP_InputField, string>(field, value ?? string.Empty));
        }

        private void CreateHeaderRow(string title, string hintText, Action onClick)
        {
            NativeRow row = NewRow(title);
            if (row.Selector != null) row.Selector.gameObject.SetActive(false);
            if (row.Right != null)
            {
                row.Right.gameObject.SetActive(true);
                TMP_Text hint = CreateValueLabel(row, 18f, TextAlignmentOptions.Right);
                if (hint != null) { hint.text = hintText ?? string.Empty; RefreshText(hint); }
            }
            if (onClick == null) return;
            var hitGo = new GameObject("JG_HeaderHit", typeof(RectTransform), typeof(Image), typeof(Button));
            hitGo.transform.SetParent(row.Root.transform, false);
            var hrt = (RectTransform)hitGo.transform;
            hrt.anchorMin = Vector2.zero;
            hrt.anchorMax = Vector2.one;
            hrt.offsetMin = hrt.offsetMax = Vector2.zero;
            Image hit = hitGo.GetComponent<Image>();
            hit.color = new Color(0f, 0f, 0f, 0f);
            hit.raycastTarget = true;
            Button b = hitGo.GetComponent<Button>();
            b.targetGraphic = hit;
            b.onClick = new ButtonClickedEvent();
            b.onClick.AddListener(() => onClick());
            hitGo.transform.SetAsLastSibling();
        }

        private void CreateStatusRow(string title, string value)
        {
            NativeRow row = NewRow(title);
            if (row.Selector != null) row.Selector.gameObject.SetActive(false);
            if (row.Right == null) return;
            row.Right.gameObject.SetActive(true);
            TMP_Text val = CreateValueLabel(row, 17f, TextAlignmentOptions.Left);
            if (val != null) { val.text = value ?? string.Empty; RefreshText(val); }
        }

        private void CreateButtonRow(string title, string[] labels, Action[] actions)
        {
            if (labels == null || _buttonTemplate == null) return;
            NativeRow row = NewRow(title);
            if (row.Selector != null) row.Selector.gameObject.SetActive(false);
            TMP_FontAsset cjk = _resources?.GetCjkFontInternal();
            for (int i = 0; i < labels.Length; i++)
            {
                GameObject btnGo = UnityEngine.Object.Instantiate(_buttonTemplate, row.Root.transform, false);
                btnGo.name = "JG_RowBtn_" + i;
                btnGo.SetActive(true);
                if (btnGo.transform is RectTransform rt)
                {
                    rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(1f, 0.5f);
                    rt.anchoredPosition = new Vector2(-20f - i * 160f, 0f);
                    rt.sizeDelta = new Vector2(150f, 42f);
                    rt.localScale = Vector3.one;
                }
                Button b = btnGo.GetComponent<Button>() ?? btnGo.GetComponentInChildren<Button>(true);
                if (b == null) continue;
                int captured = i;
                b.onClick = new ButtonClickedEvent();
                b.onClick.AddListener(() =>
                {
                    if (actions != null && captured < actions.Length) actions[captured]?.Invoke();
                });
                TMP_Text t = btnGo.GetComponentInChildren<TMP_Text>(true);
                if (t != null)
                {
                    if (cjk != null) t.font = cjk;
                    t.text = labels[i];
                    t.alignment = TextAlignmentOptions.Center;
                }
            }
        }

        private void BuildBottomActionBar(List<Action<Transform, TMP_Text>> actions)
        {
            if (_basicPanel == null || _buttonTemplate == null) return;
            TMP_Text template = FindAnyLabelTemplate();
            _bottomActionsRoot = (RectTransform)new GameObject("JG_BottomActions", typeof(RectTransform)).transform;
            _bottomActionsRoot.SetParent(_basicPanel, false);
            _bottomActionsRoot.anchorMin = _bottomActionsRoot.anchorMax = _bottomActionsRoot.pivot = new Vector2(1f, 0f);

            Transform arrow = _basicPanel.Find("Arrow");
            if (arrow != null && _scrollRoot != null)
            {
                var bounds = new Vector3[4];
                _scrollRoot.GetWorldCorners(bounds);
                Vector3 right = _basicPanel.InverseTransformPoint(bounds[2]);
                Vector3 bottom = _basicPanel.InverseTransformPoint(arrow.position);
                _bottomActionsRoot.localPosition = new Vector3(right.x - 38f, bottom.y + 29f, 0f);
            }
            _bottomActionsRoot.sizeDelta = new Vector2(600f, 62f);

            _bottomActionOffsetAccum = 0f;
            for (int i = actions.Count - 1; i >= 0; i--)
                actions[i](_bottomActionsRoot, template);
        }

        private void QueueBottomAction(Transform parent, TMP_Text template, string label, float width, Action action)
        {
            GameObject btnGo = UnityEngine.Object.Instantiate(_buttonTemplate, parent, false);
            btnGo.name = "JG_BottomAction";
            btnGo.SetActive(true);

            Animator[] anims = btnGo.GetComponentsInChildren<Animator>(true);
            for (int i = 0; i < anims.Length; i++) if (anims[i] != null) anims[i].enabled = false;

            var rt = (RectTransform)btnGo.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(1f, 0f);
            rt.anchoredPosition = new Vector2(-_bottomActionOffsetAccum, 0f);
            rt.sizeDelta = new Vector2(width, 62f);
            rt.localScale = Vector3.one;
            _bottomActionOffsetAccum += width + 24f;

            Button b = btnGo.GetComponent<Button>() ?? btnGo.GetComponentInChildren<Button>(true);
            if (b != null)
            {
                b.onClick = new ButtonClickedEvent();
                if (action != null) b.onClick.AddListener(() => action());
            }

            Image[] imgs = btnGo.GetComponentsInChildren<Image>(true);
            Image srcBg = null;
            for (int i = 0; i < imgs.Length; i++)
            {
                if (imgs[i] == null) continue;
                if (srcBg == null && imgs[i].sprite != null) srcBg = imgs[i];
                if (imgs[i].gameObject == btnGo) imgs[i].enabled = false;
                else imgs[i].gameObject.SetActive(false);
            }
            if (srcBg != null)
            {
                var bgGo = new GameObject("JG_ActionBg", typeof(RectTransform), typeof(Image));
                bgGo.transform.SetParent(btnGo.transform, false);
                var brt = (RectTransform)bgGo.transform;
                brt.anchorMin = Vector2.zero;
                brt.anchorMax = Vector2.one;
                brt.offsetMin = brt.offsetMax = Vector2.zero;
                Image bg = bgGo.GetComponent<Image>();
                bg.sprite = srcBg.sprite;
                bg.color = srcBg.color;
                bg.type = srcBg.type;
                bg.preserveAspect = false;
                bg.raycastTarget = true;
                bgGo.transform.SetAsFirstSibling();
                if (b != null) b.targetGraphic = bg;
            }

            TMP_Text[] oldTexts = btnGo.GetComponentsInChildren<TMP_Text>(true);
            for (int i = 0; i < oldTexts.Length; i++)
                if (oldTexts[i] != null) oldTexts[i].gameObject.SetActive(false);

            if (template != null)
            {
                GameObject labelGo = UnityEngine.Object.Instantiate(template.gameObject, btnGo.transform, false);
                labelGo.name = "JG_ActionLabel";
                DisableLocalizeComponents(labelGo);
                labelGo.SetActive(true);
                TMP_Text lt = labelGo.GetComponent<TMP_Text>();
                if (lt != null)
                {
                    TMP_FontAsset cjk = _resources?.GetCjkFontInternal();
                    if (cjk != null) lt.font = cjk;
                    lt.text = label;
                    lt.alignment = TextAlignmentOptions.Center;
                    lt.fontSize = 20f;
                    lt.enableWordWrapping = false;
                    lt.overflowMode = TextOverflowModes.Ellipsis;
                    lt.color = Color.white;
                    lt.raycastTarget = false;
                    RectTransform lrt = lt.rectTransform;
                    lrt.anchorMin = Vector2.zero;
                    lrt.anchorMax = Vector2.one;
                    lrt.offsetMin = new Vector2(8f, 0f);
                    lrt.offsetMax = new Vector2(-8f, 0f);
                    lt.transform.SetAsLastSibling();
                    RefreshText(lt);
                }
            }
        }

        private TMP_Text CreateCenteredLabel(NativeRow row, Action onClick)
        {
            if (row?.Title == null || row.Selector == null || row.SelectorComponent == null) return null;
            TMP_Text refLabel = (TMP_Text)row.SelectorComponent.label ?? row.Selector.Find("Text")?.GetComponent<TMP_Text>();
            if (refLabel == null) return null;

            GameObject go = UnityEngine.Object.Instantiate(row.Title.gameObject, row.Selector, false);
            go.name = "JG_Value";
            DisableLocalizeComponents(go);
            go.SetActive(true);
            TMP_Text text = go.GetComponent<TMP_Text>();
            if (text == null) { UnityEngine.Object.Destroy(go); return null; }

            var rt = (RectTransform)go.transform;
            RectTransform refRt = refLabel.rectTransform;
            rt.anchorMin = refRt.anchorMin;
            rt.anchorMax = refRt.anchorMax;
            rt.pivot = refRt.pivot;
            rt.anchoredPosition = refRt.anchoredPosition;
            rt.sizeDelta = new Vector2(refRt.sizeDelta.x, 54f);
            rt.localScale = Vector3.one;

            TMP_FontAsset cjk = _resources?.GetCjkFontInternal();
            text.font = cjk ?? refLabel.font;
            text.fontSize = refLabel.fontSize;
            text.color = refLabel.color;
            text.alignment = TextAlignmentOptions.Center;
            text.enableWordWrapping = false;
            text.raycastTarget = true;
            go.transform.SetAsLastSibling();

            if (onClick != null)
            {
                Button b = go.GetComponent<Button>() ?? go.AddComponent<Button>();
                b.targetGraphic = text;
                b.onClick = new ButtonClickedEvent();
                b.onClick.AddListener(() => onClick());
            }
            row.Value = text;
            return text;
        }

        private TMP_Text CreateValueLabel(NativeRow row, float fontSize, TextAlignmentOptions align)
        {
            if (row?.Title == null || row.Right == null) return null;
            GameObject go = UnityEngine.Object.Instantiate(row.Title.gameObject, row.Right, false);
            go.name = "JG_Value";
            DisableLocalizeComponents(go);
            go.SetActive(true);
            TMP_Text text = go.GetComponent<TMP_Text>();
            if (text == null) { UnityEngine.Object.Destroy(go); return null; }

            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(8f, 4f);
            rt.offsetMax = new Vector2(-8f, -4f);
            rt.localScale = Vector3.one;

            TMP_FontAsset cjk = _resources?.GetCjkFontInternal();
            if (cjk != null) text.font = cjk;
            text.alignment = align;
            text.fontSize = fontSize;
            text.enableWordWrapping = true;
            text.overflowMode = TextOverflowModes.Overflow;
            row.Value = text;
            return text;
        }

        private TMP_Text FindAnyLabelTemplate()
        {
            if (_rows.Count > 0 && _rows[0] != null)
            {
                TMP_Text t = _rows[0].GetComponentInChildren<TMP_Text>(true);
                if (t != null) return t;
            }
            return _rowTemplate == null ? null : _rowTemplate.GetComponentInChildren<TMP_Text>(true);
        }

        private static void BindArrow(Button root, UnityAction action)
        {
            if (root == null) return;
            Image img = root.GetComponent<Image>();
            if (img == null)
            {
                img = root.gameObject.AddComponent<Image>();
                img.color = new Color(0f, 0f, 0f, 0f);
                img.raycastTarget = true;
            }
            root.targetGraphic = img;
            root.onClick = new ButtonClickedEvent();
            root.onClick.AddListener(action);
            Button[] children = root.GetComponentsInChildren<Button>(true);
            for (int i = 0; i < children.Length; i++)
            {
                if (children[i] != null && children[i] != root)
                {
                    children[i].onClick = new ButtonClickedEvent();
                    children[i].onClick.AddListener(action);
                }
            }
        }

        private static void DisableLocalizeComponents(GameObject go)
        {
            Component[] comps = go.GetComponents<Component>();
            for (int i = 0; i < comps.Length; i++)
            {
                if (comps[i] is Behaviour b && comps[i].GetType().Name == "Localize")
                    b.enabled = false;
            }
        }

        private static void RefreshText(TMP_Text t)
        {
            if (t == null) return;
            t.SetAllDirty();
            t.ForceMeshUpdate(false, false);
        }
    }
}
