# Just Gone 源码复用矩阵（SOURCE_REUSE_MATRIX）

> 技术来源基线：《围象棋》v0.10.10.0（`F:\JustGo_WeiXiangQi\audit\P4_FIX_01010\release-manifest.json`，SHA-256: `696EAE9DC56B38064DC85AEA324FD9D5BFE4AFBF7BF8AF77A081F05B57A3E2AE`，共 58 个生产编译源 + 正式部署工具链）。
>
> 本文档严格依据 `source-baseline.json` 中的正式生产清单及 `P4_FULL_PROJECT_AUDIT_0108.md`、`P4_FIX_BATCH1_0109.md`、`P4_FIX_BATCH2_01010.md` 建立，不包含任何 v0.9.5 历史构建目录、反编译副本或废弃实验代码。

---

## 一、四类提取策略总览

| 分类 | 定义 | 数量 | 处置方式 |
| --- | --- | --- | --- |
| **第一类：可直接复用** | 已与具体棋类规则无关、经实机/单测验证的成熟基础设施 | 8 项（源/工具） | 保留核心算法与契约，调整命名空间至 `JustGone`，不作无意义重写 |
| **第二类：经过解耦即可复用** | 底层能力通用，但当前直接引用 `WxqSession` / `WxqMatch` / `WxqPlugin` / `WxqState` | 14 项（源） | 剥离围象棋规则依赖，抽象为多 MOD 共享的公共服务与注册表 |
| **第三类：仅供技术参考** | 高度绑定围象棋流程，但包含已验证的 Just Go 集成模式与边界条件 | 7 个源 | 不直接复制，仅提取其经过实机验证的设计不变量作为 API 规格依据 |
| **第四类：围象棋独有业务逻辑** | 三级征兵、围象混合棋规、资源经济、胜负口径、AI Prompt 与调度、历史兼容补丁 | 29 个源 | **严禁进入 Just Gone 核心框架**；保留在《围象棋》原工程 |

---

## 二、五大能力域可复用组件逐项评估（第一类 + 第二类）

### A. 游戏集成（Game Integration）

| 组件 | 分类 | 来源文件与方法 | 已有功能 | 依赖的游戏内部类型 | 是否依赖围象棋规则 | 已有实机验证证据 | 能否直接复用 | 解耦改造要点 | 建议公开 API | 对原版及其他 MOD 影响 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| **BepInEx 插件初始化与定向补丁回收** | 第二类 | `src/JustGoWeiXiangQiP4/Plugin.cs`: `Awake()` (L159-L185) | `new Harmony(guid)`、`PatchAll`、失败先关门禁再 `harmony.UnpatchSelf()` 定向回收 | `BepInEx.BaseUnityPlugin`, `HarmonyLib.Harmony`, `UnityEngine.Application` | 否（仅配置项绑定属围象棋） | v0.10.10 隔离 Unity 异常注入实机验证（P4 10 个补丁归零，其他 owner 补丁完好） | 解耦后复用 | 剥离围象棋调试配置项与 P1/P2 兼容调用，保留失败回滚契约 | `JustGonePlugin`, `JustGoneApi.IsReady`, `JustGoneApi.RegisterMod(...)` | 仅卸载 `dev.justgone.framework` 自身补丁，不干扰原版及《围象棋》 |
| **多模式菜单路由决策器** | 第二类 | `shared_p4/WxqCompatGuards.cs`: `WxqMenuRoute.Decide` / `EffectiveModIndex` | 保护原版索引 `[0, 2)` 永远放行，区分加载中/禁用/未注册索引 | 无（纯 C#） | 轻微（单 MOD 固定 `modIndex=2`） | P4 1997 单测 + 十轮导航实机验收 | 解耦后复用 | 从单 `modIndex` 泛化为 `MenuRouteTable`（按“标题 + 动态分配索引”精确匹配已注册 MOD；未注册索引返回 `PassThrough` 而非强制阻断，确保与《围象棋》索引 2 共存） | `MenuRouteTable.Decide(...)`, `JustGoneApi.Menu.RegisterMode(...)` | 放行原版 0/1 与第三方（如围象棋索引 2），仅拦截 Just Gone 已注册模式 |
| **会话失效与场景卸载守卫** | 第一类 | `shared_p4/WxqCompatGuards.cs`: `WxqSessionGuard.Decide` | 区分“正常场景卸载”与“场景仍在运行但 `GameManager` 异常丢失”，提供 1.5s 宽限 | 无（纯 C#） | 否 | P4 单测 + `InjectGmLossAfterSeconds` 实机验证 | **可直接复用** | 迁移为 `JustGone.Core.SessionGuard`，支持任意目标场景名 | `SessionGuard.Decide(...)`（Core 公共 API + `JgBootstrap` 内置守卫） | 无副作用，避免正常退回主菜单时误触发 Failsafe |
| **统一 Harmony 补丁协调层** | 第二类 | `src/JustGoWeiXiangQiP4/Patches.cs`: 10 个补丁类 | 拦截练习模式启动、屏蔽/接管输入与快捷键、代理 Undo/Restart/Save、每帧恢复按钮状态、菜单内部滚动滚轮吸收 | `TitleManager`, `GameManager`, `MouseWheel`, `Michsky.UI.ModernUIPack.HorizontalSelector` | 是（直接读写 `WxqState` / `WxqSession.Current` / `WxqMenuConfigPanel`） | v0.10.6 十轮往返、v0.10.8 菜单滚轮验收、v0.10.10 回收实机验证 | 解耦后复用 | 1) `LoadLocalMultiMatch` 使用 `[HarmonyPriority(Priority.High)]`，仅拦截 Just Gone 注册模式，其余一律返回 `true` 交还围象棋与原版；2) 其余补丁仅在 Just Gone 活动模式或锁生效时拦截 | 内部由 `JgPatches` 统一管理，对外暴露 `Modes` / `Input` / `Menu` / `UI` / `Events` API（第三方 MOD 无需自写 Harmony Patch） | **关键共存点**：完全不抢占原版 0/1 与围象棋索引 2；无 Just Gone 活动模式/锁时所有游戏内 Hook 零开销透传 |
| **常驻驱动与 GameManager 就绪等待** | 第二类 | `src/JustGoWeiXiangQiP4/WxqBootstrap.cs`: `EnsureCreated`, `OnSceneLoaded`, `AttachWhenReady`, `ReturnToMenuRoutine` | `DontDestroyOnLoad` 宿主、失败时清理并恢复 `runInBackground`、监听 `SceneManager.sceneLoaded`、等待 `GameManager.m_Instance` 与私有字段 `isGameStarted == true`、失败延迟安全返回主菜单（含 F10/Esc 手动兜底） | `GameManager` (`m_Instance`, 私有 `isGameStarted`/`gameMode`/`boardSize`/`gameEnvironment`), `GoEnum.*`, `SceneManager` | 是（硬编码 `Chinese_Garden`、`Local_Practice`、19 路与 `WxqSession`） | v0.10.10 初始化回滚实机验收 + 故障注入验证 | 解耦后复用 | 泛化为 `JgBootstrap`：监听所有场景、支持任意已注册模式对应的目标场景/尺寸/环境、分发 `SceneLoaded` / `GameReady` 事件、托管每帧更新与输入派发 | `JustGoneApi.Scene`, `JustGoneApi.Game`, `JustGoneApi.Modes` | 仅在目标场景加载且就绪后触发事件；初始化失败自动回滚对象与 `runInBackground` |
| **场景启动器（含原版加载屏与音乐淡出）** | 第二类 | `src/JustGoWeiXiangQiP4/WxqLauncher.cs`: `TryLaunch` | 启动冷却防重入、`GameMusic.gameMusicInstance.TriggerFadeOut()`、`LoadingScreenManager.m_Instance.LoadScene(...)` 及 `SceneManager.LoadScene` 回退、失败回滚 ES3 | `GameMusic`, `Michsky.LSS.LoadingScreenManager`, `SceneManager` | 是（硬编码 7/3/0/19 与 `WxqState.Armed`） | 十轮主菜单→棋盘往返实机验收 | 解耦后复用 | 泛化为 `SceneService.LoadScene(...)` 与 `GameModeService.TryLaunchMode(...)`，支持 4 个原生棋盘场景及自定义 ES3 启动参数 | `JustGoneApi.Scene.LoadScene(...)`, `JustGoneApi.Modes.TryLaunchMode(...)` | 优先走原版加载屏，失败时自动恢复 ES3 快照 |
| **统一异常收尾（Failsafe）** | 第二类 | `src/JustGoWeiXiangQiP4/WxqFailsafe.cs`: `Abort`, `Clear` | 防重入异常中止：锁定原版输入 → 销毁活动会话对象 → CAS 恢复 ES3 → 提示并自动返回主菜单 | 间接依赖 `GameManager.BackToMainScene` | 是（引用 `WxqState`/`WxqSession`） | `InjectFailure` (`timeout`/`prefab`/`spawn`) 与 `InjectGmLoss` 实机验证 | 解耦后复用 | 泛化为 `JgFailsafe.Abort(stage, reason)`，回调活动 MOD 的清理委托并清理该 MOD 托管资源 | `JustGoneApi.Modes.AbortActiveMode(...)` | 仅在 Just Gone 自定义模式发生致命初始化/运行时故障时激活，返回主菜单自动解锁 |
| **原版存档 ES3 事务保护** | 第二类 | `src/JustGoWeiXiangQiP4/WxqEs3Guard.cs` + `shared_p4/WxqTxn.cs` | 读取旧键 → 复制 `SaveFileOfficial_beta.es3` 并 SHA-256 校验 → 写 `txn.json` (`prepared`/`applying`/`committed`) → 失败或退出时 Compare-And-Set 逐键回滚 → 启动时扫描未完成事务对账 | `Utils.LoadGameData`, `Utils.SaveGameData`, `Constants.SAVE_FILE_NAME`, `Constants.IS_SAVED_GAME_LOADED` | 轻微（硬编码 4 个目标值 `7/3/0/false` 与备份目录名） | P4 事务单测 + `TestEs3FailureOnce` + v0.10.9 隔离路径实机验证 | 解耦后复用 | 拆分为纯逻辑 `SaveTransactionRules`（泛化到任意键数 `totalKeys`）与运行时 `SaveGuardService`，允许任意启动参数 `(GameMode, Environment, BoardSizeIndex, IsSavedLoaded)` 事务写入与 CAS 回滚 | `JustGoneApi.Saves.BeginLaunchOverride(...)`, `RestoreLastSnapshotSafe()`, `ScanIncompleteTransactions()` | 绝不覆盖整份玩家存档，CAS 保护玩家新进度，备份隔离在 `BepInEx/config/dev.justgone.framework/es3-guard/` |

---

### B. 棋盘与棋子渲染能力（Board, Piece & Rendering）

| 组件 | 分类 | 来源文件与方法 | 已有功能 | 依赖的游戏内部类型 | 是否依赖围象棋规则 | 已有实机验证证据 | 能否直接复用 | 解耦改造要点 | 建议公开 API | 对原版及其他 MOD 影响 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| **棋盘识别、坐标转换与鼠标拾取** | 第二类 | `WxqStoneView.cs`: `LogicalCellToLocal`；`WxqSession.cs`: `UpdateHover`, `TryGetBoardScreenCenter`, `CleanVanillaBoardStones` | `GoUtils.ConvertPointToBoardPosition` / `ConvertBoardPositionToPoint`、`Constants.GO_BOARD_LAYER` 射线拾取、逻辑格↔局部/世界/屏幕坐标换算、棋盘中心屏幕投影、清理原版练习残留棋子 | `GameManager` (`StonePanel`, 私有 `boardSize`/`gameEnvironment`), `GoUtils`, `GoEnum.GameEnvironment`, `Point`, `Constants.GO_BOARD_LAYER` | 轻微（硬编码 `Chinese_Garden` 与 `19`） | P2/P3/P4 实机点击、悬停、落子、全分辨率居中验证 | 解耦后复用 | 动态从 `GameManager` 读取当前 `boardSize`（9/13/19）与 `gameEnvironment`（4 种环境），提供完整的只读查询与坐标换算服务 | `JustGoneApi.Board` (`IsAvailable`, `BoardSize`, `Environment`, `CellToLocal`, `CellToWorld`, `TryCellToScreen`, `TryScreenToCell`, `TryRayToCell`, `HoveredCell`, `TryGetBoardScreenCenter`, `ClearVanillaStones`) | 只读操作零入侵；`ClearVanillaStones` 保留 `JG_*` 与 `WXQ_*` 图层 |
| **原生棋子模型复用与动画视图** | 第二类 | `src/JustGoWeiXiangQiP4/WxqStoneView.cs`: `SpawnVisual`, `AnimateDrop`, `AnimateMove`, `ApplyRedTint`, `ApplyPieceVisuals`, `SetSelected`, `PlayDropSound`, `Clear` | 实例化 `BlackStonePrefab`/`WhiteStonePrefab` 到独立子节点下、自定义材质染色、复用 `GoStone.MoveNumberText` 渲染任意字形/标签、缩放与自然摆放抖动、`iTween` 落子与抬升移动动画、`StoneAnimator` 摇晃、`PieceDropAudios` 延迟落子音效、`LastMoveCircle` 选中环、材质/对象完整回收 | `GameManager` (`BlackStonePrefab`, `WhiteStonePrefab`, `StonePanel`, `PieceDropAudios`, 私有 `soundEffectOriginalVolume`), `GoStone` (`StoneAnimator`, `LastMove`, `LastMoveCircle`, `MoveNumberText`), `iTween` | 是（硬编码 `WxqPiece`/`WxqSide`/`WxqPieceCategory`/`WxqGlyphProvider`） | P4 全套落子、移动、提子、悔棋、重开实机验证（视图一致性 0 缺陷） | 解耦后复用 | 泛化为 `PieceVisualSpec` 与 `IPieceLayer`（每个 MOD 可拥有独立命名的棋子图层，互不污染、一键销毁） | `JustGoneApi.Pieces.CreateLayer(mod, name)` → `Spawn`, `Move`, `UpdateVisual`, `Remove`, `SetSelected`, `PlayDropSound`, `Clear` | 不触碰原版 `goBoard`/`stones[,]`/`allMoves`，材质实例随棋子销毁自动 `Destroy` |
| **纯逻辑视图账本与 Delta 计划器** | 第二类 | `shared_p4/WxqViewLedger.cs` + `shared_p4/WxqViewPlan.cs` | 按确定性顺序 `Remove → Move → Add → Update` 规划视图操作，按 `PieceId` 与 `BoardCoord` 双向索引并核对逻辑/视图一致性 | 无（纯 C#） | 是（绑定 `WxqPiece` 与固定 19 路） | P4 单测 + 实机视图一致性校验 | 解耦后复用 | 泛化为 `PieceLedger<TData>` 与 `PieceDeltaOp<TData>`，支持任意棋盘尺寸与泛型载荷 | `JustGone.Core.PieceLedger<TData>` | 纯数据结构，零运行时冲突 |
| **格点高亮与 3D 标记** | 第二类 | `src/JustGoWeiXiangQiP4/WxqMarkers.cs`: `AddMarker`, `Clear` | 在棋盘格点表面按格距比例生成无碰撞体圆柱标记（`Unlit/Color` 材质、禁阴影）、支持自定义颜色/直径倍率/高度倍率、统一回收材质与对象 | 依赖 `BoardService` 坐标与 `StonePanel` 父节点 | 是（`ShowMoves`/`ShowRecruits` 读取 `WxqAction`） | 移动/吃子/越子/炮打/征兵高亮实机验证 | 解耦后复用 | 抽象为 `IMarkerLayer`（每 MOD 独立图层），提供 `SetMarker` / `RemoveMarker` / `Clear` | `JustGoneApi.Rendering.CreateMarkerLayer(mod, name)` | 独立节点与材质列表，销毁时零泄漏 |
| **原生 N×N 领地网格与原生悬停幽灵棋子** | 第二类 | `src/JustGoWeiXiangQiP4/WxqNativeOwnershipView.cs` + `WxqSession.cs`: `UpdateNativePreview`/`HideNativePreview` | 克隆 `GameManager.OwnershipPrefab` 构建 `Size×Size` 网格覆盖层（独立对象池，不写原版 `OwnershipMap`）；控制 `BlackMouseOverStone`/`WhiteMouseOverStone` 悬停预览位置 | `GameManager` (`OwnershipPrefab`, `OwnershipPanel`, `BlackMouseOverStone`, `WhiteMouseOverStone`) | 是（`Refresh` 读取 `WxqFormalState`） | v0.10.x 领地可视化与悬停预览实机验收 | 解耦后复用 | 泛化为 `IGridOverlayLayer` 与 `RenderingService.ShowHoverGhost` / `HideHoverGhost` | `JustGoneApi.Rendering.CreateGridOverlay(mod, name)`, `ShowHoverGhost`, `HideHoverGhost` | 退出时恢复 `OwnershipPanel` 原始激活状态，销毁全部克隆体 |
| **3D 棋盘旁实体陈列与射线/屏幕回退拾取** | 第二类 | `src/JustGoWeiXiangQiP4/WxqRecruitShelf.cs`: 布局计算、`BoxCollider`、遮挡优先射线检测 + 屏幕距离回退 | 在棋盘右侧按棋盘坐标系布置 3D 棋子实体，支持点击拾取（先判非陈列物体遮挡，再判命中/屏幕像素容差）、高亮缩放与透明度反馈 | `GameManager` 棋子模板、`Physics.RaycastAll`、`Camera.main` | 是（硬编码 6 兵种与红黑两套） | v0.10.x 三级征兵 3D 陈列点击验收 | 解耦后复用 | 抽象为 `IDisplayShelf`（MOD 自定义条目 ID、相对棋盘锚点偏移、视觉规格、选中/禁用态及射线拾取） | `JustGoneApi.Pieces.CreateDisplayShelf(mod, name)` | 独立 `GameObject` 与 `Material` 实例，支持随会话自动销毁 |

---

### C. 原生 UI 能力（Menu & Native UI）

| 组件 | 分类 | 来源文件与方法 | 已有功能 | 依赖的游戏内部类型 | 是否依赖围象棋规则 | 已有实机验证证据 | 能否直接复用 | 解耦改造要点 | 建议公开 API | 对原版及其他 MOD 影响 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| **原生模式菜单项注册与 BasicPanel 配置页框架** | 第二类 | `src/JustGoWeiXiangQiP4/WxqMenuConfigPanel.cs`: `CaptureCanonicalGeometryIfPossible`, `EnsureScroll`, `AddNativeScrollbar`, `NewRow`, `ChoiceRow`, `InputRow`, `HeaderRow`, `StatusRow`, `EditorAction`, `ApplyContentAndScroll`, `ShouldAbsorbWheelInternal`, `TeardownNativeUi`, `EnterBasicPageRoutine` | 在 `ModeSelection_LM` 注册模式项；捕获 `BasicPanel/List` 设计态几何基线；包裹 `ScrollRect` + 克隆原生 `Scrollbar` + 滚轮边界吸收；等待 `Animator`/`CanvasGroup` 稳定；克隆原生行/输入框/蓝色按钮与右下操作区；离开时无损还原原版 `List` 与原版模式 0/1 行可见性 | `HorizontalSelector`, `TitleManager`, `MouseWheel`, `TMP_Text`, `TMP_InputField`, `ScrollRect`, `Scrollbar`, `Button`, `ContentSizeFitter`, `VerticalLayoutGroup`, `Animator`, `CanvasGroup` | 是（混合了 `WxqAiSettingsStore` / API 测试逻辑与固定索引 `2`） | v0.9.3 布局路径无关性验收 + v0.10.8 四行表单与全屏/窗口验收 | 解耦后复用 | 拆出 `JgMenuHost` 与 `IMenuPageBuilder`：支持多 MOD 通过 `Menu.RegisterMode` 向 `ModeSelection_LM` 追加独立模式项（`CreateNewItem`），由框架统一处理几何基线、滚动、滚轮吸收与退出复原 | `JustGoneApi.Menu.RegisterMode(...)`, `IMenuPageBuilder` | **严禁触碰索引 0/1 及非 Just Gone 注册的第三方项（如围象棋索引 2）**；切出时完整 `Teardown` 还原原生 `List` |
| **原生风格局内通知（Toast / Sticky Error）** | 第一类（轻度解耦） | `src/JustGoWeiXiangQiP4/WxqNativeFeedback.cs`: `Build`, `Show`, `ShowError`, `ClearError`, `Update` | 克隆 `GameManager.WarningMessage`（回退 `ResultText`），禁用克隆体 `Animator` 与 `raycastTarget`，支持短时淡出通知、防刷屏去重、常驻错误不被普通信息覆盖 | `GameManager` (`WarningMessage`, `ResultText`), `Canvas`, `CanvasGroup`, `TMP_Text` | 否（仅 `Attach(WxqSession, ...)` 签名耦合） | v0.10.x 全流程通知与错误显示实机验证 | **轻度解耦直接复用** | 改为由 `UiService` 惰性挂载到当前场景 `Canvas`，向所有 MOD 暴露 `ShowToast` / `ShowStickyError` / `ClearStickyError` | `JustGoneApi.UI.ShowToast`, `ShowStickyError`, `ClearStickyError` | 单例非阻塞浮层，不拦截点击 |
| **左侧自适应镜像折叠侧栏（ScrollRect）** | 第二类 | `src/JustGoWeiXiangQiP4/WxqRecruitRegionPanel.cs`: 构造、`AlignToHistory`, `AddButton`, `SetInstruction`, `Dispose` | 读取右侧原版 `PracticeScrollRect` 世界四角并按画布水平镜像对齐，构建可折叠标题栏 + `RectMask2D`/`ScrollRect`/`VerticalLayoutGroup`/`ContentSizeFitter` 滚动列表 + 底部提示条 | `GameManager` (`PracticeMovePanel`, `PracticeScrollRect`, `ResultText`), `Canvas`, `ScrollRect`, `TMP_Text` | 是（硬编码征兵区域渲染） | v0.10.x 征兵区域侧栏实机验收 | 解耦后复用 | 泛化为 `ISidebarPanel`（自定义标题、左侧镜像/自由锚点、动态行项与高亮、底部提示、折叠切换、销毁回收） | `JustGoneApi.UI.CreateSidebarPanel(mod, options)` | 每面板独立根对象，通过 `ModContext` 自动销毁 |
| **原版 Practice 按钮接管、棋谱列表、终局面板与系统保存框** | 第二类 | `src/JustGoWeiXiangQiP4/WxqNativeUiAdapter.cs`: `Wire`, `RefreshButtons`, `RebuildHistory`, `ShowGameOver`, `HideGameOver`, `SaveWithNativeDialog`, `WxqResultLayout` | 替换 `UndoButton`/`SaveButton`/`RestartButton` 的 `onClick`；每帧 `UpdateButtonsInPanels` Postfix 同步按钮可用态；克隆 `PracticeMovePrefab` 渲染自定义历史项；复用并铺满原版 `GameOverPanel` + 随棋盘屏幕中心自适应缩放；调用 `Crosstales.FB.FileBrowser.Instance.SaveFileAsync` 弹出原生保存对话框（含目录回退） | `GameManager`, `PracticeMove`, `Crosstales.FB.FileBrowser` | 是（绑定 `WxqSession` / `WxqGameResult` / `.wxq`） | P4 全流程 E2E + 历史浏览 + 终局面板 + 棋谱导出实机验收 | 解耦后复用 | 拆分为独立 UI 能力：`SetPracticeButtonStates`、`SetPracticeHistory`、`ShowGameOver`/`HideGameOver`、`ShowSaveFileDialog` | `JustGoneApi.UI` | 仅在自定义模式激活或显式调用时生效；退出场景自动恢复 |
| **CJK / TMP 字体资产发现与系统动态字体回退** | 第一类 | `src/JustGoWeiXiangQiP4/WxqGlyphProvider.cs`: `Get`, `CanRender` | 扫描已加载 `TMP_FontAsset` 并检测目标字符集；缺失时按优先级从 OS 字体创建动态 `TMP_FontAsset` 并缓存 | `TMPro.TMP_FontAsset`, `UnityEngine.Font`, `Resources` | 轻微（固定校验 9 个象棋汉字） | P4 全兵种汉字渲染实机验证 | **可直接复用（泛化字符集）** | 参数化为 `EnsureFontForCharacters(string)` 与内部 `FindOrCreateFontInternal`，剔除象棋字形映射表 | `JustGoneApi.Resources.HasCjkFont`, `EnsureFontForCharacters` | 只读扫描与按需创建缓存字体，零副作用 |

---

### D. 输入、工具与资源管理（Input, Utilities & Resources）

| 组件 | 分类 | 来源文件与方法 | 已有功能 | 依赖的游戏内部类型 | 是否依赖围象棋规则 | 已有实机验证证据 | 能否直接复用 | 解耦改造要点 | 建议公开 API | 对原版及其他 MOD 影响 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| **UI 指针遮挡检测与输入焦点检测** | 第一类 | `WxqSession.cs`: `PointerOverUnityUi()` (L1579-L1599)、焦点检查 (L1489-L1492) | `EventSystem.current.RaycastAll` 判 `Selectable`/`ScrollRect`；判焦点是否在 `InputField`/`TMP_InputField` | `EventSystem`, `Selectable`, `ScrollRect`, `InputField`, `TMP_InputField` | 否 | P4 实机验证 | **可直接复用** | 提取至 `InputService` + 引用计数式 `InputLockRegistry` | `JustGoneApi.Input` | 零侵入，多 MOD 可安全共享 |
| **安全原子配置文件写入** | 第一类 | `shared_p4/WxqAtomicConfigFile.cs`: `Write` | 同目录独占临时文件 → `Flush(true)` → `File.Replace`/`File.Move`，异常删临时文件 | 无（纯 .NET） | 否 | v0.10.9 隔离锁文件故障注入实机验证（WXQ-P1-001） | **可直接复用** | 迁移为 `JustGone.Core.AtomicFileWriter.WriteAllTextAtomic` | `AtomicFileWriter.WriteAllTextAtomic` | 保护 MOD 配置文件不损坏 |
| **零依赖 MiniJson 读写器** | 第一类 | `shared_p4/WxqMiniJson.cs`: `WxqJson` | 零外部依赖 JSON 解析/序列化 | 无（纯 C#） | 否 | P4 全量单测 + 生产运行 | **可直接复用** | 迁移为 `JustGone.Core.MiniJson` | `MiniJson.Parse`, `TryParse`, `Serialize` | 无第三方依赖冲突 |
| **异常安全日志封装** | 第一类 | `src/JustGoWeiXiangQiP4/WxqLog.cs` | 吞掉日志后端异常的统一日志出口 | `BepInEx.Logging.ManualLogSource` | 否 | P4 全周期使用 | **可直接复用** | 迁移为 `JgLog` + `ModContext.Log` | `ModContext.Log` | 无副作用 |

---

### E. 开发与发布基础设施（Build, Deployment & Testing）

| 组件 | 分类 | 来源文件与方法 | 已有功能 | 依赖的游戏内部类型 | 是否依赖围象棋规则 | 已有实机验证证据 | 能否直接复用 | 解耦改造要点 | 建议工具 | 对原版及其他 MOD 影响 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| **net472 确定性构建配置** | 第一类 | `Directory.Build.props`, `JustGoWeiXiangQiP4.csproj`, `tools/build-p4.ps1` | 固定游戏引用路径、`Private=false`、`LangVersion 8.0`、`Deterministic=true`、`CodePage 65001`、单测+编译+SHA-256 输出 | Just Go `Managed` & `BepInEx/core` | 轻微 | v0.10.10 生产编译验证 | **可直接复用** | 适配为 `F:\JustGone\Directory.Build.props` 与 `tools\build.ps1`，增加示例 MOD 零私有引用验证 | `tools/build.ps1` | 仅在 `F:\JustGone` 构建，不触碰游戏目录 |
| **事务性插件安装、校验与回滚引擎** | 第一类 | `tools/p4-deployment.ps1`, `tools/p4-install.ps1` | 运行中进程拦截、符号链接防护、排他锁、版本+SHA-256 身份门禁、暂存校验、`install-receipt.json`、归档、安装后复核、异常自动回滚、开发模式强制显式 `-GameDir` | BepInEx 目录结构 | 轻微（硬编码 `JustGoWeiXiangQiP4`） | v0.10.10 临时目录 27 条事务断言通过（WXQ-P2-003） | **可直接复用** | 泛化为 `Invoke-JustGoneDeployment`（归档目录 `BepInEx/JustGoneDeploymentHistory`，与围象棋完全隔离） | `tools/deploy-lib.ps1`, `tools/install.ps1`, `tools/test-deployment.ps1` | 严禁动 `JustGoWeiXiangQiP4` 目录 |

---

## 三、第三类与第四类源文件完整清点（58/58 全覆盖）

### 第三类：仅供技术参考（7 个源）
1. `src/JustGoWeiXiangQiP4/WxqSession.cs` — 已提取棋盘拾取、UI 挂接与清理逻辑，其余作为会话模式参考。
2. `src/JustGoWeiXiangQiP4/WxqState.cs` — 五态跨场景会话状态机参考。
3. `src/JustGoWeiXiangQiP4/WxqUiDiagnostics.cs` — 主菜单翻页动画与布局几何诊断参考。
4. `src/JustGoWeiXiangQiP4/WxqUiFlow.cs` — 自动化 UI 往返驱动参考。
5. `src/JustGoWeiXiangQiP4/WxqFailInject.cs` — 可控故障注入开关参考。
6. `shared_p4/WxqInteractionRules.cs` — 纯逻辑输入门禁与滚轮决策参考。
7. `src/JustGoWeiXiangQiP4/WxqMenuRuntime.cs` — 启动前配置校验契约参考。

### 第四类：围象棋独有业务逻辑（29 个源，严禁进入 Just Gone 核心）
- **核心棋规与正式层（18 个）**：`WxqCoreTypes.cs`, `WxqActions.cs`, `WxqMatch.cs`, `WxqMatchEngine.cs`, `WxqFormalTypes.cs`, `WxqFormalMovement.cs`, `WxqFormalCapture.cs`, `WxqFormalOutcome.cs`, `WxqCaptureResolver.cs`, `WxqLegalActions.cs`, `WxqLibertyAnalyzer.cs`, `WxqQuadrantPolicy.cs`, `WxqRecruitTerritory.cs`, `WxqRecruitRegionIndex.cs`, `WxqRepetition.cs`, `WxqReplayRecord.cs`, `WxqRulePolicies.cs`, `WxqStateLayers.cs`
- **外部/内置 AI 与凭据系统（8 个）**：`WxqAgents.cs`, `WxqAiAgentContexts.cs`, `WxqAiConfig.cs`, `WxqApiProtocol.cs`, `WxqApiTransport.cs`, `WxqCredentialStore.cs`, `WxqAiRuntime.cs`, `WxqApiClient.cs`
- **围象棋历史插件兼容补丁（2 个）**：`WxqP1Compat.cs`, `WxqLegacyOverlayHider.cs`
- **自动生成程序集信息（1 个）**：`generated/AssemblyInfo.cs`
- **核对**：第一/二类 22 个生产源 + 第三类 7 个 + 第四类 29 个 = **58/58 全部清点完毕**。
