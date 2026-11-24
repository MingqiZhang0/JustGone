# Just Gone 第三方 MOD 开发者指南（v0.1.0 Developer Preview）

> ⚠️ **稳定性声明**：当前为 `v0.1.0` 开发者预览版（Developer Preview）。所有公开接口均由真实代码实现，但在 `1.0.0` 正式版之前**不承诺所有公共接口永久保持二进制兼容**。各 API 的实机验证与稳定性状态见本文第 9 节及 [PHASE2_ACCEPTANCE_REPORT.md](PHASE2_ACCEPTANCE_REPORT.md)。

本指南帮助第三方开发者仅引用 `JustGone.dll` 构建《Just Go》MOD——**无需引用 `Assembly-CSharp.dll`，无需引用 `0Harmony.dll`，也不需要《围象棋》源码**。

---

## 1. 环境与依赖要求

- **游戏本体**：《Just Go》v1.2.0（Unity 2021.3.29f1 Mono，Windows x64）
- **插件加载器**：BepInEx 5.4.23.6（含 0Harmony 2.9.0.0）
- **框架版本**：`JustGone v0.1.0`（GUID: `dev.justgone.framework`）
- **编译环境**：.NET SDK（支持 `net472` 引用程序集）与 C# 8.0+

---

## 2. 项目引用与 BepInEx 依赖声明

使用 SDK 包中的 `templates/JustGoneModTemplate`（或自行新建 `net472` 类库），仅需引用 `JustGone.dll`、`BepInEx.dll`、`UnityEngine.dll`、`UnityEngine.CoreModule.dll`（均设为 `<Private>false</Private>`）：

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net472</TargetFramework>
    <LangVersion>8.0</LangVersion>
    <GameDir Condition="'$(GameDir)' == ''">F:\Steam\steamapps\common\Just Go</GameDir>
    <JustGoneSdkDir Condition="'$(JustGoneSdkDir)' == ''">..\lib\net472</JustGoneSdkDir>
    <AppendTargetFrameworkToOutputPath>false</AppendTargetFrameworkToOutputPath>
    <CopyLocalLockFileAssemblies>false</CopyLocalLockFileAssemblies>
  </PropertyGroup>

  <ItemGroup>
    <Reference Include="JustGone">
      <HintPath>$(JustGoneSdkDir)\JustGone.dll</HintPath>
      <Private>false</Private>
    </Reference>
    <Reference Include="BepInEx">
      <HintPath>$(GameDir)\BepInEx\core\BepInEx.dll</HintPath>
      <Private>false</Private>
    </Reference>
    <Reference Include="UnityEngine">
      <HintPath>$(GameDir)\JustGo_Data\Managed\UnityEngine.dll</HintPath>
      <Private>false</Private>
    </Reference>
    <Reference Include="UnityEngine.CoreModule">
      <HintPath>$(GameDir)\JustGo_Data\Managed\UnityEngine.CoreModule.dll</HintPath>
      <Private>false</Private>
    </Reference>
  </ItemGroup>
</Project>
```

插件类上**必须**声明 `[BepInDependency(JustGoneApi.FrameworkGuid)]`：

```csharp
[BepInPlugin("author.justgone.mymod", "My Just Go Mod", "0.1.0")]
[BepInDependency(JustGoneApi.FrameworkGuid)]
public sealed class MyModPlugin : BaseUnityPlugin { ... }
```

---

## 3. 编写第一个第三方 MOD（最小可运行示例）

```csharp
using BepInEx;
using JustGone;
using JustGone.Core;
using UnityEngine;

namespace MyJustGoneMod
{
    [BepInPlugin("author.justgone.hello", "Hello Just Gone", "0.1.0")]
    [BepInDependency(JustGoneApi.FrameworkGuid)]
    public sealed class HelloModPlugin : BaseUnityPlugin
    {
        private ModContext _ctx;
        private IMarkerLayer _markers;

        private void Awake()
        {
            _ctx = JustGoneApi.RegisterMod("author.justgone.hello", "Hello Just Gone", "0.1.0");
            JustGoneApi.Events.OnGameReady(_ctx, e =>
            {
                Cleanup();
                _markers = JustGoneApi.Rendering.CreateMarkerLayer(_ctx, "hello");
                JustGoneApi.UI.ShowToast($"已进入 {e.BoardSize}x{e.BoardSize} 棋盘 ({e.Environment})", 2.5f);
            });
            JustGoneApi.Events.OnBoardClicked(_ctx, e =>
            {
                if (e.PointerOverUi || e.MouseButton != 0 || !e.Coord.IsValid) return;
                _markers?.Clear();
                _markers?.SetMarker(e.Coord, Color.cyan, diameterScale: 0.85f);
                JustGoneApi.UI.ShowToast($"点击 {e.Coord.ToGtp(e.BoardSize)} {e.Coord}", 1.8f);
            });
            JustGoneApi.Events.OnGameLeaving(_ctx, _ => Cleanup());
        }

        private void Cleanup() { _markers?.Dispose(); _markers = null; }
        private void OnDestroy() { Cleanup(); _ctx?.Dispose(); }
    }
}
```

构建后将生成的 `.dll` 放入 `<Just Go 目录>\BepInEx\plugins\<你的MOD名>\` 即可加载运行。

---

## 4. 注册自定义游戏模式与原生菜单配置页

通过 `JustGoneApi.Modes.RegisterGameMode(_ctx, definition)` 将新模式自动追加到原版「练习模式」的 `ModeSelection_LM`（不覆盖原版 0/1 或其他 MOD 的模式项），并可提供原生 `BasicPanel` 配置页与会话实现 `IGameModeSession`：

```csharp
JustGoneApi.Modes.RegisterGameMode(_ctx, new GameModeDefinition
{
    ModeId = "author.mymode",
    DisplayTitle = "我的自定义模式",
    TargetEnvironment = GameEnvironmentKind.ChineseGarden,
    BoardSize = BoardSizeKind.Nineteen,
    SuppressVanillaInput = true,       // 自动屏蔽原版落子与快捷键
    ClearVanillaStonesOnStart = true,  // 自动清理原版残留棋子
    ValidateLaunch = () => MenuLaunchValidation.Ok(),
    BuildMenuConfigPage = page =>
    {
        page.AddChoiceRow("选项", new[] { "A", "B" }, 0, idx => { /* 更新配置 */ });
        page.AddInputRow("备注", "默认", txt => { /* 更新输入 */ });
        page.AddStatusRow("状态", "就绪");
        page.AddBottomAction("恢复默认", () => page.RequestRebuild(true));
    },
    CreateSession = (owner, sessionCtx) => new MySession(owner)
});
```

进入模式前框架会自动调用 `SaveGuardService` 备份并校验原版 `SaveFileOfficial_beta.es3`，退出或异常中止时按 Compare-And-Set 安全回滚。

---

## 5. 访问棋盘、棋子与 3D 渲染

- **棋盘信息与坐标转换（`JustGoneApi.Board`）**：
  - `BoardSize`（9 / 13 / 19）、`Environment`、`HoveredCell`
  - `CellToLocal(coord)` / `CellToWorld(coord)` / `TryCellToScreen(coord, out sp)` / `TryScreenToCell(sp, out coord)`
  - `coord.ToGtp(boardSize)` 与 `BoardCoord.TryParseGtp(...)`
- **原生棋子图层（`JustGoneApi.Pieces.CreateLayer`）**：
  - 每个 MOD 独立图层，不修改原版棋盘数组；
  - `Spawn(id, coord, new PieceVisualSpec { BaseStyle = StoneBaseStyle.White, Tint = Color.red, LabelText = "星", UseCjkFont = true })`
  - 支持 `Move`、`UpdateVisual`、`Remove`、`SetSelected`、`PlayDropSound`、`Clear`、`Dispose`
- **格点标记与幽灵棋子（`JustGoneApi.Rendering`）**：
  - `CreateMarkerLayer(ctx, name)` → `SetMarker(coord, color, diameterScale)`
  - `ShowHoverGhost(StoneBaseStyle.Black, coord)` / `HideHoverGhost()`

---

## 6. 处理输入与事件

- **事件总线（`JustGoneApi.Events`）**：`OnSceneLoaded`、`OnGameReady`、`OnGameLeaving`、`OnBoardHoverChanged`、`OnBoardClicked`、`OnKeyPressed`、`OnMouseWheel`、`OnPauseChanged`、`OnNativeAction`。
  - 每个订阅者运行在独立 `try/catch` 中，某个 MOD 回调抛异常不会阻断其他 MOD。
- **输入查询与多 MOD 输入锁（`JustGoneApi.Input`）**：
  - 点击棋盘前务必检查 `e.PointerOverUi`（或 `Input.IsPointerOverUi`），避免穿透 UI；
  - 响应快捷键前务必检查 `e.IsTextInputFocused`（或 `Input.IsTextInputFocused`），避免在输入框打字时误触热键；
  - 需要临时拦截原版棋盘输入时使用 `IDisposable lockToken = JustGoneApi.Input.AcquireVanillaInputLock(_ctx, "reason")`，采用多 MOD 引用计数，释放后不影响其他 MOD 的锁。

---

## 7. 创建原生风格 UI

- **局内 Toast / 错误提示**：`JustGoneApi.UI.ShowToast("提示", 2.5f)`、`ShowStickyError(...)`、`ClearStickyError()`
- **原生折叠侧栏**：`JustGoneApi.UI.CreateSidebarPanel(_ctx, new SidebarPanelOptions { Title = "工具栏", MirrorNativeHistoryPanel = true })`
- **原版 Practice 按钮与右侧棋谱**：
  - 在 `IGameModeSession.OnNativeAction` 中拦截 `Undo` / `Restart` / `Save`；
  - 通过 `SetPracticeButtonStates(undo, save, restart)` 与 `SetPracticeHistory(items, onClick)` 同步原版界面状态。

---

## 8. `ModContext` 生命周期、资源释放与多 MOD 共存

1. 在 `Awake()` 调用 `JustGoneApi.RegisterMod(...)` 获取 `_ctx`，在 `OnDestroy()` 调用 `_ctx?.Dispose()`。
2. 所有事件订阅、输入锁、图层、侧栏面板、菜单模式注册均自动挂入 `_ctx`（或 `ModeSessionContext.Track(...)`），销毁时按 LIFO 逆序清理，单个资源异常不会阻断后续清理。
3. **跨场景规则**：离开棋盘场景时（`OnGameLeaving`）必须释放旧场景的图层与侧栏，切勿缓存旧 `GameObject` 跨场景复用。
4. **多 MOD 共存规范**：
   - 工具类 MOD 的左侧栏应提供快捷键开关（如 `F8`）或自定义 `AnchorMin`/`AnchorMax`，避免与自定义模式侧栏叠加；
   - 严禁绕过 Just Gone 直接给 `TitleManager.LoadLocalMultiMatch` 或 `GameManager.Update*Input` 打冲突 Harmony 补丁；
   - 不要向原版 `SaveFileOfficial_beta.es3` 写入自定义 MOD 数据，请使用 `JustGoneApi.Resources.GetModConfigDirectory(_ctx)` + `AtomicFileWriter` 保存自有配置。

---

## 9. 当前 API 稳定性等级与已知限制

- **实机稳定度分层**（详见 [PHASE2_ACCEPTANCE_REPORT.md](PHASE2_ACCEPTANCE_REPORT.md) 第 6 节）：
  - **已实机验证**：`ModContext`、四模式路由与共存、`Board`（9/13/19 坐标/GTP/拾取）、`Pieces.CreateLayer`（染色/汉字/落子/移除）、`Rendering`（标记与幽灵棋子）、`Menu` 配置页、`UI`（Toast/侧栏/Practice 按钮与棋谱）、`Modes` 会话与 `Undo`/`Restart`、`Events` 异常隔离、`Input` 多 MOD 锁、`Saves` ES3 CAS 事务。
  - **仅离线/编译验证**：`ShowSaveFileDialog`（系统文件保存弹窗未在自动化实机交互点选）、`ShowGameOver` / `HideGameOver`、`CreateDisplayShelf`、`CreateGridOverlay`、`PauseOrResume`、`TrySetCamera`。
- **已知限制**：多 MOD 左侧栏/Toast 尚未做统一槽位排队；默认英文字体缺失 `▾`/`▸` 箭头字形；同一时刻仅允许 1 个自定义游戏模式激活；`v0.1.0` 不承诺 `1.0.0` 前的二进制兼容。
