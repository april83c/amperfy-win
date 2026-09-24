# Starts a Navidrome server (Subsonic API) with the test library on a Windows CI runner.
# Usage: ./start-navidrome.ps1 -MusicDir <dir> -WorkDir <dir> [-Port 4533]
param(
  [Parameter(Mandatory = $true)][string]$MusicDir,
  [Parameter(Mandatory = $true)][string]$WorkDir,
  [int]$Port = 4533,
  [string]$User = "admin",
  [string]$Password = "amperfy-test",
  # Pinned release: a direct download needs no (rate limited) GitHub API call
  [string]$Version = "0.64.1"
)
$ErrorActionPreference = "Stop"
New-Item -ItemType Directory -Force -Path $WorkDir | Out-Null
$WorkDir = (Resolve-Path $WorkDir).Path
$MusicDir = (Resolve-Path $MusicDir).Path

$assetName = "navidrome_${Version}_windows_amd64.zip"
$url = "https://github.com/navidrome/navidrome/releases/download/v$Version/$assetName"
Write-Host "Downloading $url"
$zip = Join-Path $WorkDir "navidrome.zip"
for ($i = 1; $i -le 3; $i++) {
  try { Invoke-WebRequest -Uri $url -OutFile $zip; break }
  catch { if ($i -eq 3) { throw }; Start-Sleep -Seconds (5 * $i) }
}
Expand-Archive -Path $zip -DestinationPath (Join-Path $WorkDir "bin") -Force
$exe = Get-ChildItem (Join-Path $WorkDir "bin") -Recurse -Filter "navidrome.exe" | Select-Object -First 1

$env:ND_MUSICFOLDER = $MusicDir
$env:ND_DATAFOLDER = Join-Path $WorkDir "data"
$env:ND_PORT = "$Port"
$env:ND_ADDRESS = "127.0.0.1"
$env:ND_LOGLEVEL = "info"
$env:ND_SCANNER_SCHEDULE = "0"
$env:ND_ENABLETRANSCODINGCONFIG = "true"
New-Item -ItemType Directory -Force -Path $env:ND_DATAFOLDER | Out-Null
$log = Join-Path $WorkDir "navidrome.log"
Start-Process -FilePath $exe.FullName -RedirectStandardOutput $log -RedirectStandardError "$log.err" -WindowStyle Hidden

$base = "http://127.0.0.1:$Port"
for ($i = 0; $i -lt 60; $i++) {
  try { Invoke-WebRequest -Uri "$base/ping" -UseBasicParsing | Out-Null; break } catch { Start-Sleep -Seconds 1 }
}
# Create the first (admin) user
Invoke-RestMethod -Method Post -Uri "$base/auth/createAdmin" -ContentType "application/json" -Body (@{ username = $User; password = $Password } | ConvertTo-Json) | Out-Null
# Trigger a full scan and wait until it finished
$salt = "abc123"
$md5 = [System.Security.Cryptography.MD5]::Create()
$token = -join ($md5.ComputeHash([System.Text.Encoding]::UTF8.GetBytes("$Password$salt")) | ForEach-Object { $_.ToString("x2") })
$auth = "u=$User&t=$token&s=$salt&v=1.16.1&c=ci&f=json"
Invoke-RestMethod -Uri "$base/rest/startScan?$auth&fullScan=true" | Out-Null
for ($i = 0; $i -lt 60; $i++) {
  $status = Invoke-RestMethod -Uri "$base/rest/getScanStatus?$auth"
  if (-not $status.'subsonic-response'.scanStatus.scanning) { break }
  Start-Sleep -Seconds 1
}
$albums = Invoke-RestMethod -Uri "$base/rest/getAlbumList2?$auth&type=alphabeticalByName&size=50"
Write-Host "Navidrome ready at $base with $($albums.'subsonic-response'.albumList2.album.Count) albums"
