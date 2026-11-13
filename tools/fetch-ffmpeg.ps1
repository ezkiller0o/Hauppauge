<#
.SYNOPSIS
    Downloads a Windows x64 FFmpeg build and copies ffmpeg.exe into the destination folder.

.DESCRIPTION
    Hauppauge Live Capture Studio records MP4/H.264 by starting ffmpeg.exe as a separate process.
    This script is a convenience only; you can also download FFmpeg yourself and copy ffmpeg.exe next to the
    program (see README.md).

    The default builds contain libx264 and are licensed under the GPL. They are downloaded on YOUR machine
    and are not part of this project's source. Make sure the licence suits how you use and share the result.

.PARAMETER Destination
    Folder that receives ffmpeg.exe (created when missing).

.PARAMETER Url
    Optional direct URL of a .zip that contains ffmpeg.exe (for example a build you host yourself).
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Destination,
    [string]$Url = ''
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'      # the progress bar makes Invoke-WebRequest extremely slow in Windows PowerShell 5.1
try { [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12 } catch { }

$sources = @(
    'https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-gpl.zip',
    'https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip'
)
if ($Url) { $sources = @($Url) }

$work = Join-Path ([IO.Path]::GetTempPath()) ('hlcs-ffmpeg-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $work | Out-Null

try {
    $done = $false
    foreach ($source in $sources) {
        try {
            Write-Host "Downloading FFmpeg: $source"
            $zip = Join-Path $work 'ffmpeg.zip'
            $extract = Join-Path $work 'extracted'
            if (Test-Path $extract) { Remove-Item $extract -Recurse -Force }

            Invoke-WebRequest -Uri $source -OutFile $zip -UseBasicParsing
            Expand-Archive -Path $zip -DestinationPath $extract -Force

            $found = Get-ChildItem -Path $extract -Recurse -Filter 'ffmpeg.exe' | Select-Object -First 1
            if (-not $found) { throw 'ffmpeg.exe was not found inside the downloaded archive.' }

            # the build must contain an H.264 encoder, otherwise recording cannot work
            $encoders = (& $found.FullName -hide_banner -encoders 2>&1 | Out-String)
            if (($encoders -notmatch ' libx264 ') -and ($encoders -notmatch ' h264_mf ')) {
                throw 'This FFmpeg build has no H.264 encoder (libx264 / h264_mf).'
            }

            New-Item -ItemType Directory -Path $Destination -Force | Out-Null
            Copy-Item -Path $found.FullName -Destination (Join-Path $Destination 'ffmpeg.exe') -Force
            Write-Host "ffmpeg.exe installed to: $Destination" -ForegroundColor Green
            $done = $true
            break
        }
        catch {
            Write-Warning "This source did not work: $($_.Exception.Message)"
        }
    }

    if (-not $done) {
        throw 'No FFmpeg download worked. Download a Windows x64 build with libx264 manually and copy ffmpeg.exe into the destination folder.'
    }
}
finally {
    Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
}
