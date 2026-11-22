param(
    [string]$ConfigPath = (Join-Path $PSScriptRoot 'local-vlm.json'),
    [string]$OllamaPath,
    [ValidateSet('ollama', 'gemini')][string]$Provider,
    [switch]$CheckOnly,
    [switch]$Cpu,
    [switch]$AutoGpu,
    [switch]$SkipInferenceCheck,
    [switch]$Usb,
    [string]$AdbPath = 'adb'
)
$ErrorActionPreference = 'Stop'
if (-not $Provider) {
    $startupConfig = Get-Content -LiteralPath $ConfigPath -Raw | ConvertFrom-Json
    $Provider = if ($startupConfig.provider) { [string]$startupConfig.provider } else { 'ollama' }
}
# Shared with the Editor: first launch downloads missing portable runtimes and model.
. (Join-Path $PSScriptRoot 'Setup-LocalVlm.ps1') -ConfigPath $ConfigPath -Provider $Provider -OllamaPath $OllamaPath -CheckOnly:$CheckOnly -Cpu:$Cpu -AutoGpu:$AutoGpu
if ($CheckOnly) { Write-Host 'Configuration check passed (model inference not run).'; exit 0 }
$pythonPath = $python.path
$pythonArguments = $python.arguments
$port = [int]$config.bridge_port
$runtimeConfigPath = Join-Path $settingsRoot 'local-vlm-runtime.json'
if (Test-Path -LiteralPath $machinePath) {
    $machine = Get-Content -LiteralPath $machinePath -Raw | ConvertFrom-Json
    if ($machine.cpu_only) { $config | Add-Member -NotePropertyName num_gpu -NotePropertyValue 0 -Force }
}
$config.provider = $Provider
[IO.File]::WriteAllText($runtimeConfigPath, ($config | ConvertTo-Json -Depth 8))
if ($Provider -eq 'gemini' -and [string]::IsNullOrWhiteSpace($env:GEMINI_API_KEY)) {
    $secret = Read-Host 'Gemini API key (this session only)' -AsSecureString
    $secretPointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secret)
    try { $env:GEMINI_API_KEY = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($secretPointer) }
    finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($secretPointer); $secret.Dispose() }
    if ([string]::IsNullOrWhiteSpace($env:GEMINI_API_KEY)) { throw 'A Gemini API key is required.' }
}
if ($Usb) {
    & $AdbPath reverse "tcp:$port" "tcp:$port"
    if ($LASTEXITCODE -ne 0) { throw 'USB forwarding failed.' }
}
$health = $null
try { $health = Invoke-RestMethod -Uri "http://127.0.0.1:$port/health" -TimeoutSec 2 } catch {}
$modelName = if ($Provider -eq 'gemini') { [string]$config.gemini_model } else { [string]$config.model }
if ($health) {
    $cpuOnly = $config.PSObject.Properties.Name -contains 'num_gpu' -and $config.num_gpu -eq 0
    if ($health.provider -ne $Provider -or $health.model -ne $modelName -or
        ($Provider -eq 'ollama' -and [bool]$health.cpu_only -ne $cpuOnly)) {
        throw 'A bridge with different settings is already running. Stop it in Tools > Home Care > VLM Backend, then start again.'
    }
    Write-Host "Bridge is already running at http://127.0.0.1:$port/verify."
    exit 0
}
if ($Provider -eq 'ollama' -and -not $SkipInferenceCheck) {
    Write-Host 'Checking actual local image inference. First loading can take a minute or more ...'
    & $pythonPath @pythonArguments (Join-Path $PSScriptRoot 'verification_bridge.py') --config $runtimeConfigPath --check-local
    if ($LASTEXITCODE -ne 0) { throw 'Local inference is not ready. See the message above; the bridge was not started.' }
}
Write-Host "Ready for Unity: open VRGuidanceDemo, connect Quest Link, then Play. Keep this window open."
& $pythonPath @pythonArguments (Join-Path $PSScriptRoot 'verification_bridge.py') --provider $Provider --config $runtimeConfigPath --port $port
if ($LASTEXITCODE -ne 0) { throw "Bridge exited with code $LASTEXITCODE." }
