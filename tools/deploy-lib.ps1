function Invoke-JustGoneDeployment {
  param(
    [Parameter(Mandatory)][string]$GameDir,
    [ValidateSet('Install','Remove')][string]$Mode = 'Install',
    [string]$PluginName = 'JustGone',
    [string]$SourceDll,
    [string]$ExpectedSha256,
    [string]$ExpectedVersion,
    [switch]$Development,
    [scriptblock]$Checkpoint = {}
  )
  $ErrorActionPreference = 'Stop'
  $game = Get-Item -LiteralPath $GameDir -Force
  $bep = Join-Path $game.FullName 'BepInEx'
  $plugins = Join-Path $bep 'plugins'
  $archive = Join-Path $bep 'JustGoneDeploymentHistory'

  foreach ($path in @($game.FullName, $bep, $plugins, $archive)) {
    if (Test-Path -LiteralPath $path) {
      $item = Get-Item -LiteralPath $path -Force
      if (!$item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        throw "Expected a regular directory: $path"
      }
    }
  }
  if (!(Test-Path -LiteralPath (Join-Path $bep 'core/BepInEx.dll') -PathType Leaf)) {
    throw "BepInEx installation not found: $bep"
  }
  if (!(Test-Path -LiteralPath $plugins -PathType Container)) { throw "Plugin directory missing: $plugins" }
  if (Get-Process -Name JustGo -ErrorAction SilentlyContinue) { throw 'Close Just Go before deployment.' }

  # Never touch WeiXiangQi P4 directories
  if ($PluginName -like 'JustGoWeiXiangQi*') {
    throw "Just Gone deployment tools must not modify WeiXiangQi plugin directories: $PluginName"
  }

  $targetDir = Join-Path $plugins $PluginName
  $sourceHash = $null
  $version = $null

  if ($Mode -eq 'Install') {
    if (!$SourceDll -or !(Test-Path -LiteralPath $SourceDll -PathType Leaf)) {
      throw 'Specify an existing DLL with -SourceDll.'
    }
    $source = Get-Item -LiteralPath $SourceDll
    $assembly = [Reflection.AssemblyName]::GetAssemblyName($source.FullName)
    if ($assembly.Name -ne $PluginName) { throw "Unexpected plugin assembly: $($assembly.Name) (expected $PluginName)" }
    $version = $assembly.Version.ToString()
    if (!$Development -and
        ($ExpectedVersion -notmatch '^\d+\.\d+\.\d+\.\d+$' -or $ExpectedSha256 -notmatch '^[0-9a-fA-F]{64}$')) {
      throw 'Formal deployment requires an explicit expected version and SHA256.'
    }
    if ($ExpectedVersion -and $version -ne $ExpectedVersion) {
      throw "Source assembly version ($version) does not match expected ($ExpectedVersion)."
    }
    $sourceHash = (Get-FileHash -LiteralPath $source.FullName -Algorithm SHA256).Hash
    if ($ExpectedSha256 -and $sourceHash -ne $ExpectedSha256) {
      throw 'Source SHA256 does not match the release identity.'
    }
  }

  [IO.Directory]::CreateDirectory($archive) | Out-Null
  $lockPath = Join-Path $archive 'deployment.lock'
  $lock = [IO.File]::Open($lockPath, [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
  try {
    if (Test-Path -LiteralPath $targetDir) {
      $item = Get-Item -LiteralPath $targetDir -Force
      if (!$item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        throw "Expected a regular plugin directory: $targetDir"
      }
    }
    $duplicates = @(Get-ChildItem -LiteralPath $plugins -Recurse -Force -File -Filter "$PluginName.dll" |
      Where-Object { $_.DirectoryName -ne $targetDir })
    if ($duplicates.Count) { throw "Unexpected duplicate $PluginName.dll under plugins." }

    $transaction = Join-Path $archive ((Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [guid]::NewGuid().ToString('N'))
    $staged = Join-Path $transaction 'staged'
    [IO.Directory]::CreateDirectory($transaction) | Out-Null
    $archivedOld = $false
    $published = $false
    try {
      if ($Mode -eq 'Install') {
        [IO.Directory]::CreateDirectory($staged) | Out-Null
        $stageDll = Join-Path $staged "$PluginName.dll"
        & $Checkpoint 'stage-copy' $staged
        Copy-Item -LiteralPath $source.FullName -Destination $stageDll
        $pdb = [IO.Path]::ChangeExtension($source.FullName, '.pdb')
        if (Test-Path -LiteralPath $pdb -PathType Leaf) {
          Copy-Item -LiteralPath $pdb -Destination (Join-Path $staged "$PluginName.pdb")
        }
        $xml = [IO.Path]::ChangeExtension($source.FullName, '.xml')
        if (Test-Path -LiteralPath $xml -PathType Leaf) {
          Copy-Item -LiteralPath $xml -Destination (Join-Path $staged "$PluginName.xml")
        }
        & $Checkpoint 'stage-verify' $staged
        if ((Get-FileHash -LiteralPath $stageDll -Algorithm SHA256).Hash -ne $sourceHash) {
          throw 'Staged DLL SHA256 mismatch.'
        }
        [ordered]@{ plugin = $PluginName; version = $version; sha256 = $sourceHash; archive = $transaction } |
          ConvertTo-Json | Set-Content -LiteralPath (Join-Path $staged 'install-receipt.json') -Encoding UTF8
      }

      if (Get-Process -Name JustGo -ErrorAction SilentlyContinue) { throw 'Just Go started during staging.' }
      if (Test-Path -LiteralPath $targetDir) {
        & $Checkpoint "archive-$PluginName" $targetDir
        [IO.Directory]::Move($targetDir, (Join-Path $transaction $PluginName))
        $archivedOld = $true
      }
      if ($Mode -eq 'Install') {
        & $Checkpoint 'publish' $targetDir
        [IO.Directory]::Move($staged, $targetDir)
        $published = $true
        & $Checkpoint 'installed-verify' $targetDir
        if ((Get-FileHash -LiteralPath (Join-Path $targetDir "$PluginName.dll") -Algorithm SHA256).Hash -ne $sourceHash) {
          throw 'Installed DLL SHA256 mismatch.'
        }
      } elseif (Test-Path -LiteralPath $targetDir) {
        throw "$PluginName remains installed."
      }
    } catch {
      $failure = $_
      $restoreErrors = @()
      if ($published) {
        try { [IO.Directory]::Move($targetDir, $staged) } catch { $restoreErrors += $_.Exception.Message }
      }
      if ($archivedOld) {
        try { [IO.Directory]::Move((Join-Path $transaction $PluginName), $targetDir) } catch { $restoreErrors += $_.Exception.Message }
      }
      if ($restoreErrors.Count) {
        throw "Deployment failed: $failure. Automatic recovery incomplete. Backups: $transaction. $restoreErrors"
      }
      throw "Deployment failed; previous plugin directory restored. $failure"
    }
    [pscustomobject]@{ mode = $Mode; plugin = $PluginName; version = $version; sha256 = $sourceHash; archive = $transaction }
  } finally {
    $lock.Dispose()
  }
}
