# Run in PowerShell 7: pwsh -NoProfile -File Tools/Test-RequirementsPrototype.ps1
# Compiles and executes the pure requirement validation/aggregation checks without Unity or API keys.
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$sourcePaths = @(
    'Assets/_Project/Runtime/Procedures/ProcedureRequirements.cs',
    'Assets/_Project/Runtime/Procedures/RequirementsJson.cs',
    'Assets/_Project/Runtime/Procedures/RequirementsPrompts.cs',
    'Assets/_Project/Runtime/Procedures/ProcedureSession.cs',
    'Assets/_Project/Runtime/Procedures/GeminiVerificationProtocol.cs',
    'Assets/_Project/Editor/Authoring/RequirementsPrototypeChecks.cs',
    'Assets/_Project/Editor/Authoring/ARPrototypeChecks.cs'
) | ForEach-Object { Join-Path $projectRoot $_ }
$referencePaths = @((Get-ChildItem -LiteralPath (Join-Path $PSHOME 'ref') -Filter '*.dll').FullName)
$referencePaths += [Newtonsoft.Json.JsonConvert].Assembly.Location
# PowerShell can bundle Newtonsoft built for an earlier compatible System.Runtime.
Add-Type -Path $sourcePaths -ReferencedAssemblies $referencePaths -CompilerOptions '/nowarn:1701'
$examplePath = Join-Path $projectRoot 'Assets/_Project/Content/Procedures/requirements_example.json'
$passed = [HomeCare.Authoring.RequirementsPrototypeChecks]::Run($examplePath)
Write-Output "$passed requirement validation and verification policy checks passed."
$arExamplePath = Join-Path $projectRoot 'Assets/_Project/Content/Procedures/ar_example.json'
$arPassed = [HomeCare.Authoring.ARPrototypeChecks]::Run($arExamplePath)
Write-Output "$arPassed AR step progression and verification protocol checks passed."

# Syntax-check every custom script, including EditorWindow and transport code.
# This does not substitute for compilation against the installed Unity Editor assemblies.
$parseOptions = [Microsoft.CodeAnalysis.CSharp.CSharpParseOptions]::Default.WithPreprocessorSymbols([string[]]@('UNITY_EDITOR'))
$scriptPaths = Get-ChildItem -LiteralPath (Join-Path $projectRoot 'Assets/_Project') -Recurse -Filter '*.cs'
foreach ($script in $scriptPaths) {
    $tree = [Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree]::ParseText([string](Get-Content -LiteralPath $script.FullName -Raw), $parseOptions)
    $errors = @($tree.GetDiagnostics() | Where-Object { $_.Severity.ToString() -eq 'Error' })
    if ($errors.Count -gt 0) { throw "$($script.Name): $($errors -join [Environment]::NewLine)" }
}
Write-Output "$($scriptPaths.Count) custom C# scripts passed syntax checks."
