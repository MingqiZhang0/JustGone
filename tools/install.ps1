param(
  [string]$GameDir = "F:\Steam\steamapps\common\Just Go",
  [string]$PluginName = "JustGone",
  [string]$From,
  [string]$ExpectedVersion,
  [string]$ExpectedSha256,
  [switch]$Development
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'deploy-lib.ps1')

if (!$From -or !(Test-Path -LiteralPath $From -PathType Leaf)) {
  throw 'Specify an existing DLL with -From.'
}
if ($Development -and !$PSBoundParameters.ContainsKey('GameDir')) {
  throw 'Development installs require an explicit isolated test -GameDir.'
}
Invoke-JustGoneDeployment -GameDir $GameDir -Mode Install -PluginName $PluginName -SourceDll $From -ExpectedVersion $ExpectedVersion -ExpectedSha256 $ExpectedSha256 -Development:$Development
