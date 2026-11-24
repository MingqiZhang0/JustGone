param(
  [string]$Configuration = "Release"
)
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$root = Split-Path -Parent $here
. (Join-Path $here 'deploy-lib.ps1')

$dllPath = Join-Path $root "src\JustGone\bin\$Configuration\JustGone.dll"
if (!(Test-Path -LiteralPath $dllPath)) { throw "Build JustGone first: $dllPath" }
$dllHash = (Get-FileHash -LiteralPath $dllPath -Algorithm SHA256).Hash
$dllVer = ([Reflection.AssemblyName]::GetAssemblyName($dllPath)).Version.ToString()

$tmp = Join-Path ([IO.Path]::GetTempPath()) ("justgone_deploy_test_" + [guid]::NewGuid().ToString('N'))
$bepCore = Join-Path $tmp 'BepInEx\core'
$plugins = Join-Path $tmp 'BepInEx\plugins'
$wxqDir = Join-Path $plugins 'JustGoWeiXiangQiP4'
[IO.Directory]::CreateDirectory($bepCore) | Out-Null
[IO.Directory]::CreateDirectory($wxqDir) | Out-Null
Set-Content -LiteralPath (Join-Path $bepCore 'BepInEx.dll') -Value 'stub' -Encoding ASCII
Set-Content -LiteralPath (Join-Path $wxqDir 'JustGoWeiXiangQiP4.dll') -Value 'wxq-untouched' -Encoding ASCII

$passed = 0
function Assert-True([bool]$cond, [string]$msg) {
  if (!$cond) { throw "ASSERT FAILED: $msg" }
  $script:passed++
}

try {
  # 1) Reject formal install without identity
  $threw = $false
  try { Invoke-JustGoneDeployment -GameDir $tmp -Mode Install -PluginName JustGone -SourceDll $dllPath }
  catch { $threw = $true }
  Assert-True $threw "Formal install without version/sha256 rejected"

  # 2) Reject wrong SHA256
  $threw = $false
  try { Invoke-JustGoneDeployment -GameDir $tmp -Mode Install -PluginName JustGone -SourceDll $dllPath -ExpectedVersion $dllVer -ExpectedSha256 ('0' * 64) }
  catch { $threw = $true }
  Assert-True $threw "Wrong SHA256 rejected"

  # 3) Reject touching WeiXiangQi plugin directory
  $threw = $false
  try { Invoke-JustGoneDeployment -GameDir $tmp -Mode Remove -PluginName JustGoWeiXiangQiP4 }
  catch { $threw = $true }
  Assert-True $threw "Refuses to touch JustGoWeiXiangQiP4"

  # 4) Formal install succeeds with exact identity + receipt + untouched WeiXiangQi
  $res = Invoke-JustGoneDeployment -GameDir $tmp -Mode Install -PluginName JustGone -SourceDll $dllPath -ExpectedVersion $dllVer -ExpectedSha256 $dllHash
  Assert-True ($res.sha256 -eq $dllHash) "Receipt hash matches"
  Assert-True (Test-Path -LiteralPath (Join-Path $plugins 'JustGone\JustGone.dll')) "JustGone.dll installed"
  Assert-True (Test-Path -LiteralPath (Join-Path $plugins 'JustGone\install-receipt.json')) "install-receipt.json written"
  Assert-True ((Get-Content -LiteralPath (Join-Path $wxqDir 'JustGoWeiXiangQiP4.dll') -Raw).Trim() -eq 'wxq-untouched') "WeiXiangQi untouched"

  # 5) Simulated failure after publish triggers full rollback restoring previous JustGone directory
  $threw = $false
  try {
    Invoke-JustGoneDeployment -GameDir $tmp -Mode Install -PluginName JustGone -SourceDll $dllPath -Development -Checkpoint {
      param($stage, $path)
      if ($stage -eq 'installed-verify') { throw 'injected post-publish failure' }
    }
  } catch { $threw = $true }
  Assert-True $threw "Injected post-publish failure threw"
  Assert-True ((Get-FileHash -LiteralPath (Join-Path $plugins 'JustGone\JustGone.dll') -Algorithm SHA256).Hash -eq $dllHash) "Previous JustGone.dll restored on rollback"

  # 6) Clean removal leaves WeiXiangQi untouched
  Invoke-JustGoneDeployment -GameDir $tmp -Mode Remove -PluginName JustGone | Out-Null
  Assert-True (!(Test-Path -LiteralPath (Join-Path $plugins 'JustGone'))) "JustGone removed"
  Assert-True ((Get-Content -LiteralPath (Join-Path $wxqDir 'JustGoWeiXiangQiP4.dll') -Raw).Trim() -eq 'wxq-untouched') "WeiXiangQi still untouched after remove"

  Write-Host "Deployment transaction tests passed: $passed assertions." -ForegroundColor Green
} finally {
  try { Remove-Item -LiteralPath $tmp -Recurse -Force } catch { }
}
