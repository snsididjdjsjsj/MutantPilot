[CmdletBinding()]
param(
    [string]$GameDirectory = 'C:\Program Files (x86)\Steam\steamapps\common\tModLoader',
    [switch]$Windowed,
    [ValidateSet(0, 1, 2)]
    [Nullable[int]]$FrameSkipMode,
    [string]$PlayerName,
    [string]$WorldName
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrEmpty($PlayerName) -ne [string]::IsNullOrEmpty($WorldName)) {
    throw 'Specify both PlayerName and WorldName, or neither.'
}
$gameRoot = (Resolve-Path -LiteralPath $GameDirectory).Path
$runtimePath = Join-Path $gameRoot 'dotnet\dotnet.exe'
$assemblyPath = Join-Path $gameRoot 'tModLoader.dll'
foreach ($requiredPath in @($runtimePath, $assemblyPath)) {
    if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
        throw "Missing tModLoader file: $requiredPath"
    }
}

$runningGame = Get-CimInstance Win32_Process -Filter "Name = 'dotnet.exe'" |
    Where-Object { $_.ExecutablePath -eq $runtimePath -and $_.CommandLine -match 'tModLoader\.dll' }
if ($runningGame) {
    throw 'tModLoader is already running. Use the existing game window.'
}

if ($Windowed -or $PSBoundParameters.ContainsKey('FrameSkipMode')) {
    $saveRoot = Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'My Games\Terraria\tModLoader'
    $configPath = Join-Path $saveRoot 'config.json'
    if (Test-Path -LiteralPath $configPath) {
        $backupPath = "$configPath.before-windowed-$(Get-Date -Format 'yyyyMMdd-HHmmss-fff')"
        Copy-Item -LiteralPath $configPath -Destination $backupPath
        $settings = Get-Content -LiteralPath $configPath -Raw | ConvertFrom-Json
        if ($Windowed) { foreach ($setting in @{
            Fullscreen = $false
            WindowBorderless = $false
            WindowMaximized = $false
            DisplayWidth = 1280
            DisplayHeight = 720
            ThrottleWhenInactive = $false
        }.GetEnumerator()) {
            $settings | Add-Member -MemberType NoteProperty -Name $setting.Key -Value $setting.Value -Force
        } }
        if ($PSBoundParameters.ContainsKey('FrameSkipMode')) {
            $settings | Add-Member -MemberType NoteProperty -Name 'FrameSkipMode' -Value $FrameSkipMode -Force
        }
        [IO.File]::WriteAllText($configPath, ($settings | ConvertTo-Json -Depth 30),
            (New-Object Text.UTF8Encoding($false)))
        Write-Output "Display settings backed up to $backupPath"
    }
}

# Keep the bundled runtime attached to this terminal, matching the verified launch.
# Steam should already be open; game licensing and Workshop loading are unchanged.
Push-Location -LiteralPath $gameRoot
try {
    if ($PlayerName) {
        & $runtimePath $assemblyPath -skipselect "${PlayerName}:$WorldName"
    }
    else {
        & $runtimePath $assemblyPath
    }
    $gameExitCode = $LASTEXITCODE
}
finally {
    Pop-Location
}
exit $gameExitCode
