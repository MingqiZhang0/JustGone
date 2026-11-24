# Third-Party Notices & Source Provenance

> **License**: Original source code and documentation in **Just Gone** are licensed under the [MIT License](LICENSE). Third-party runtime components and external projects listed below remain subject to their respective licenses and terms. This document records source provenance and third-party dependencies required for compliance and attribution.

---

## 1. Internal Technical Provenance (First-Party Extracted Code)

Just Gone extracts, decouples, and generalizes battle-tested 《Just Go》 integration techniques from the project Owner's first-party project **《围象棋》 (`JustGo_WeiXiangQi`) v0.10.10.0** (baseline SHA-256 `696EAE9DC56B38064DC85AEA324FD9D5BFE4AFBF7BF8AF77A081F05B57A3E2AE`):

- **Direct / Lightly Generalized Reuses**: `AtomicFileWriter` (from `WxqAtomicConfigFile.cs`), `MiniJson` (from `WxqMiniJson.cs`), `SessionGuard` (from `WxqCompatGuards.cs`), `SaveTransactionRules` (from `WxqTxn.cs`), `JgLog` (from `WxqLog.cs`), `ResourceService` font fallback (from `WxqGlyphProvider.cs`), `deploy-lib.ps1` / `install.ps1` (from `p4-deployment.ps1` / `p4-install.ps1`).
- **Decoupled & Generalized Framework Services**: `MenuRouting` / `JgMenuHost` (from `WxqMenuRoute` & `WxqMenuConfigPanel.cs`), `JgPatches` (from `Patches.cs`), `JgBootstrap` / `JgFailsafe` (from `WxqBootstrap.cs`, `WxqLauncher.cs`, `WxqFailsafe.cs`), `SaveGuardService` (from `WxqEs3Guard.cs`), `BoardService` / `PieceService` / `RenderingService` (from `WxqStoneView.cs`, `WxqMarkers.cs`, `WxqNativeOwnershipView.cs`, `WxqRecruitShelf.cs`), `UiService` (from `WxqNativeFeedback.cs`, `WxqRecruitRegionPanel.cs`, `WxqNativeUiAdapter.cs`), `PieceLedger` (from `WxqViewLedger.cs` & `WxqViewPlan.cs`).
- **Strictly Excluded**: All WeiXiangQi-specific game rules, three-tier recruitment, economy, AI prompts/agents, credential storage, and legacy P1/P2 compatibility shims remain exclusively in `JustGo_WeiXiangQi`. Just Gone has **zero runtime dependency** on `JustGoWeiXiangQiP4.dll`.

Full 58-file traceability is documented in `docs/SOURCE_REUSE_MATRIX.md`.

---

## 2. Third-Party Runtime Dependencies (Not Bundled in Release Packages)

Just Gone compiles against (`Private=false`) and runs alongside the following third-party components installed by the user. **None of these binaries are redistributed in Just Gone release archives**:

| Component | Version Used | License | Purpose |
| --- | --- | --- | --- |
| **BepInEx 5** (`BepInEx.dll`) | 5.4.23.6 | LGPL-2.1 | Unity plugin loader, configuration, and logging runtime |
| **HarmonyX / 0Harmony** (`0Harmony.dll`) | 2.9.0.0 | MIT | Runtime IL patching and owner-scoped unpatching |
| **Unity Engine & TextMeshPro** (`UnityEngine*.dll`, `Unity.TextMeshPro.dll`) | Unity 2021.3.29f1 | Proprietary (Unity Technologies) | Game engine runtime installed with 《Just Go》 |
| **《Just Go》 Managed Assemblies** (`Assembly-CSharp.dll`, `Assembly-CSharp-firstpass.dll`, `Sirenix.*.dll`) | 《Just Go》 v1.2.0 | Proprietary (Studio Amateur / respective vendors) | Target game runtime; referenced only internally by `JustGone.dll`, never redistributed and never exposed to third-party MODs |

---

## 3. Clean Distribution Guarantee

Just Gone release archives (`Runtime`, `SDK`, `Examples`) contain **only** first-party Just Gone source code, documentation, scripts, and compiled Just Gone assemblies. They never bundle game executables, game managed DLLs, game art/audio assets, user save files (`*.es3`), API keys, `steam_appid.txt`, or isolated test harnesses.
