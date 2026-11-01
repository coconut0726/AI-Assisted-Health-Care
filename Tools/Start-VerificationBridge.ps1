param(
    [switch]$Usb,
    [string]$AdbPath = 'adb',
    [string]$HostAddress = '127.0.0.1',
    [int]$Port = 8765
)
$ErrorActionPreference = 'Stop'
if ($Usb) {
    & $AdbPath reverse "tcp:$Port" "tcp:$Port"
    if ($LASTEXITCODE -ne 0) { throw 'Could not configure USB forwarding. Connect one headset and enable USB debugging.' }
}
if ([string]::IsNullOrWhiteSpace($env:GEMINI_API_KEY)) {
    Write-Warning 'GEMINI_API_KEY is not set in this PowerShell process. The bridge health endpoint will work, but verification will return an error.'
}
& python (Join-Path $PSScriptRoot 'verification_bridge.py') --host $HostAddress --port $Port
exit $LASTEXITCODE
