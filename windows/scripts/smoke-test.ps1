param(
  [Parameter(Mandatory = $true)][string]$ExePath,
  [Parameter(Mandatory = $true)][string]$OutDir,
  [int]$WaitSeconds = 20,
  [string]$Arguments = "",
  # Tour mode: the app exits by itself when done; wait up to this many seconds for the exit.
  [int]$ExitTimeoutSeconds = 0
)
$ErrorActionPreference = "Stop"
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$OutDir = (Resolve-Path $OutDir).Path

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

function Save-Screenshot([string]$name) {
  $bounds = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
  $bmp = New-Object System.Drawing.Bitmap $bounds.Width, $bounds.Height
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.CopyFromScreen($bounds.Location, [System.Drawing.Point]::Empty, $bounds.Size)
  $bmp.Save((Join-Path $OutDir "$name.png"), [System.Drawing.Imaging.ImageFormat]::Png)
  $g.Dispose(); $bmp.Dispose()
}

$env:AMPERFY_SMOKE_LOG = Join-Path $OutDir "app.log"
if ($Arguments) {
  $p = Start-Process -FilePath $ExePath -ArgumentList $Arguments -PassThru
} else {
  $p = Start-Process -FilePath $ExePath -PassThru
}
if ($ExitTimeoutSeconds -gt 0) {
  $exited = $p.WaitForExit($ExitTimeoutSeconds * 1000)
  Save-Screenshot "screenshot"
  if (-not $exited) {
    Stop-Process -Id $p.Id -Force
    Write-Host "App did not exit within $ExitTimeoutSeconds s"
  }
  $alive = $true
  $tourFailed = -not $exited -or $p.ExitCode -ne 0
} else {
  Start-Sleep -Seconds $WaitSeconds
  Save-Screenshot "screenshot"
  $alive = -not $p.HasExited
  $tourFailed = $false
  if ($alive) {
    Stop-Process -Id $p.Id -Force
  } else {
    Write-Host "App exited with code $($p.ExitCode)"
  }
}
# Collect crash info from the Application event log
Get-WinEvent -FilterHashtable @{LogName='Application'; StartTime=(Get-Date).AddMinutes(-10)} -ErrorAction SilentlyContinue |
  Where-Object { $_.ProviderName -in @('.NET Runtime','Application Error','Windows Error Reporting') } |
  Format-List TimeCreated, ProviderName, Id, Message | Out-File (Join-Path $OutDir "eventlog.txt")
Get-Content (Join-Path $OutDir "eventlog.txt") -ErrorAction SilentlyContinue | Select-Object -First 80
if (Test-Path $env:AMPERFY_SMOKE_LOG) { Get-Content $env:AMPERFY_SMOKE_LOG | Select-Object -Last 80 }
if (-not $alive) { throw "Amperfy exited prematurely" }
if ($tourFailed) { throw "Screenshot tour did not finish cleanly (exit code $($p.ExitCode))" }
Write-Host "Smoke test passed: app was still running after $WaitSeconds s"
