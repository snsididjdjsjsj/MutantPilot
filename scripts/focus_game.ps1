# Bring the tModLoader window to the foreground. ASCII only.
#
# SetForegroundWindow alone is rejected by the shell's foreground lock when the caller is a
# background process, which is what made runs fail with "No acknowledgement". Attaching to
# both the current foreground thread and the game thread first is what actually works here.
Add-Type -Namespace FG -Name W -MemberDefinition @'
[DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
[DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
[DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
[DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
[DllImport("user32.dll")] public static extern bool AttachThreadInput(uint a, uint b, bool f);
[DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr h);
[DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int c);
[DllImport("user32.dll")] public static extern IntPtr SetActiveWindow(IntPtr h);
[DllImport("user32.dll")] public static extern IntPtr SetFocus(IntPtr h);
'@

$proc = Get-Process | Where-Object { $_.ProcessName -eq 'dotnet' -and $_.MainWindowHandle -ne 0 } |
    Sort-Object StartTime -Descending | Select-Object -First 1
if (-not $proc) { Write-Output 'no game window'; exit 1 }

$h = $proc.MainWindowHandle
$fg = [FG.W]::GetForegroundWindow()
if ($fg -eq $h) { Write-Output 'already focused'; exit 0 }

$pidFg = [uint32]0
$pidGame = [uint32]0
$tFg = [FG.W]::GetWindowThreadProcessId($fg, [ref]$pidFg)
$tGame = [FG.W]::GetWindowThreadProcessId($h, [ref]$pidGame)
$tMe = [FG.W]::GetCurrentThreadId()

[void][FG.W]::AttachThreadInput($tMe, $tFg, $true)
[void][FG.W]::AttachThreadInput($tMe, $tGame, $true)
[void][FG.W]::ShowWindow($h, 9)
[void][FG.W]::BringWindowToTop($h)
[void][FG.W]::SetForegroundWindow($h)
[void][FG.W]::SetActiveWindow($h)
[void][FG.W]::SetFocus($h)
[void][FG.W]::AttachThreadInput($tMe, $tGame, $false)
[void][FG.W]::AttachThreadInput($tMe, $tFg, $false)
Start-Sleep -Milliseconds 250
Write-Output ("focused=" + ([FG.W]::GetForegroundWindow() -eq $h))
