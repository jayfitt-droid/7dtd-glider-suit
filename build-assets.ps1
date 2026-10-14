# Rebuilds the mod's art: Blender models and bakes into the Unity project,
# renders the inventory icon, then Unity builds the materials, prefab and asset
# bundle into Mod\GliderSuit. Run deploy.ps1 afterwards to install.
#   powershell -File build-assets.ps1 [-SkipBlender]
param([switch]$SkipBlender)
$ErrorActionPreference = 'Stop'
$repoRoot = $PSScriptRoot
$blender = 'C:\Program Files\Blender Foundation\Blender 5.2\blender.exe'
# Must match the game's engine (7DaysToDie_Data UnityPlayer.dll: 2022.3.62f2).
$unity = 'C:\Program Files\Unity\Hub\Editor\2022.3.62f2\Editor\Unity.exe'
$project = Join-Path $repoRoot 'unity\GliderSuitAssets'
$logDir = Join-Path $repoRoot 'unity'

if (-not $SkipBlender) {
    if (-not (Test-Path -LiteralPath $blender)) { throw "Blender not found: $blender" }
    $blenderLog = Join-Path $logDir 'blender.log'
    & $blender -b --factory-startup -P (Join-Path $repoRoot 'unity\blender\build_assets.py') -- (Join-Path $project 'Assets\Source') (Join-Path $logDir 'preview') (Join-Path $repoRoot 'tools\icon_master.png') *> $blenderLog
    if ($LASTEXITCODE -ne 0 -or -not (Select-String -Path $blenderLog -Pattern '\[assets\] done' -Quiet)) {
        throw "Blender build failed; see $blenderLog"
    }
    Write-Host 'Blender assets built (previews in unity\preview).'
    # The inventory icon: trim and scale the master Blender just rendered.
    & python (Join-Path $repoRoot 'tools\make_icon.py')
    if ($LASTEXITCODE -ne 0) { throw 'make_icon.py failed' }
}

if (-not (Test-Path -LiteralPath $unity)) { throw "Unity 2022.3.62f2 not found: $unity" }
$unityLog = Join-Path $logDir 'unity.log'
# None of these paths contain spaces, so Start-Process needs no extra quoting.
$args = @('-batchmode', '-projectPath', $project, '-executeMethod', 'GliderSuitEditor.GliderSuitBuild.BuildAll', '-logFile', $unityLog)
$proc = Start-Process -FilePath $unity -Wait -PassThru -NoNewWindow -ArgumentList $args
if ($proc.ExitCode -ne 0) { throw "Unity build failed (exit $($proc.ExitCode)); see $unityLog" }
Select-String -Path $unityLog -Pattern '\[GliderSuitBuild\]' | ForEach-Object { $_.Line }
