# Just Gone — Phase 2 真实 Unity 集成与多 MOD 共存验收报告

> **验收日期**：2026-11-24  
> **框架版本**：`JustGone v0.1.0`（net472）  
> **共存基线**：《围象棋》`v0.10.10.0`（`JustGoWeiXiangQiP4.dll`，SHA-256 `696EAE9DC56B38064DC85AEA324FD9D5BFE4AFBF7BF8AF77A081F05B57A3E2AE`）+ 原有历史插件 `P0 v0.4.4` / `P2 v0.5.0`  
> **部署边界**：本轮**仅安装于独立隔离副本** `F:\JustGone\sandbox\JustGo_Isolated`，**未向正式游戏目录部署**，未修改《围象棋》源码或正式安装件，未触碰真实用户存档与 API Key。

---

## 1. 隔离 Unity 环境与存档安全核验

- **测试账户与隔离方式**：本轮在当前标准 Windows 用户（`pc`，无管理员权限访问 `CodexSandboxOffline` 账户目录）下，使用独立游戏副本 `F:\JustGone\sandbox\JustGo_Isolated`（只读 `JustGo_Data`/`MonoBleedingEdge` 目录联接 + 独立 `BepInEx/core`、`BepInEx/plugins`、空白 `BepInEx/config`、测试专用 `steam_appid.txt = 1862520`）。
- **存档与配置路径隔离证据**：
  - 测试专用 `JustGone.Phase2Harness.dll`（仅位于 `sandbox/`，严禁进入发行包）在 `Awake()` 阶段将 `Application.persistentDataPath` 重定向至 `F:\JustGone\sandbox\IsolatedUserData\PersistentData`，隔离副本内已实际生成 `SaveFileOfficial_beta.es3`；
  - Just Gone 的 SaveGuard 备份与事务日志独立落盘于 `F:\JustGone\sandbox\JustGo_Isolated\BepInEx\config\dev.justgone.framework\es3-guard\20261124_081929_16598db5.txn.json`（实测终态 `"state":"rolledback"`，含 `.es3.bak` 及 SHA-256），与围象棋配置目录 `local.weixiangqi.justgo.p4` 完全分离。
- **原版用户存档完好性**：运行前后比对 `C:\Users\pc\AppData\LocalLow\Studio Amateur\JustGo\SaveFileOfficial_beta.es3`，字节数（`14,864 B`）与最后修改时间完全不变；未读取任何真实 API Key，未发起任何外部模型 API 请求。
- **《围象棋》正式安装件完好性**：`F:\Steam\steamapps\common\Just Go\BepInEx\plugins\JustGoWeiXiangQiP4\JustGoWeiXiangQiP4.dll` 哈希仍与 v0.10.10.0 基线完全一致。
- **自动化报告总计核对（[phase2_report.txt](file:///F:/JustGone/sandbox/phase2_report.txt)）**：总计 **65 条断言，63 通过、2 失败**。2 条失败均为测试脚本对 `LocalMultiBoardSizeSelector` 索引顺序的预期反转（UI 选择器 `0=9路, 1=13路, 2=19路`，而 ES3 `BoardSizeIndex` 为 `0=19路, 1=13路, 2=9路`）；实际运行中 **9×9、13×13、19×19 三种棋盘均已进入并通过 `BoardInspector_CoordRoundTrip` 精确验证**。
- **Save 与 JSON 导出验证边界**：`GameManager.CopySGF` 的 Harmony 补丁注册、`MiniJson` 与 `AtomicFileWriter` 均已通过验证，但 **`ShowSaveFileDialog` 系统文件保存弹窗与沙盒 JSON 落盘尚未在 Phase 2 自动化实机中交互点击验证**（避免阻塞无头自动化进程），下文 API 状态表已如实标注为离线/编译验证，不宣称实机通过。

---

## 2. 框架启动、加载顺序与真实 Harmony Owner 核验

### 2.1 BepInEx 实际加载顺序（7 个插件全部无错加载）
1. `dev.justgone.framework` — `Just Gone v0.1.0`（`JustGoneApi.IsReady = True`）
2. `example.justgone.boardinspector` — `Just Gone Example: Board Inspector v0.1.0`（通过 `RegisterMod` 注册独立 `ModContext`）
3. `example.justgone.customsandbox` — `Just Gone Example: Custom Mode & UI v0.1.0`（通过 `RegisterMod` 注册独立 `ModContext`）
4. `local.weixiangqi.justgo.p0` — `WeiXiangQi P1 Native Menu Preview v0.4.4`
5. `local.weixiangqi.justgo.p2` — `WeiXiangQi P2 Board Geometry Lab v0.5.0`
6. `local.weixiangqi.justgo.p4` — `WeiXiangQi Complete Rules v0.10.10`
7. `test.justgone.phase2harness` — `Just Gone Phase 2 Harness v0.1.0`（仅隔离测试）

### 2.2 真实运行时 Harmony Patch 清单（`Harmony.GetPatchInfo` 实测）

| 目标方法 | Prefix Owner 与优先级（高→低） | Postfix Owner | 冲突评估 |
| --- | --- | --- | --- |
| `TitleManager.LoadLocalMultiMatch` | `dev.justgone.framework (prio=600)` → `local.weixiangqi.justgo.p4 (prio=400)` | 无 | **无冲突**：Just Gone 优先仅接管已注册模式（索引 3），索引 0/1/2 一律返回 `true` 透传 |
| `GameManager.UpdateMouseInput` | `dev.justgone.framework (400)`, `local.weixiangqi.justgo.p4 (400)` | 无 | **无冲突**：无 Just Gone 活动模式/锁时直接透传 |
| `GameManager.UpdateScrollWheel` | `dev.justgone.framework (400)`, `local.weixiangqi.justgo.p4 (400)` | 无 | **无冲突**：同上 |
| `GameManager.UpdateKeyboardInput` | `dev.justgone.framework (400)`, `local.weixiangqi.justgo.p4 (400)` | 无 | **无冲突**：同上 |
| `GameManager.UndoWrapper` | `dev.justgone.framework (400)`, `local.weixiangqi.justgo.p4 (400)` | 无 | **无冲突**：仅活动自定义模式或消费订阅时拦截 |
| `GameManager.RestartGame` | `dev.justgone.framework (400)`, `local.weixiangqi.justgo.p4 (400)` | 无 | **无冲突**：同上 |
| `GameManager.CopySGF` | `dev.justgone.framework (400)`, `local.weixiangqi.justgo.p4 (400)` | 无 | **无冲突**：同上 |
| `GameManager.UpdateButtonsInPanels` | 无 | `dev.justgone.framework (400)`, `local.weixiangqi.justgo.p4 (400)` | **无冲突**：无按钮覆写状态时直接返回 |
| `MouseWheel.Update` | `dev.justgone.framework (400)`, `local.weixiangqi.justgo.p4 (400)` | 无 | **无冲突**：仅 Just Gone 菜单滚动容器可滚动时吸收 |

---

## 3. Phase 2 实机发现并修复的真实兼容性问题

在首次真实 Unity 运行中发现并修复了以下离线测试无法暴露的问题（均在 `JustGone` 内部修复，**未修改《围象棋》任何代码**）：

1. **`HorizontalSelector.CreateNewItem` 导致 `ForwardClick()` 空引用（`NullReferenceException`）**：
   - 根因：`ModernUIPack.HorizontalSelector.CreateNewItem(title)` 构造的 `Item` 未初始化 `onValueChanged`（包括围象棋 P1 添加的索引 2 与 Just Gone 添加的索引 3），点击右键切换模式时触发 `itemList[index].onValueChanged.Invoke()` 抛 NRE。
   - 修复：在 `JgMenuHost.SyncRegisteredModesIntoSelector()` 中为 `itemList` 全部条目的空 `onValueChanged` 补齐 `new UnityEvent()`，并调用 `UpdateIndicators()` 刷新指示器。
2. **自定义模式标题被 I2 `LocalizationManager.GetTermTranslation` 清空**：
   - 修复：在 `JgMenuHost.Update()` 检测到 Just Gone 模式激活且 `label.text` 为空时，自动回填 `DisplayTitle` 并应用 CJK 字体。
3. **从围象棋（索引 2）切换到 Just Gone（索引 3）时的 `BasicPanel/List` 交接竞争**：
   - 修复：在 `JgMenuHost.EnterBasicPageRoutine()` 中等待 `IsListAvailableForHost()`，确保围象棋完成 `WXQ_AIConfigScroll` 卸载还原后再挂载 `JG_MenuScroll`。

---

## 4. 四模式共存往返验收记录（同一 Unity 进程连续完成）

主菜单 `ModeSelection_LM` 实测包含 4 个独立不重复条目：
- `[0]` `Mode_Friend_Matchup`（原版好友约战）
- `[1]` `Mode_Free_Play_Mode`（原版自由摆谱）
- `[2]` `围象棋`（《围象棋》v0.10.10.0）
- `[3]` `自由标记沙盒 (JG)`（Just Gone 示例二注册）

| 往返轮次 | 目标模式 | 进入场景与路数实测 | 所有权核验 | 退出与清理核验 |
| --- | --- | --- | --- | --- |
| Round 1 | 原版模式 0（好友约战） | `GameScene_ChineseGarden`，`9x9`，`started=True` | `JustGone=False`, `WXQ=False` | 原生退出回 `MainScene`，无残留对象 |
| Round 2 | 原版模式 1（自由摆谱） | `GameScene_ChineseGarden`，`13x13`，`started=True` | `JustGone=False`, `WXQ=False` | 原生退出回 `MainScene`，无残留对象 |
| Round 3 | 原版模式 1（自由摆谱） | `GameScene_ChineseGarden`，`19x19`，`started=True` | `JustGone=False`, `WXQ=False` | 含点击探测标记 + `F8` 切换 + 退出零残留 |
| Round 4 | 围象棋模式 2（v0.10.10.0） | `GameScene_ChineseGarden`，`19x19`，`started=True` | `JustGone=False`, `WXQ=True`（完整六子开局与右侧兵种架） | 原生退出回 `MainScene`，无残留对象 |
| Round 5 | Just Gone 模式 3（自由标记沙盒） | `GameScene_ChineseGarden`，`19x19`，`started=True` | `JustGone=True (example.sandbox.marker)`, `WXQ=False`（未被围象棋误拦截） | 原生退出回 `MainScene`，`activeMode=<null>`，`inputSuppressed=False`，零残留对象 |
| 往返后核验 | `MainScene` | `ModeSelection_LM.Count == 4` | 4 个模式全部可选，无重复或覆盖 | 隔离 `SaveFileOfficial_beta.es3` 未写入沙盒数据 |

---

## 5. 双示例 MOD 与多 MOD 独立性实机证据

### 5.1 示例一：`JustGone.Example.BoardInspector`
- 在真实 9×9、13×13、19×19 棋盘上均准确读取 `BoardSize` 与 `Environment`；
- 中心点 `TryCellToScreen` ↔ `TryScreenToCell` 往返完全精确（9×9 `(4,4)/E5`、13×13 `(6,6)/G7`、19×19 `(9,9)/K10`）；
- 点击生成 3D 探测标记（`JG_Markers_...` 子对象数 = 1），`F8` 热键正常隐藏/恢复侧栏；
- 离开棋盘场景后侧栏与标记自动销毁，零残留。

### 5.2 示例二：`JustGone.Example.CustomModeAndUi`
- 主菜单注册「自由标记沙盒 (JG)」并在 `BasicPanel` 渲染原生配置页（路数选择、场景选择、自定义标记输入、当前配置状态、底部蓝色「恢复默认」按钮）；
- 局内生成 9 枚黄色星位 3D 标记、原生黑子 `(9,9)`、原生白子 `(9,10)`、朱红染色带汉字「星」棋子 `(10,9)`；
- 触发原生 `UndoWrapper()` 棋子数 `3 → 2`，触发原生 `RestartGame()` 棋子数 `3 → 0`，右侧原生 `棋谱信息` 同步刷新。

### 5.3 多 MOD 独立性（实机 6/6 全通过）
1. 独立 `ModContext`：`harness.mod.alpha` 与 `harness.mod.beta` 互不冲突；
2. 并发事件订阅互不覆盖；
3. **异常隔离**：`alpha` 回调故意抛出 `InvalidOperationException`，`EventService` 捕获并记录日志，`beta` 回调仍正常执行；
4. **资源销毁隔离**：`alpha.Dispose()` 释放 `alpha` 的输入锁并注销 `alpha`，`beta` 的输入锁与注册状态保持有效；
5. 全部锁释放后 `IsVanillaInputSuppressed == false`，原版输入完全恢复；
6. 退出场景后无悬空 Unity 对象或旧订阅残留。

### 5.4 真实 Unity 运行截图（`F:\JustGone\artifacts\phase2\`）
- [01_menu_jg_config.png](file:///F:/JustGone/artifacts/phase2/01_menu_jg_config.png) — 主菜单「自由标记沙盒 (JG)」原生配置页（1.80 MB）
- [02_weixiangqi_coexist.png](file:///F:/JustGone/artifacts/phase2/02_weixiangqi_coexist.png) — 围象棋 v0.10.10.0 与 Just Gone 同进程共存（5.88 MB）
- [03_jg_custom_sandbox.png](file:///F:/JustGone/artifacts/phase2/03_jg_custom_sandbox.png) — 自由标记沙盒：黑/白/朱红「星」字棋子、9 星位标记、原生侧栏与棋谱列表（5.76 MB）
- [04_board_inspector_9x9.png](file:///F:/JustGone/artifacts/phase2/04_board_inspector_9x9.png) — 棋盘检查器：原生侧栏、GTP/屏幕坐标与青色 3D 探测标记（5.83 MB）

---

## 6. 公共 API 验证状态清单

> 严格区分实机验证、离线单测与场景/模式受限项，不将未实机项夸大为已稳定。

| API 模块 | 已完成真实 Unity 验证 | 仅离线单测 / 编译验证 | 受限或待后续实机补测项 |
| --- | --- | --- | --- |
| **`JustGoneApi` / `ModContext`** | `IsReady`, `RegisterMod`, `TryGetMod`, `RegisteredMods`, `Track`, `Dispose` | `TrackUnityObject` | — |
| **`Game` (`IGameService`)** | `IsInMatchScene`, `IsGameStarted`, `IsPaused`, `CurrentVanillaMode`, `CurrentEnvironment`, `CurrentBoardSize`, `ReturnToMainMenu` | — | `PauseOrResume`, `TrySetCamera`（沿用围象棋已验证路径，Phase 2 未单独触发） |
| **`Scene` (`ISceneService`)** | `CurrentSceneName`, `CurrentSceneKind`, `IsMainMenu`, `IsBoardScene`, `LoadScene` | `LoadEnvironmentScene` | 非 `ChineseGarden` 场景（如 `JapaneseRoom`）的棋盘坐标仅复用原版 `GoUtils`，待实机逐场景复核 |
| **`Board` (`IBoardService`)** | `IsAvailable`, `BoardSize` (9/13/19), `Environment`, `CellToLocal`, `CellToWorld`, `TryCellToScreen`, `TryScreenToCell`, `TryRayToCell`, `HoveredCell`, `GetCellSpacingLocal`, `ClearVanillaStones` | `TryGetBoardScreenCenter` | 仅在棋盘场景且 `GameManager.StonePanel != null` 时可用 |
| **`Pieces` (`IPieceService`)** | `CreateLayer`, `Spawn` (含染色/汉字/动画), `Remove`, `Clear`, `Dispose`, `PlayDropSound` | `Move`, `UpdateVisual`, `SetSelected`, `CreateDisplayShelf` | `CreateDisplayShelf` 依赖 `StonePanel` 且未在 Phase 2 示例中挂载实测 |
| **`Rendering` (`IRenderingService`)** | `CreateMarkerLayer`, `SetMarker`, `Clear`, `Dispose`, `ShowHoverGhost`, `HideHoverGhost` | `RemoveMarker`, `CreateGridOverlay` | `CreateGridOverlay` 仅在含 `OwnershipPrefab` 的棋盘场景可用 |
| **`Input` (`IInputService`)** | `IsPointerOverUi`, `IsTextInputFocused`, `IsVanillaInputSuppressed`, `AcquireVanillaInputLock` | `RegisterMenuWheelAbsorber` | — |
| **`Menu` (`IMenuService`)** | `RegisterMode`, `IMenuPageBuilder` (`AddChoiceRow`, `AddInputRow`, `AddStatusRow`, `AddBottomAction`, `RequestRebuild`) | `AddHeaderRow`, `AddButtonRow`, `ShowMenuNotice`, `ShowTransientNotice` | 仅在 `MainScene` 的本地练习 `BasicPanel` 生效 |
| **`UI` (`IUiService`)** | `ShowToast`, `CreateSidebarPanel`, `SetItems`, `SetSelectedItem`, `SetVisible`, `Dispose`, `SetPracticeButtonStates`, `ClearPracticeButtonOverrides`, `SetPracticeHistory` | `ShowStickyError`, `ClearStickyError`, `ShowGameOver`, `HideGameOver`, `ShowSaveFileDialog` | `ShowSaveFileDialog` 触发系统原生文件对话框（自动化未弹窗阻塞，沿用围象棋实测代码） |
| **`Modes` (`IGameModeService`)** | `RegisterGameMode`, `TryLaunchMode`, `ActiveModeId`, `HasActiveMode`, `IGameModeSession` (`OnStart`/`OnUpdate`/`OnEnd`/`OnNativeAction`) | `AbortActiveMode`（Failsafe 路径） | 同一时间仅允许 1 个活动自定义模式 |
| **`Events` (`IEventService`)** | `OnSceneLoaded`, `OnGameReady`, `OnGameLeaving`, `OnBoardHoverChanged`, `OnBoardClicked`, `OnKeyPressed` | `OnMouseWheel`, `OnPauseChanged`, `OnNativeAction` | — |
| **`Resources` (`IResourceService`)** | `GetCjkFontInternal`, `FindOrCreateFontInternal`, `GetModConfigDirectory` | `HasNativeStonePrefabs`, `HasNativeOwnershipPrefab`, `HasCjkFont`, `EnsureFontForCharacters` | — |
| **`Saves` (`ISaveGuardService`)** | `BeginLaunchOverride`, `RestoreLastSnapshotSafe`, `ScanIncompleteTransactions` | `SaveTransactionRules` CAS 对账全分支 | 仅保护原版 ES3 4 个启动键，不管理第三方 MOD 自定义存档 |
| **`JustGone.Core` 纯逻辑** | 运行时间接验证 | 12 组全量单元测试（`BoardCoord`/`MenuRouting`/`SessionGuard`/`SaveTransactionRules`/`AtomicFileWriter`/`MiniJson`/`PieceLedger`/`DisposableTracker`/`InputLockRegistry`） | — |

---

## 7. 已知限制（实机观察诚实记录）

1. **多 MOD 左侧栏与顶部 Toast 位置重叠**：当多个 MOD（如《围象棋》与 `BoardInspector`，或 `BoardInspector` 与 `CustomModeAndUi`）同时使用左侧镜像侧栏（`MirrorNativeHistoryPanel = true`）或同时发送顶部 Toast 时，会在同一屏幕区域叠加（见截图 `02`/`03`）。当前可通过自定义 `AnchorMin`/`AnchorMax` 或热键（如 `F8`）避让；Phase 3 计划增加共享侧栏槽位协调器与 Toast 队列。
2. **主菜单配置页视口高度基线**：`JgMenuHost` 在首次检测到 `BasicPanel/List` 时捕获设计态高度；若先经过行较少的原版「自由摆谱」再首次捕获，视口高度可能为 `300`（见截图 `01`），虽可通过原生滚动条/滚轮完整滚动，但可见行数偏少。后续可结合 `BasicPanel` 固定比例作为最小视口高度兜底。
3. **侧栏折叠箭头字符**：默认英文 TMP 字体不含 `▾`/`▸` 符号，在 `02`/`03`/`04` 截图中渲染为方块 `□`；后续应改用 CJK 字体或 ASCII 标记（`[-]`/`[+]`）。
4. **单活动模式限制**：同一时刻仅支持激活 1 个自定义游戏模式；观察/工具类 MOD（如 `BoardInspector`）可与任意原版模式或自定义模式同时运行。

---

## 8. 制品、哈希与测试/回滚材料

- 隔离测试副本保留于：`F:\JustGone\sandbox\JustGo_Isolated`
- 隔离测试存档保留于：`F:\JustGone\sandbox\IsolatedUserData\PersistentData`
- 完整自动化实机报告保留于：[phase2_report.txt](file:///F:/JustGone/sandbox/phase2_report.txt)
- 事务安装与回滚脚本保留于：[deploy-lib.ps1](file:///F:/JustGone/tools/deploy-lib.ps1)、[install.ps1](file:///F:/JustGone/tools/install.ps1)、[test-deployment.ps1](file:///F:/JustGone/tools/test-deployment.ps1)
- 最新构建产物 SHA-256 见本轮最终构建核验输出。
