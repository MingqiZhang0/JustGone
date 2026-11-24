# Just Gone 开发路线图（ROADMAP）

> 本路线图完全依据《围象棋》v0.10.10.0 的真实源码成熟度制定，避免重复底层探索，按“基础设施先行 → 真实示例验证 → 隔离实机共存验收 → 生态扩展与迁移”推进。

---

## Phase 1：框架立项、核心提取与首批公共 API（本轮交付）

**目标**：建立独立 `F:\JustGone` 工程，从《围象棋》v0.10.10.0 完成第一/二类通用能力提取、解耦、公共 API 封装、双示例 MOD 与自动化验证。

- [x] 完成《围象棋》v0.10.10.0 全量 58 个生产源 + 部署工具链复用分析（`SOURCE_REUSE_MATRIX.md`）
- [x] 完成框架分层架构与《围象棋》Harmony 共存设计（`ARCHITECTURE.md`）
- [x] 完成公共 API 契约文档（`API_CATALOG.md`）
- [x] 提取 `JustGone.Core` 纯逻辑层（坐标、多 MOD 菜单路由、会话守卫、通用 CAS ES3 事务规则、原子写、MiniJson、泛型视图账本与 Delta 计划器）
- [x] 提取并解耦 `JustGone.dll` 全部 12 组运行时服务与 `ModContext` 所有权模型
- [x] 实现示例 MOD 一：`JustGone.Example.BoardInspector`（零 `Assembly-CSharp` / `0Harmony` 引用，验证棋盘尺寸/坐标/悬停/点击/场景状态/原生侧栏与 Toast）
- [x] 实现示例 MOD 二：`JustGone.Example.CustomModeAndUi`（零 `Assembly-CSharp` / `0Harmony` 引用，验证原生模式菜单注册、原生 `BasicPanel` 配置页、自定义沙盒模式接管、原生棋子生成/染色/汉字/动画、格点高亮、3D 陈列与原版 Undo/Restart/Save 代理）
- [x] 提取 net472 构建脚本与事务性部署/回滚工具链（`tools/build.ps1`, `tools/deploy-lib.ps1`, `tools/install.ps1`, `tools/test-deployment.ps1`）
- [x] 离线验证：net472 编译零错误 + `JustGone.Core.Tests` 全量单元测试 + 部署事务/回滚测试

---

## Phase 2：隔离 Unity 实机共存验收与稳定化

**目标**：在既有独立 Windows 测试账户（`CodexSandboxOffline` 等离线隔离副本，不触碰真实用户存档与 API Key）完成实机验收。

1. **原版回归验证**：安装 `JustGone.dll`（不装示例 MOD），验证原版 0（好友约战）、1（自由摆谱）、本地人机与基础操作零异常。
2. **与《围象棋》v0.10.10.0 共存验证**：同时加载 `JustGoWeiXiangQiP4.dll` 与 `JustGone.dll` + 两个示例 MOD：
   - 验证 `ModeSelection_LM` 同时存在原版 0/1、围象棋 2、Just Gone 示例模式 3；
   - 验证切换到索引 2 正常进入围象棋、切换到索引 3 正常进入 Just Gone 示例模式（不触发围象棋 `BlockUnsupportedIndex`）；
   - 验证围象棋对局内输入、落子、菜单滚轮与退出完全不受 Just Gone 干扰。
3. **双示例 MOD 共存验证**：同时加载 `BoardInspector` 与 `CustomModeAndUi`，验证事件隔离、图层隔离、退出场景资源零残留、ES3 CAS 恢复正常。
4. **初始化失败回滚专项**：注入异常确认 `JustGone` 仅 `UnpatchSelf()` 自身补丁，不影响原版及围象棋补丁。

---

## Phase 3：原生 UI 与扩展能力深化

**目标**：基于 Phase 2 实机反馈补强更高阶的通用能力（仅按真实需求扩展，不造空壳）。

1. **更多原生菜单入口扩展**：支持在 `VSComputer` / `Settings` / `PausePanel` 注入原生风格按钮与配置分组。
2. **原版棋盘只读快照监听**：在不干扰原版规则的前提下，为分析类/复盘类 MOD 提供原版落子、提子、手数变化的只读事件（需针对性补充读取 `GameManager` 落子触发点）。
3. **布局诊断与开发工具包**：按需从 `WxqUiDiagnostics.cs` 提取可选的 `JustGone.Diagnostics` 模块，提供菜单几何断言与按键探针。
4. **MOD 打包模板（`dotnet new justgone-mod`）与 XML 文档生成**：发布标准 NuGet/本地 SDK 包，提供一键脚手架。

---

## Phase 4：《围象棋》兼容性迁移评估（长期独立专项）

**目标**：待 Just Gone 经过 Phase 2–3 充分实战稳定后，再独立评估将《围象棋》底层集成层迁移至 Just Gone：
- 严格遵循“先并存、后渐进迁移”原则，当前阶段绝不改动《围象棋》生产源码与安装件；
- 迁移后《围象棋》将精简掉 ~2500 行重复的菜单布局、ES3 守卫、Harmony 补丁与棋子视图胶水代码，专注核心规则与 AI 引擎。
