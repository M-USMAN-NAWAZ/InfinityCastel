param([ValidateSet('Test', 'Preview', 'Install', 'Exterior')][string]$Mode = 'Test')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$project = Join-Path $root 'Temp/ArchitectureValidation'
$project = [IO.Path]::GetFullPath($project)
if (!$project.StartsWith([IO.Path]::GetFullPath($root) + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Validation project must stay inside this workspace.' }
New-Item -ItemType Directory -Force -Path "$project/Assets", "$project/Packages", "$project/ProjectSettings", "$project/Library" | Out-Null
foreach ($folder in @('Assets', 'Packages', 'ProjectSettings', 'Library/PackageCache')) {
    $sync = if ($folder -eq 'Assets') { '/MIR' } else { '/E' }
    & robocopy "$root/$folder" "$project/$folder" $sync /MT:16 /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "Copy failed: $folder" }
}
$log = Join-Path $root "Temp/building-architecture-$($Mode.ToLower()).log"
$method = "InfinityCastleArchitectureValidation.$Mode"
$arguments = @('-batchmode', '-projectPath', ('"' + $project + '"'), '-executeMethod', $method, '-logFile', ('"' + $log + '"'))
if ($Mode -ne 'Preview') { $arguments += @('-nographics', '-quit') }
$process = Start-Process 'D:/for unity/6000.0.51f1/Editor/Unity.exe' -WindowStyle Hidden -PassThru -ArgumentList $arguments
$process.WaitForExit()
if ($process.ExitCode -ne 0) { throw "Architecture $Mode failed. See $log" }
if ($Mode -eq 'Exterior') {
    & robocopy "$project/Assets/InfinityCastle/MobileArchitecture" "$root/Assets/InfinityCastle/MobileArchitecture" /E /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) { throw 'Exterior prefab copy failed.' }
}
Copy-Item -LiteralPath "$project/Assets/prefabs/InfinityCastleRuntime.prefab" -Destination "$root/Assets/prefabs/InfinityCastleRuntime.prefab"
foreach ($file in @('Assets/Scripts/DynamicInfinityCastle.Architecture.cs.meta', 'Assets/Editor/InfinityCastleArchitectureValidation.cs.meta')) {
    if (!(Test-Path -LiteralPath "$root/$file")) { Copy-Item -LiteralPath "$project/$file" -Destination "$root/$file" }
}
if ($Mode -eq 'Test') { Get-Content -LiteralPath "$root/Temp/building-architecture-tests.txt" }
