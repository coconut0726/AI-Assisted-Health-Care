param(
    [string]$ConfigPath = (Join-Path $PSScriptRoot 'local-vlm.json'),
    [ValidateSet('ollama', 'gemini')][string]$Provider,
    [string]$Model,
    [string]$OllamaPath,
    [switch]$CheckOnly,
    [switch]$Cpu,
    [switch]$AutoGpu
)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$projectRoot = Split-Path -Parent $PSScriptRoot
$bundleRoot = Join-Path $projectRoot 'LocalVlm'
$settingsRoot = Join-Path $projectRoot 'UserSettings'
if (-not [Environment]::Is64BitOperatingSystem -or $env:PROCESSOR_ARCHITECTURE -eq 'ARM64') {
    throw 'This portable setup supports Windows x64. Use an x64 Windows PC for this demo.'
}
if ($Cpu -and $AutoGpu) { throw 'Choose either -Cpu or -AutoGpu.' }
$config = Get-Content -LiteralPath $ConfigPath -Raw | ConvertFrom-Json
if (-not $Provider) { $Provider = if ($config.provider) { [string]$config.provider } else { 'ollama' } }
if ($Provider -eq 'gemini' -and $CheckOnly -and [string]::IsNullOrWhiteSpace($env:GEMINI_API_KEY)) {
    throw 'GEMINI_API_KEY is missing in this process. Enter it in the VLM Backend window.'
}
if (-not $Model) { $Model = [string]$config.model }
if ([string]::IsNullOrWhiteSpace($Model) -or $Model.EndsWith('-cloud')) { throw 'Choose a downloaded local model, not a cloud model.' }
$ollamaUrl = $config.ollama_url.TrimEnd('/')
$ollamaUri = [Uri]$ollamaUrl
if ($ollamaUri.Scheme -ne 'http' -or -not $ollamaUri.IsLoopback) { throw 'Use a loopback HTTP ollama_url for this local setup.' }
$machinePath = Join-Path $settingsRoot 'local-vlm-machine.json'
if ($Cpu -or $AutoGpu) {
    if ($CheckOnly) { throw 'CheckOnly cannot change the compute setting.' }
    New-Item -ItemType Directory -Path $settingsRoot -Force | Out-Null
    [IO.File]::WriteAllText($machinePath, (@{ cpu_only = [bool]$Cpu } | ConvertTo-Json))
}
$runtimeManifest = Get-Content (Join-Path $PSScriptRoot 'local-vlm-runtime.json') -Raw | ConvertFrom-Json

# GitHub source ZIPs can contain LFS pointer text instead of the binary Unity assets.
$assetRoot = Join-Path $projectRoot 'Assets'
$missingLfs = @()
if (Test-Path -LiteralPath $assetRoot) {
    foreach ($asset in (Get-ChildItem -LiteralPath $assetRoot -Recurse -File | Where-Object { $_.Length -le 200 })) {
        if ([IO.File]::ReadAllText($asset.FullName).StartsWith('version https://git-lfs.github.com/spec/v1')) { $missingLfs += $asset.FullName }
    }
}
if ($missingLfs.Count) {
    $git = Get-Command git -ErrorAction SilentlyContinue
    if ($CheckOnly -or -not $git -or -not (Test-Path -LiteralPath (Join-Path $projectRoot '.git'))) {
        throw 'Unity binary assets are missing (Git LFS pointers). Clone with Git LFS enabled, then run git lfs pull.'
    }
    Write-Host 'Restoring missing Git LFS Unity assets ...'
    & $git.Source -c "safe.directory=$projectRoot" -C $projectRoot lfs pull
    if ($LASTEXITCODE -ne 0) { throw 'Git LFS assets could not be downloaded. Install Git LFS, check repository access, then rerun git lfs pull.' }
    foreach ($asset in $missingLfs) {
        $stream = [IO.File]::OpenRead($asset)
        try { $prefixBytes = New-Object byte[] 64; $count = $stream.Read($prefixBytes,0,64) }
        finally { $stream.Dispose() }
        if ([Text.Encoding]::UTF8.GetString($prefixBytes,0,$count).StartsWith('version https://git-lfs.github.com/spec/v1')) { throw 'Some Git LFS assets are still missing. Check repository access and rerun git lfs pull.' }
    }
}

function Get-RuntimeHash([string]$path) {
    $algorithm = [Security.Cryptography.SHA256]::Create()
    $stream = [IO.File]::OpenRead($path)
    try { return [BitConverter]::ToString($algorithm.ComputeHash($stream)).Replace('-', '').ToLowerInvariant() }
    finally { $stream.Dispose(); $algorithm.Dispose() }
}

function Expand-Runtime([string]$archive, [string]$destination) {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $resolvedDestination = [IO.Path]::GetFullPath($destination).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    $zip = [IO.Compression.ZipFile]::OpenRead($archive)
    try {
        foreach ($entry in $zip.Entries) {
            $target = [IO.Path]::GetFullPath((Join-Path $destination $entry.FullName))
            if (-not $target.StartsWith($resolvedDestination, [StringComparison]::OrdinalIgnoreCase)) { throw 'Runtime archive contains an invalid path.' }
            if (-not $entry.Name) { New-Item -ItemType Directory -Path $target -Force | Out-Null; continue }
            New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
            [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $target, $true)
        }
    } finally { $zip.Dispose() }
}

function Download-Runtime($spec, [string]$archive) {
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    New-Item -ItemType Directory -Path (Split-Path -Parent $archive) -Force | Out-Null
    if ((Test-Path -LiteralPath $archive) -and (Get-RuntimeHash $archive) -eq $spec.sha256) { return }
    Write-Host "Downloading portable runtime from $($spec.url) ..."
    $partial = $archive + '.partial'
    try {
        Invoke-WebRequest -UseBasicParsing -Uri $spec.url -OutFile $partial -TimeoutSec 3600
        if ((Get-RuntimeHash $partial) -ne $spec.sha256) {
            throw 'Runtime checksum mismatch. Retry the download; the file was not installed.'
        }
        Move-Item -LiteralPath $partial -Destination $archive -Force
    } catch {
        throw "Could not prepare the runtime. Check internet access to python.org and github.com, then rerun Apply & Start or Start-LocalVlm.cmd. $($_.Exception.Message)"
    }
}

function Find-Python {
    $candidate = Join-Path $bundleRoot 'runtime/python/python.exe'
    $candidates = @($candidate)
    foreach ($name in @('python','py')) {
        $command = Get-Command $name -ErrorAction SilentlyContinue
        if ($command -and $command.Source -notmatch 'WindowsApps') { $candidates += $command.Source }
    }
    foreach ($candidate in $candidates) {
        if (-not (Test-Path -LiteralPath $candidate -PathType Leaf)) { continue }
        $arguments = @()
        if ([IO.Path]::GetFileName($candidate) -eq 'py.exe') { $arguments = @('-3') }
        try {
            & $candidate @arguments -c 'import sys; sys.exit(0 if sys.version_info >= (3, 10) else 1)' 2>$null
            if ($LASTEXITCODE -eq 0) { return @{ path = $candidate; arguments = $arguments } }
        } catch { }
    }
    return $null
}

$python = Find-Python
if ($null -eq $python) {
    if ($CheckOnly) { throw 'Python 3.10+ is missing. Run Start-LocalVlm.cmd once to prepare it.' }
    $archive = Join-Path $bundleRoot 'downloads/python-embed.zip'
    Download-Runtime $runtimeManifest.python $archive
    $destination = Join-Path $bundleRoot 'runtime/python'
    New-Item -ItemType Directory -Path $destination -Force | Out-Null
    Expand-Runtime $archive $destination
    $python = Find-Python
    if ($null -eq $python) { throw 'Portable Python could not be started.' }
}
Write-Host 'Python is ready.'

function Get-OllamaTags {
    try { return Invoke-RestMethod -Uri "$ollamaUrl/api/tags" -TimeoutSec 2 } catch { return $null }
}

if ($Provider -eq 'ollama') {
    $tags = Get-OllamaTags
    if ($null -eq $tags) {
        if ($CheckOnly) { throw 'Ollama is not running. Run Start-LocalVlm.cmd to start it.' }
        if (-not $OllamaPath) {
            $candidates = @((Join-Path $bundleRoot 'runtime/ollama/ollama.exe'))
            $command = Get-Command ollama -ErrorAction SilentlyContinue
            if ($command) { $candidates += $command.Source }
            $candidates += (Join-Path $env:LOCALAPPDATA 'Programs/Ollama/ollama.exe')
            $OllamaPath = $candidates | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
        }
        if (-not $OllamaPath) {
            $archive = Join-Path $bundleRoot 'downloads/ollama-windows-amd64.zip'
            Download-Runtime $runtimeManifest.ollama $archive
            $destination = Join-Path $bundleRoot 'runtime/ollama'
            New-Item -ItemType Directory -Path $destination -Force | Out-Null
            Expand-Runtime $archive $destination
            $OllamaPath = Join-Path $destination 'ollama.exe'
        }
        if (-not (Test-Path -LiteralPath $OllamaPath)) { throw 'The specified Ollama executable does not exist.' }
        New-Item -ItemType Directory -Path $settingsRoot -Force | Out-Null
        New-Item -ItemType Directory -Path (Join-Path $bundleRoot 'models') -Force | Out-Null
        # Explicit stdin/stdout/stderr handles let the setup process exit independently
        # of the long-running service, including when Unity captures setup output.
        $launchCode = @'
import os, pathlib, subprocess, sys
executable, cwd, host, models, logs = sys.argv[1:]
environment = dict(os.environ, OLLAMA_HOST=host, OLLAMA_MODELS=models,
                   OLLAMA_NO_CLOUD='1', OLLAMA_NUM_PARALLEL='1')
environment.pop('GEMINI_API_KEY', None)
with open(pathlib.Path(logs) / 'ollama.out.log', 'ab') as output, open(pathlib.Path(logs) / 'ollama.err.log', 'ab') as errors:
    process = subprocess.Popen([executable, 'serve'], cwd=cwd, env=environment,
                               stdin=subprocess.DEVNULL, stdout=output, stderr=errors,
                               close_fds=True, creationflags=subprocess.CREATE_NO_WINDOW)
print(process.pid)
'@
        $pythonPath = $python.path; $pythonArgs = $python.arguments
        $serviceId = $launchCode | & $pythonPath @pythonArgs - $OllamaPath $projectRoot $ollamaUri.Authority (Join-Path $bundleRoot 'models') $settingsRoot
        if ($LASTEXITCODE -ne 0 -or $serviceId -notmatch '^\d+$') { throw 'Ollama could not be launched.' }
        $process = [Diagnostics.Process]::GetProcessById([int]$serviceId)
        try {
            $deadline = [DateTime]::UtcNow.AddSeconds(60)
            while ($null -eq $tags -and [DateTime]::UtcNow -lt $deadline) {
                Start-Sleep -Milliseconds 250
                $tags = Get-OllamaTags
                if ($process.HasExited -and $null -eq $tags) { throw 'Ollama could not start.' }
            }
        } finally { $process.Dispose() }
        if ($null -eq $tags) { throw 'Ollama did not become available within 60 seconds.' }
    }
    if (@($tags.models | ForEach-Object { $_.name }) -notcontains $Model) {
        if ($CheckOnly) { throw "Model '$Model' is missing. Run Start-LocalVlm.cmd to download it." }
        Write-Host "Downloading $Model into the active Ollama model store. This can take several minutes ..."
        $pull = Invoke-RestMethod -Uri "$ollamaUrl/api/pull" -Method Post -ContentType 'application/json' -Body (@{ name=$Model; stream=$false } | ConvertTo-Json) -TimeoutSec 14400
        if ($pull.error) { throw "Model download failed: $($pull.error)" }
        $tags = Get-OllamaTags
        if (@($tags.models | ForEach-Object { $_.name }) -notcontains $Model) { throw 'The model download did not complete. Rerun to resume it.' }
    }
    Write-Host "Local model is installed: $Model"
    $gpuCommand = Get-Command nvidia-smi -ErrorAction SilentlyContinue
    if ($gpuCommand) {
        $driver = (& $gpuCommand.Source --query-gpu=driver_version --format=csv,noheader | Select-Object -First 1)
        if ($LASTEXITCODE -eq 0 -and $driver -match '^\d+\.\d+$' -and [version]$driver -lt [version]'551.61') {
            Write-Warning "NVIDIA driver $driver is older than Ollama's Windows requirement (551.61). Update the driver, or select CPU mode with Start-LocalVlm.cmd -Cpu."
        }
    }
}

if (-not $CheckOnly) {
    New-Item -ItemType Directory -Path $settingsRoot -Force | Out-Null
    [IO.File]::WriteAllText((Join-Path $settingsRoot 'local-vlm-python.json'), ($python | ConvertTo-Json))
}
Write-Host 'Dependencies ready. Model installation alone does not confirm that inference works.'
