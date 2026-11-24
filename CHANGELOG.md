# Changelog

All notable changes to **Just Gone (The Just Go Modding Extension)** are documented in this file.

## [0.1.0-preview] - 2026-11-24 (Developer Preview)

> **API Stability Notice**: Developer Preview release. Public APIs are backed by real implementations, but binary compatibility is not yet guaranteed prior to `1.0.0`.

### Added
- **Pure-Logic Core (`JustGone.Core`)**: `BoardCoord` (9/13/19 + GTP), `BoardTypes`, multi-MOD `MenuRouteTable`, `SessionGuard`, generalized CAS `SaveTransactionRules`, `AtomicFileWriter`, zero-dependency `MiniJson`, generic `PieceLedger<TData>`, `DisposableTracker`, `InputLockRegistry`.
- **12 Public Runtime Services (`JustGone.dll`)**:
  - `Game`, `Scene`, `Board`, `Pieces`, `Rendering`, `Input`, `Menu`, `UI`, `Modes`, `Events`, `Resources`, `Saves`.
- **Per-MOD Isolation (`ModContext`)**: Isolated registration, logging (`[JG:<modId>]`), resource/event/lock tracking, and LIFO cleanup on `Dispose()`.
- **Coexistence Architecture**: `Priority.High` menu routing on `TitleManager.LoadLocalMultiMatch` preserving vanilla modes 0/1 and coexisting with standalone《围象棋》v0.10.10.0 (index 2); transactional `UnpatchSelf()` rollback on initialization failure.
- **Two Reference Example MODs** (compile-time verified zero references to `Assembly-CSharp` and `0Harmony`):
  - `JustGone.Example.BoardInspector`
  - `JustGone.Example.CustomModeAndUi`
- **SDK Project Template**: `templates/JustGoneModTemplate/` for standalone third-party MOD development.
- **Transactional Build & Deployment Tooling**: `tools/build.ps1`, `tools/deploy-lib.ps1`, `tools/install.ps1`, `tools/test-deployment.ps1`, `tools/package-release.ps1`.

### Fixed (during Phase 2 Real Unity Integration)
- Initialized null `HorizontalSelector.Item.onValueChanged` across `ModeSelection_LM` items and refreshed indicators via `UpdateIndicators()`, preventing `NullReferenceException` on `ForwardClick()` when stepping past mod-added entries.
- Restored custom mode display titles with CJK font fallback when I2 `LocalizationManager.GetTermTranslation` returns empty.
- Added `BasicPanel/List` handoff wait (`IsListAvailableForHost`) so switching directly from《围象棋》(index 2) to a Just Gone mode (index 3) waits cleanly for `WXQ_AIConfigScroll` teardown.

### Verified
- `JustGone.Core.Tests`: 12/12 unit tests passed.
- Deployment transaction tests: 11/11 assertions passed.
- Real Unity isolated coexistence acceptance: 63/65 harness assertions passed (2 harness-side selector index expectation mismatches; all 9×9, 13×13, and 19×19 boards, 4-mode round-trip, multi-MOD exception & resource isolation, and 4 full-resolution screenshots verified).
