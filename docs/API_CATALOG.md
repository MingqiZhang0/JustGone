# Just Gone 公共能力与接口清单（API_CATALOG）

> **设计原则**：
> 1. 本文档列出的每一项公共接口均对应 `JustGone.dll` 中的**真实实现**，不包含空壳或未落地的占位 API。
> 2. 公共签名**完全隔离 `Assembly-CSharp` 私有类型**（如 `GameManager`、`TitleManager`、`GoEnum`、`GoStone`、`HorizontalSelector`），第三方 MOD 无需引用 `Assembly-CSharp.dll` 或 `0Harmony.dll`。
> 3. 所有资源创建、事件订阅、模式与菜单注册均可经由 `ModContext` 绑定所有权，支持多 MOD 并存与一键回收。

---

## 1. 入口与 MOD 作用域（`JustGone`）

### `JustGoneApi`（静态公共门面）

| 成员 | 类型 | 说明 |
| --- | --- | --- |
| `FrameworkGuid` | `const string` | `"dev.justgone.framework"` |
| `FrameworkVersion` | `const string` | `"0.1.0"` |
| `IsReady` | `bool` | 框架 Harmony 补丁与 Bootstrap 是否完整就绪 |
| `RegisterMod(string id, string displayName, string version)` | `ModContext` | 注册独立 MOD 上下文；重复 ID 抛出明确异常 |
| `TryGetMod(string id, out ModContext context)` | `bool` | 查询已注册 MOD 上下文 |
| `RegisteredMods` | `IReadOnlyList<ModInfo>` | 已注册 MOD 只读快照 |
| `Game` | `IGameService` | 游戏状态、暂停、相机与返回主菜单 |
| `Scene` | `ISceneService` | 当前场景、对局就绪查询与安全切场景 |
| `Board` | `IBoardService` | 棋盘尺寸、坐标转换、鼠标拾取与悬停 |
| `Pieces` | `IPieceService` | 原生棋子模型复用、染色/字形/动画图层与 3D 展示架 |
| `Rendering` | `IRenderingService` | 格点 3D 高亮标记、原生领地网格、悬停幽灵棋子 |
| `Input` | `IInputService` | 指针 UI 检测、焦点检测、多 MOD 输入锁与菜单滚轮吸收 |
| `Menu` | `IMenuService` | 主菜单模式项注册与原生配置页构建 |
| `UI` | `IUiService` | 原生通知、镜像折叠侧栏、Practice 按钮/历史/终局面板与原生保存框 |
| `Modes` | `IGameModeService` | 自定义游戏模式注册、启动、活动会话管理与 Failsafe 中止 |
| `Events` | `IEventService` | 场景、对局就绪、棋盘交互、输入与原生按钮事件总线 |
| `Resources` | `IResourceService` | CJK/TMP 字体发现与动态创建、原生资源可用性查询与托管对象回收 |
| `Saves` | `ISaveGuardService` | 原版 ES3 存档 CAS 事务覆盖、安全回滚与未完成事务对账 |

### `ModContext`（`IDisposable`）

每个第三方 MOD 的隔离作用域对象：
- `ModInfo Info`：`Id`、`DisplayName`、`Version`
- `IModLogger Log`：带 `[JG:<ModId>]` 前缀的异常安全日志出口（`Info`/`Warn`/`Error`/`Debug`）
- `IDisposable Track(IDisposable resource)`：将任意可释放资源（事件订阅、输入锁、图层、面板、菜单模式）绑定到本 MOD 生命周期
- `T TrackUnityObject<T>(T obj) where T : UnityEngine.Object`：托管 Unity 对象，随 `Dispose()` 销毁
- `void Dispose()`：逆序安全释放本 MOD 全部资源、取消全部事件订阅并注销 MOD

---

## 2. Core 纯逻辑类型（`JustGone.Core`，无 Unity 依赖）

| 类型 | 主要成员 | 来源 |
| --- | --- | --- |
| `BoardCoord` | `X`, `Y`, `Invalid`, `IsValid`, `InBounds(int size)`, `ToKey(int size)`, `FromKey(int key, int size)`, `ToGtp(int size)`, `TryParseGtp(string, int size, out BoardCoord)` | 泛化自 `WxqCell` |
| `BoardSizeKind` | `Nine = 9`, `Thirteen = 13`, `Nineteen = 19` + `ToIndex()` / `FromIndex(int)` | 对应 `GoEnum.GoBoardSize` |
| `GameEnvironmentKind` | `Commentary = 0`, `Tatami = 1`, `MatchRoom = 2`, `ChineseGarden = 3` + `DefaultSceneName()` | 对应 `GoEnum.GameEnvironment` |
| `VanillaGameMode` | `LocalSingle = 0`, `LocalMulti = 1`, `LocalCareerMode = 2`, `LocalReplay = 3`, `LocalTsumego = 4`, `OnlineMultiOgs = 5`, `OnlineMultiSteam = 6`, `LocalPractice = 7` | 对应 `GoEnum.GameMode` |
| `SceneKind` | `Unknown`, `MainMenu`, `ChineseGarden`, `JapaneseRoom`, `CommentaryRoom`, `MatchRoom`, `TutorialJapaneseRoom`, `TutorialChineseGarden` | 对应 `GoEnum.Scene` |
| `StoneBaseStyle` | `Black`, `White` | 原生棋子 Prefab 抽象 |
| `VirtualCameraKind` | `StartCamera1`, `StartCamera2`, `StartCameraReplay`, `EndCameraTsumego`, `GameOver` | 对应 `GoEnum.VirtualCameraType` |
| `MenuRouteTable` / `MenuRouting` | `Register(...)`, `AssignIndex(...)`, `Unregister(...)`, `Decide(...)` | 泛化自 `WxqMenuRoute` |
| `SessionGuard` | `Decide(bool gameManagerAlive, bool expectedScene, bool sceneLoaded, float lostSeconds, float graceSeconds)` | 直接复用 `WxqSessionGuard` |
| `SaveTransactionRules` | `ShouldRestore(...)`, `Reconcile(SaveTxnState, int matchesWritten, int matchesBefore, int other, int totalKeys)` | 泛化自 `WxqTxnRules` |
| `AtomicFileWriter` | `WriteAllTextAtomic(string path, string content, string backupPath = null)` | 直接复用 `WxqAtomicConfigFile` |
| `MiniJson` | `Parse(string)`, `TryParse(string, out object)`, `Serialize(object)` | 直接复用 `WxqJson` |
| `PieceLedger<TData>` / `PieceDeltaPlanner` | 双向 ID/坐标索引、确定性 `Remove → Move → Add → Update` 视图计划与一致性核对 | 泛化自 `WxqViewLedger`/`WxqViewPlan` |

---

## 3. 十二大运行时公共服务详细清单

### 3.1 Game API (`IGameService`)
- `bool IsInMatchScene`：当前是否处于有效棋盘对局场景且 `GameManager` 存活
- `bool IsGameStarted`：原版 `GameManager.Start()` 是否已完成
- `bool IsPaused`：当前对局是否处于暂停态
- `bool IsGameOver`：当前原版终局标记
- `VanillaGameMode CurrentVanillaMode` / `GameEnvironmentKind CurrentEnvironment` / `int CurrentBoardSize`
- `void PauseOrResume()`：切换原版暂停菜单
- `bool TrySetCamera(VirtualCameraKind camera, float transitionSeconds = 1.5f)`：安全切换原版虚拟相机
- `void ReturnToMainMenu()`：优先调用 `GameManager.BackToMainScene()`，失败回退 `SceneManager.LoadScene("MainScene")`

### 3.2 Scene API (`ISceneService`)
- `string CurrentSceneName` / `SceneKind CurrentSceneKind`
- `bool IsMainMenu` / `bool IsBoardScene`
- `bool LoadScene(string sceneName, bool fadeMusic = true, bool preferLoadingScreen = true)`：复用原版 `GameMusic` 淡出与 `LoadingScreenManager` 加载场景（带 `SceneManager` 回退）
- `bool LoadEnvironmentScene(GameEnvironmentKind env, bool fadeMusic = true)`

### 3.3 Board API (`IBoardService`)
- `bool IsAvailable`：棋盘与 `StonePanel` 是否就绪
- `int BoardSize`（9 / 13 / 19）、`GameEnvironmentKind Environment`
- `BoardCoord HoveredCell`：当前鼠标悬停格点（无效时为 `BoardCoord.Invalid`）
- `Vector3 CellToLocal(BoardCoord coord)` / `Vector3 CellToWorld(BoardCoord coord)`
- `bool TryCellToScreen(BoardCoord coord, out Vector2 screenPos)`
- `bool TryScreenToCell(Vector2 screenPos, out BoardCoord coord)`（使用 `Constants.GO_BOARD_LAYER` 射线拾取 + `GoUtils.ConvertBoardPositionToPoint`）
- `bool TryRayToCell(Ray ray, out BoardCoord coord)`
- `bool TryGetBoardScreenCenter(out Vector2 screenCenter)`（供自适应居中 UI 使用）
- `float GetCellSpacingLocal()`：计算相邻两格的局部间距（供标记与 3D 展示缩放）
- `int ClearVanillaStones()`：仅移除原版练习模式下 `StonePanel` 的非托管棋子（保留预览棋子与所有 Just Gone MOD 图层）

### 3.4 Piece API (`IPieceService`)
- `IPieceLayer CreateLayer(ModContext owner, string layerName, bool naturalJitter = true)`
  - `Spawn(int pieceId, BoardCoord coord, PieceVisualSpec spec, bool animateDrop = true)`
  - `Move(int pieceId, BoardCoord targetCoord, bool animateMove = true)`
  - `UpdateVisual(int pieceId, PieceVisualSpec spec)`
  - `Remove(int pieceId)`
  - `SetSelected(int? pieceId)`（切换原版 `GoStone.LastMoveCircle` 选中环）
  - `PlayDropSound()`（复用原版 `PieceDropAudios` 与音量设置）
  - `TryGetPieceCoord(int pieceId, out BoardCoord coord)` / `int Count` / `Clear()` / `Dispose()`
- `PieceVisualSpec`：`BaseStyle`（`Black`/`White`）、`Color? Tint`、`string LabelText`、`Color LabelColor`、`float Scale`、`bool UseCjkFont`
- `IDisplayShelf CreateDisplayShelf(ModContext owner, string shelfName)`（棋盘旁 3D 原生棋子陈列架）：
  - `AddItem(string itemId, PieceVisualSpec spec, Vector3 boardRelativeOffset)`
  - `SetVisible(bool visible)` / `SetItemState(string itemId, bool enabled, bool selected)`
  - `bool TryRaycastItem(Ray ray, out string itemId)`（含遮挡检查与屏幕距离回退）
  - `Clear()` / `Dispose()`

### 3.5 Rendering API (`IRenderingService`)
- `IMarkerLayer CreateMarkerLayer(ModContext owner, string layerName)`
  - `SetMarker(BoardCoord coord, Color color, float diameterScale = 1f, float heightScale = 1f)`
  - `RemoveMarker(BoardCoord coord)` / `Clear()` / `int Count` / `Dispose()`
- `IGridOverlayLayer CreateGridOverlay(ModContext owner, string layerName)`（复用原版 `OwnershipPrefab` 对象池）
  - `SetVisible(bool visible)` / `SetCellColor(BoardCoord coord, Color color)` / `ClearCell(BoardCoord coord)` / `ClearAll()` / `Dispose()`
- `void ShowHoverGhost(StoneBaseStyle style, BoardCoord coord)` / `void HideHoverGhost()`（复用原版 `BlackMouseOverStone`/`WhiteMouseOverStone`）

### 3.6 Input API (`IInputService`)
- `bool IsPointerOverUi`：基于 `EventSystem.current.RaycastAll` 判断是否悬停在 `Selectable` / `ScrollRect` 上
- `bool IsTextInputFocused`：当前焦点是否在 `InputField` / `TMP_InputField`
- `bool IsVanillaInputSuppressed`：当前是否存在任何有效输入锁或抑制原版输入的活动模式
- `IDisposable AcquireVanillaInputLock(ModContext owner, string reason)`：令牌式原版输入抑制（支持多 MOD 引用计数共存，释放即自动解除）
- `IDisposable RegisterMenuWheelAbsorber(ModContext owner, Func<bool> shouldAbsorb)`：注册主菜单滚轮吸收条件

### 3.7 Menu API (`IMenuService`)
- `bool IsMainMenuReady`
- `IMenuModeRegistration RegisterMode(ModContext owner, MenuModeDescriptor descriptor)`
  - `MenuModeDescriptor`：`ModeId`、`DisplayTitle`、`LaunchOptions`（目标环境/棋盘尺寸/底层原版承载模式）、`Func<MenuLaunchValidation> ValidateLaunch`、`Action<IMenuPageBuilder> BuildConfigPage`、`Action OnLaunchRequested`
- `IMenuPageBuilder`（在原版 `FriendsPlay/Content/BasicPanel/List` 内按原生视觉构建配置页，自动处理设计态几何基线、滚动容器、原生滚动条、滚轮边界吸收与离开还原）：
  - `AddChoiceRow(string title, string[] options, int selectedIndex, Action<int> onChanged, Action onValueClicked = null, Func<int, string> formatLabel = null)`
  - `AddInputRow(string title, string initialValue, Action<string> onChanged, string placeholder = null, bool password = false)`
  - `AddHeaderRow(string title, string hintText, Action onClick)`
  - `AddStatusRow(string title, string value)`
  - `AddButtonRow(string title, string[] labels, Action[] actions)`
  - `AddBottomAction(string label, Action onClick, float width = 260f)`
  - `ShowTransientNotice(string message, float durationSeconds = 6f)`
  - `RequestRebuild(bool scrollToTop = false)`
- `void ShowMenuNotice(string message, float durationSeconds = 8f)`

### 3.8 UI API (`IUiService`)
- `void ShowToast(string message, float durationSeconds = 2.8f)`
- `void ShowStickyError(string message)` / `void ClearStickyError()`
- `ISidebarPanel CreateSidebarPanel(ModContext owner, SidebarPanelOptions options)`
  - 镜像对齐右侧原版 `PracticeScrollRect`（或自定义锚点），内置折叠标题栏、`ScrollRect`+`RectMask2D`、行按钮列表、选中高亮与底部提示条
  - `SetVisible(bool)` / `SetTitle(string)` / `SetFooterText(string)` / `SetItems(IReadOnlyList<SidebarItem>)` / `SetSelectedItem(string itemId)` / `ClearItems()` / `Dispose()`
- `void SetPracticeButtonStates(bool? undoInteractable, bool? saveInteractable, bool? restartInteractable)`
- `void SetPracticeHistory(IReadOnlyList<HistoryListItem> items, Action<int> onItemClicked, int selectedIndex = -1)`
- `void ShowGameOver(GameOverViewOptions options)` / `void HideGameOver()`
- `void ShowSaveFileDialog(string defaultFileName, string extension, string content, Action<SaveDialogResult> onCompleted, string fallbackDirectory = null)`

### 3.9 Game Mode API (`IGameModeService`)
- `IReadOnlyList<GameModeRegistrationInfo> RegisteredModes`
- `string ActiveModeId` / `bool HasActiveMode`
- `IGameModeRegistration RegisterGameMode(ModContext owner, GameModeDefinition definition)`
  - 同时完成菜单项注册（`MenuService`）与对局会话接管；
  - `GameModeDefinition`：`ModeId`、`DisplayTitle`、`TargetEnvironment`、`BoardSize`、`SuppressVanillaInput`（默认 `true`）、`ClearVanillaStonesOnStart`（默认 `true`）、`ValidateLaunch`、`BuildMenuConfigPage`、`CreateSession(ModContext owner, ModeSessionContext ctx)`
- `IGameModeSession`（MOD 实现的会话接口）：
  - `void OnStart(ModeSessionContext ctx)`
  - `void OnUpdate(ModeSessionContext ctx, float deltaTime)`
  - `void OnEnd(ModeSessionContext ctx)`
  - 可选：`OnUndoRequested`、`OnRestartRequested`、`OnSaveRequested`、`OnRefreshNativeButtons`
- `bool TryLaunchMode(string modeId, out string error)`
- `void AbortActiveMode(string stage, string reason)`（走统一 Failsafe 安全退出）

### 3.10 Event API (`IEventService`)
所有事件订阅返回 `IDisposable`（可交给 `ModContext.Track` 自动退订），派发器对每个订阅者独立 `try/catch` 隔离异常：
- `OnSceneLoaded(ModContext owner, Action<SceneLoadedEventArgs> handler)`
- `OnGameReady(ModContext owner, Action<GameReadyEventArgs> handler)`
- `OnGameLeaving(ModContext owner, Action<SceneLoadedEventArgs> handler)`
- `OnBoardHoverChanged(ModContext owner, Action<BoardHoverEventArgs> handler)`
- `OnBoardClicked(ModContext owner, Action<BoardClickEventArgs> handler)`
- `OnKeyPressed(ModContext owner, KeyCode key, Action<KeyEventArgs> handler)`
- `OnMouseWheel(ModContext owner, Action<MouseWheelEventArgs> handler)`
- `OnPauseChanged(ModContext owner, Action<PauseChangedEventArgs> handler)`
- `OnNativeAction(ModContext owner, Action<NativeActionEventArgs> handler)`（Undo / Restart / Save）

### 3.11 Resource API (`IResourceService`)
- `TMP_FontAsset GetCjkFont()` / `TMP_FontAsset FindOrCreateFont(string requiredCharacters)`
- `bool HasNativeStonePrefabs` / `bool HasNativeOwnershipPrefab`
- `string GetModConfigDirectory(ModContext owner)`（返回 `BepInEx/config/<modId>` 并自动创建目录）

### 3.12 Save Guard API (`ISaveGuardService`)
- `bool BeginLaunchOverride(SaveLaunchOverrideSpec spec, out string error)`
- `bool RestoreLastSnapshot(out string error)` / `void RestoreLastSnapshotSafe()`
- `int ScanIncompleteTransactions()`
- `string BackupDirectory`
