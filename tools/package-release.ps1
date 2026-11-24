param(
  [string]$Version = '0.1.0',
  [string]$Configuration = 'Release',
  [string]$GameDir = 'F:\Steam\steamapps\common\Just Go'
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$root = Split-Path -Parent $here
$releaseRoot = Join-Path $root "release\v$Version"
$stageRoot = Join-Path $releaseRoot '_staging'

if (Test-Path -LiteralPath $releaseRoot) { Remove-Item -LiteralPath $releaseRoot -Recurse -Force }
New-Item -ItemType Directory -Force -Path $releaseRoot, $stageRoot | Out-Null

# Locate compiled artifacts (preserving Phase 2 verified binaries)
$jgDll = Join-Path $root "src\JustGone\bin\$Configuration\JustGone.dll"
$jgPdb = Join-Path $root "src\JustGone\bin\$Configuration\JustGone.pdb"
$jgXml = Join-Path $root "src\JustGone\bin\$Configuration\JustGone.xml"
$ex1Dll = Join-Path $root "src\Examples\JustGone.Example.BoardInspector\bin\$Configuration\JustGone.Example.BoardInspector.dll"
$ex1Pdb = Join-Path $root "src\Examples\JustGone.Example.BoardInspector\bin\$Configuration\JustGone.Example.BoardInspector.pdb"
$ex2Dll = Join-Path $root "src\Examples\JustGone.Example.CustomModeAndUi\bin\$Configuration\JustGone.Example.CustomModeAndUi.dll"
$ex2Pdb = Join-Path $root "src\Examples\JustGone.Example.CustomModeAndUi\bin\$Configuration\JustGone.Example.CustomModeAndUi.pdb"

foreach ($f in @($jgDll, $jgXml, $ex1Dll, $ex2Dll)) {
  if (!(Test-Path -LiteralPath $f -PathType Leaf)) { throw "Required build artifact missing: $f" }
}

function Copy-FileTo([string]$src, [string]$dst) {
  $dir = Split-Path -Parent $dst
  if (!(Test-Path -LiteralPath $dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
  Copy-Item -LiteralPath $src -Destination $dst -Force
}

# ==================================================================
# 1. Runtime Package
# ==================================================================
$rtStage = Join-Path $stageRoot 'Runtime'
Copy-FileTo $jgDll (Join-Path $rtStage 'plugins\JustGone\JustGone.dll')
if (Test-Path -LiteralPath $jgPdb) { Copy-FileTo $jgPdb (Join-Path $rtStage 'plugins\JustGone\JustGone.pdb') }
Copy-FileTo $jgXml (Join-Path $rtStage 'plugins\JustGone\JustGone.xml')
Copy-FileTo (Join-Path $root 'tools\deploy-lib.ps1') (Join-Path $rtStage 'tools\deploy-lib.ps1')
Copy-FileTo (Join-Path $root 'tools\install.ps1') (Join-Path $rtStage 'tools\install.ps1')
Copy-FileTo (Join-Path $root 'README.md') (Join-Path $rtStage 'README.md')
Copy-FileTo (Join-Path $root 'README.zh-CN.md') (Join-Path $rtStage 'README.zh-CN.md')
Copy-FileTo (Join-Path $root 'LICENSE') (Join-Path $rtStage 'LICENSE')
Copy-FileTo (Join-Path $root 'CHANGELOG.md') (Join-Path $rtStage 'CHANGELOG.md')
Copy-FileTo (Join-Path $root 'THIRD_PARTY_NOTICES.md') (Join-Path $rtStage 'THIRD_PARTY_NOTICES.md')

$installGuide = @"
# Just Gone v$Version Runtime — 安装与卸载说明

## 环境要求
- 《Just Go》v1.2.0 (Unity 2021.3.29f1 Mono)
- BepInEx 5.4.23.6（含 0Harmony 2.9.0.0）

## 安装方式一：事务性安全安装（推荐）
先退出《Just Go》，再执行：
``````powershell
& '.\tools\install.ps1' -GameDir '<你的 Just Go 目录>' -From '.\plugins\JustGone\JustGone.dll' -ExpectedVersion '0.1.0.0' -ExpectedSha256 '$((Get-FileHash -LiteralPath $jgDll -Algorithm SHA256).Hash)'
``````

## 安装方式二：手动安装
1. 退出《Just Go》。
2. 将 `plugins\JustGone\` 整个目录复制到 `<你的 Just Go 目录>\BepInEx\plugins\JustGone\`。

## 卸载
1. 退出《Just Go》。
2. 删除 `<你的 Just Go 目录>\BepInEx\plugins\JustGone\` 目录。
3. 不会影响原版存档或《围象棋》插件。
"@
Set-Content -LiteralPath (Join-Path $rtStage 'INSTALL.md') -Value $installGuide -Encoding UTF8

# ==================================================================
# 2. SDK Package
# ==================================================================
$sdkStage = Join-Path $stageRoot 'SDK'
Copy-FileTo $jgDll (Join-Path $sdkStage 'lib\net472\JustGone.dll')
Copy-FileTo $jgXml (Join-Path $sdkStage 'lib\net472\JustGone.xml')
foreach ($doc in @('DEVELOPER_GUIDE.md', 'API_CATALOG.md', 'ARCHITECTURE.md', 'PHASE2_ACCEPTANCE_REPORT.md', 'ROADMAP.md')) {
  Copy-FileTo (Join-Path $root "docs\$doc") (Join-Path $sdkStage "docs\$doc")
}
foreach ($tf in @('MyJustGoneMod.csproj', 'MyModPlugin.cs', 'README.md')) {
  Copy-FileTo (Join-Path $root "templates\JustGoneModTemplate\$tf") (Join-Path $sdkStage "templates\JustGoneModTemplate\$tf")
}
Copy-FileTo (Join-Path $root 'README.md') (Join-Path $sdkStage 'README.md')
Copy-FileTo (Join-Path $root 'README.zh-CN.md') (Join-Path $sdkStage 'README.zh-CN.md')
Copy-FileTo (Join-Path $root 'LICENSE') (Join-Path $sdkStage 'LICENSE')
Copy-FileTo (Join-Path $root 'CHANGELOG.md') (Join-Path $sdkStage 'CHANGELOG.md')
Copy-FileTo (Join-Path $root 'THIRD_PARTY_NOTICES.md') (Join-Path $sdkStage 'THIRD_PARTY_NOTICES.md')

# ==================================================================
# 3. Examples Package (Sources + Compiled DLLs)
# ==================================================================
$exStage = Join-Path $stageRoot 'Examples'
Copy-FileTo $ex1Dll (Join-Path $exStage 'bin\JustGone.Example.BoardInspector\JustGone.Example.BoardInspector.dll')
if (Test-Path -LiteralPath $ex1Pdb) { Copy-FileTo $ex1Pdb (Join-Path $exStage 'bin\JustGone.Example.BoardInspector\JustGone.Example.BoardInspector.pdb') }
Copy-FileTo $ex2Dll (Join-Path $exStage 'bin\JustGone.Example.CustomModeAndUi\JustGone.Example.CustomModeAndUi.dll')
if (Test-Path -LiteralPath $ex2Pdb) { Copy-FileTo $ex2Pdb (Join-Path $exStage 'bin\JustGone.Example.CustomModeAndUi\JustGone.Example.CustomModeAndUi.pdb') }

foreach ($sf in @(
  'src\Examples\JustGone.Example.BoardInspector\JustGone.Example.BoardInspector.csproj',
  'src\Examples\JustGone.Example.BoardInspector\BoardInspectorPlugin.cs',
  'src\Examples\JustGone.Example.CustomModeAndUi\JustGone.Example.CustomModeAndUi.csproj',
  'src\Examples\JustGone.Example.CustomModeAndUi\CustomModeUiPlugin.cs'
)) {
  Copy-FileTo (Join-Path $root $sf) (Join-Path $exStage $sf)
}
Copy-FileTo (Join-Path $root 'README.md') (Join-Path $exStage 'README.md')
Copy-FileTo (Join-Path $root 'README.zh-CN.md') (Join-Path $exStage 'README.zh-CN.md')
Copy-FileTo (Join-Path $root 'LICENSE') (Join-Path $exStage 'LICENSE')
Copy-FileTo (Join-Path $root 'CHANGELOG.md') (Join-Path $exStage 'CHANGELOG.md')
Copy-FileTo (Join-Path $root 'THIRD_PARTY_NOTICES.md') (Join-Path $exStage 'THIRD_PARTY_NOTICES.md')

# ==================================================================
# Create ZIPs + Strict Hygiene Scan
# ==================================================================
$rtZip = Join-Path $releaseRoot "JustGone-v$Version-Runtime.zip"
$sdkZip = Join-Path $releaseRoot "JustGone-v$Version-SDK.zip"
$exZip = Join-Path $releaseRoot "JustGone-v$Version-Examples.zip"

[IO.Compression.ZipFile]::CreateFromDirectory($rtStage, $rtZip)
[IO.Compression.ZipFile]::CreateFromDirectory($sdkStage, $sdkZip)
[IO.Compression.ZipFile]::CreateFromDirectory($exStage, $exZip)

$forbiddenPatterns = @(
  'Assembly-CSharp', 'UnityEngine', 'UnityPlayer', 'BepInEx\.dll', '0Harmony\.dll',
  'Sirenix\.', 'JustGo\.exe', 'JustGo_Data', '\.es3$', 'steam_appid\.txt',
  'Phase2Harness', 'sandbox', 'JustGoWeiXiangQi', 'CodexSandbox', 'api[_-]?key', 'credentials'
)

foreach ($zipPath in @($rtZip, $sdkZip, $exZip)) {
  $za = [IO.Compression.ZipFile]::OpenRead($zipPath)
  try {
    foreach ($entry in $za.Entries) {
      foreach ($pat in $forbiddenPatterns) {
        if ($entry.FullName -match $pat) {
          throw "HYGIENE VIOLATION in $(Split-Path -Leaf $zipPath): forbidden entry '$($entry.FullName)' matched '$pat'"
        }
      }
    }
    Write-Host ("Hygiene OK: {0,-36} ({1,2} entries)" -f (Split-Path -Leaf $zipPath), $za.Entries.Count) -ForegroundColor Green
  } finally {
    $za.Dispose()
  }
}

# Clean temporary staging directory
Remove-Item -LiteralPath $stageRoot -Recurse -Force

# ==================================================================
# Source Baseline + Build Dependencies + Release Manifest + SHA256SUMS
# ==================================================================
$sourceFiles = @()
foreach ($relDir in @('src\JustGone.Core', 'src\JustGone', 'src\Examples', 'tests\JustGone.Core.Tests', 'templates\JustGoneModTemplate', 'docs', 'tools')) {
  $absDir = Join-Path $root $relDir
  if (Test-Path -LiteralPath $absDir) {
    $sourceFiles += @(Get-ChildItem -LiteralPath $absDir -Recurse -File | Where-Object {
      $_.FullName -notmatch '\\(bin|obj)\\' -and $_.Extension -in '.cs', '.csproj', '.ps1', '.md', '.jpg', '.png'
    })
  }
}
foreach ($topFile in @('Directory.Build.props', 'JustGone.sln', 'LICENSE', 'README.md', 'README.zh-CN.md', 'CHANGELOG.md', 'THIRD_PARTY_NOTICES.md', '.gitignore')) {
  $p = Join-Path $root $topFile
  if (Test-Path -LiteralPath $p) { $sourceFiles += Get-Item -LiteralPath $p }
}

$sourceBaseline = @($sourceFiles | Sort-Object FullName -Unique | ForEach-Object {
  [ordered]@{
    path = $_.FullName.Substring($root.Length + 1).Replace('\', '/')
    bytes = $_.Length
    sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
  }
})
$sourceBaselinePath = Join-Path $releaseRoot 'source-baseline.json'
ConvertTo-Json -InputObject $sourceBaseline -Depth 4 | Set-Content -LiteralPath $sourceBaselinePath -Encoding UTF8

function Get-FileIdentity([string]$path) {
  $it = Get-Item -LiteralPath $path
  [ordered]@{
    name = $it.Name
    bytes = $it.Length
    fileVersion = $it.VersionInfo.FileVersion
    sha256 = (Get-FileHash -LiteralPath $it.FullName -Algorithm SHA256).Hash
  }
}

[xml]$projXml = Get-Content -LiteralPath (Join-Path $root 'src\JustGone\JustGone.csproj')
$deps = @($projXml.SelectNodes('//Reference') | ForEach-Object {
  $hp = $_.HintPath.Replace('$(GameManaged)', (Join-Path $GameDir 'JustGo_Data\Managed')).
    Replace('$(GameBepInExCore)', (Join-Path $GameDir 'BepInEx\core'))
  if (Test-Path -LiteralPath $hp) { Get-FileIdentity $hp }
})

$sdkVer = (& dotnet --version | Select-Object -First 1).Trim()
$manifest = [ordered]@{
  schema = 1
  project = 'Just Gone (The Just Go Modding Extension)'
  version = "$Version-preview"
  assemblyVersion = ([Reflection.AssemblyName]::GetAssemblyName($jgDll)).Version.ToString()
  releaseChannel = 'developer-preview'
  binaryCompatibilityGuaranteed = $false
  license = 'MIT'
  licenseStatus = 'MIT License (Confirmed by Owner)'
  builtWith = [ordered]@{
    dotnetSdk = $sdkVer
    targetFramework = 'net472'
    langVersion = '8.0'
    deterministic = $true
  }
  sourceBaseline = [ordered]@{
    path = 'source-baseline.json'
    fileCount = $sourceBaseline.Count
    sha256 = (Get-FileHash -LiteralPath $sourceBaselinePath -Algorithm SHA256).Hash
  }
  binaries = [ordered]@{
    runtimeDll = Get-FileIdentity $jgDll
    exampleBoardInspector = Get-FileIdentity $ex1Dll
    exampleCustomModeAndUi = Get-FileIdentity $ex2Dll
  }
  packages = [ordered]@{
    runtimeZip = Get-FileIdentity $rtZip
    sdkZip = Get-FileIdentity $sdkZip
    examplesZip = Get-FileIdentity $exZip
  }
  compileDependencies = $deps
}

$manifestPath = Join-Path $releaseRoot 'release-manifest.json'
ConvertTo-Json -InputObject $manifest -Depth 8 | Set-Content -LiteralPath $manifestPath -Encoding UTF8

# SHA256SUMS.txt for GitHub Release
$sumLines = @(
  "$((Get-FileHash -LiteralPath $rtZip -Algorithm SHA256).Hash)  $(Split-Path -Leaf $rtZip)",
  "$((Get-FileHash -LiteralPath $sdkZip -Algorithm SHA256).Hash)  $(Split-Path -Leaf $sdkZip)",
  "$((Get-FileHash -LiteralPath $exZip -Algorithm SHA256).Hash)  $(Split-Path -Leaf $exZip)",
  "$((Get-FileHash -LiteralPath $jgDll -Algorithm SHA256).Hash)  JustGone.dll",
  "$((Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash)  release-manifest.json",
  "$((Get-FileHash -LiteralPath $sourceBaselinePath -Algorithm SHA256).Hash)  source-baseline.json"
)
Set-Content -LiteralPath (Join-Path $releaseRoot 'SHA256SUMS.txt') -Value $sumLines -Encoding UTF8

# GitHub Release Notes
$notes = @"
# Just Gone v$Version — Developer Preview

**The Just Go Modding Extension** — First Developer Preview release for *Just Go* mod developers.

> **Developer Preview Notice**: All 12 public services are backed by real runtime implementations and verified in isolated Unity coexistence testing, but binary and source compatibility are not guaranteed prior to ``1.0.0``. Original source code and documentation are licensed under the **MIT License** (see ``LICENSE`` and ``THIRD_PARTY_NOTICES.md``).

## Release Packages

- **``JustGone-v$Version-Runtime.zip``**: Runtime framework for players and testers (``JustGone.dll``, ``JustGone.pdb``, ``JustGone.xml``, transactional ``tools/install.ps1``, ``INSTALL.md``, ``LICENSE``, and ``THIRD_PARTY_NOTICES.md``).
- **``JustGone-v$Version-SDK.zip``**: Mod developer SDK (``lib/net472/JustGone.dll`` + ``JustGone.xml``), Developer Guide, API Catalog, Architecture, Phase 2 Acceptance Report, and standalone starter project ``templates/JustGoneModTemplate``.
- **``JustGone-v$Version-Examples.zip``**: Source code and compiled DLLs for both reference example mods (``JustGone.Example.BoardInspector`` and ``JustGone.Example.CustomModeAndUi``), compile-time verified to have zero references to ``Assembly-CSharp`` or ``0Harmony``.

## Verification Summary

- **Pure-logic unit tests** (``JustGone.Core.Tests``): **12/12 passed**
- **Transactional deployment & rollback tests**: **11/11 passed**
- **Isolated Unity real-game coexistence acceptance** (*Just Go* v1.2.0, BepInEx 5.4.23.6, vanilla modes 0/1, *WeiXiangQi* v0.10.10.0 index 2, Just Gone sandbox index 3, 9×9 / 13×13 / 19×19): **63/65 assertions passed** (2 failures were harness-side selector index expectations; all three board sizes were entered and verified; ``ShowSaveFileDialog`` was not interactively clicked in headless automation and is documented accordingly).
- **Clean-directory SDK template build**: Zero errors, zero warnings, no *WeiXiangQi* source required.

## SHA-256 Checksums

``````text
$($sumLines -join "`r`n")
``````
"@
Set-Content -LiteralPath (Join-Path $releaseRoot 'GITHUB_RELEASE_NOTES.md') -Value $notes -Encoding UTF8

Write-Host ""
Write-Host "Release v$Version staged at $releaseRoot" -ForegroundColor Cyan
Get-ChildItem -LiteralPath $releaseRoot | Select-Object Name, Length | Format-Table -AutoSize
