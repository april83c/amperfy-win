param(
  [Parameter(Mandatory = $true)][string]$ExePath,
  [Parameter(Mandatory = $true)][string]$OutDir,
  [int]$WaitSeconds = 20,
  [string]$Arguments = ""
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
Start-Sleep -Seconds $WaitSeconds
Save-Screenshot "screenshot"
$alive = -not $p.HasExited
if ($alive) {
  Stop-Process -Id $p.Id -Force
} else {
  Write-Host "App exited with code $($p.ExitCode)"
}
# Collect crash info from the Application event log
Get-WinEvent -FilterHashtable @{LogName='Application'; StartTime=(Get-Date).AddMinutes(-10)} -ErrorAction SilentlyContinue |
  Where-Object { $_.ProviderName -in @('.NET Runtime','Application Error','Windows Error Reporting') } |
  Format-List TimeCreated, ProviderName, Id, Message | Out-File (Join-Path $OutDir "eventlog.txt")
Get-Content (Join-Path $OutDir "eventlog.txt") -ErrorAction SilentlyContinue | Select-Object -First 80
if (Test-Path $env:AMPERFY_SMOKE_LOG) { Get-Content $env:AMPERFY_SMOKE_LOG | Select-Object -Last 80 }
if (-not $alive) { throw "Amperfy exited prematurely" }
Write-Host "Smoke test passed: app was still running after $WaitSeconds s"
