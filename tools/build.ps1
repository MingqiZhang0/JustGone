param(
  [string]$Configuration = "Release",
  [switch]$SkipTests
)
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$root = Split-Path -Parent $here

if (-not $SkipTests) {
  Write-Host "== [1/4] JustGone.Core unit tests ==" -ForegroundColor Cyan
  Push-Location (Join-Path $root 'tests\JustGone.Core.Tests')
  dotnet run -c $Configuration
  $testExit = $LASTEXITCODE
  Pop-Location
  if ($testExit -ne 0) { throw "JustGone.Core.Tests failed (exit $testExit)" }
}

Write-Host "== [2/4] Build JustGone framework (net472) ==" -ForegroundColor Cyan
dotnet build (Join-Path $root 'src\JustGone\JustGone.csproj') -c $Configuration
if ($LASTEXITCODE -ne 0) { throw "JustGone build failed" }

Write-Host "== [3/4] Build Example MODs (net472) ==" -ForegroundColor Cyan
$examples = @(
  'src\Examples\JustGone.Example.BoardInspector\JustGone.Example.BoardInspector.csproj',
  'src\Examples\JustGone.Example.CustomModeAndUi\JustGone.Example.CustomModeAndUi.csproj'
)
foreach ($proj in $examples) {
  dotnet build (Join-Path $root $proj) -c $Configuration
  if ($LASTEXITCODE -ne 0) { throw "Example build failed: $proj" }
}

Write-Host "== [4/4] Verify isolation & output SHA-256 ==" -ForegroundColor Cyan
$outputs = @(
  (Join-Path $root "src\JustGone\bin\$Configuration\JustGone.dll"),
  (Join-Path $root "src\Examples\JustGone.Example.BoardInspector\bin\$Configuration\JustGone.Example.BoardInspector.dll"),
  (Join-Path $root "src\Examples\JustGone.Example.CustomModeAndUi\bin\$Configuration\JustGone.Example.CustomModeAndUi.dll")
)

foreach ($dll in $outputs) {
  if (!(Test-Path -LiteralPath $dll)) { throw "Missing build output: $dll" }
  $item = Get-Item -LiteralPath $dll
  $hash = (Get-FileHash -LiteralPath $dll -Algorithm SHA256).Hash
  $asm = [Reflection.Assembly]::ReflectionOnlyLoadFrom($dll)
  $refs = @($asm.GetReferencedAssemblies() | ForEach-Object { $_.Name })
  if ($item.Name -like 'JustGone.Example.*') {
    if ($refs -contains 'Assembly-CSharp') { throw "ISOLATION VIOLATION: $($item.Name) references Assembly-CSharp!" }
    if ($refs -contains '0Harmony') { throw "ISOLATION VIOLATION: $($item.Name) references 0Harmony!" }
    if ($refs -contains 'JustGoWeiXiangQiP4') { throw "ISOLATION VIOLATION: $($item.Name) references JustGoWeiXiangQiP4!" }
  }
  if ($refs -contains 'JustGoWeiXiangQiP4') { throw "DEPENDENCY VIOLATION: $($item.Name) references JustGoWeiXiangQiP4!" }
  Write-Host ("  {0,-44} {1,8} B  SHA256={2}" -f $item.Name, $item.Length, $hash)
}
Write-Host "Build & isolation verification passed." -ForegroundColor Green
