param([ValidateSet('Test', 'Volume', 'Preview', 'Install', 'Exterior', 'Diagnostics', 'Traversal', 'Quality', 'Corners', 'Recovery', 'Player', 'AndroidBuild')][string]$Mode = 'Test')
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
if ($Mode -in @('Diagnostics', 'Traversal', 'Quality', 'Corners', 'Recovery')) { $method = "InfinityCastleTraversalValidation.$Mode" }
if ($Mode -in @('Player', 'AndroidBuild')) { $method = "InfinityCastleBuildValidation.$Mode" }
$arguments = @('-batchmode', '-projectPath', ('"' + $project + '"'), '-executeMethod', $method, '-logFile', ('"' + $log + '"'))
if ($Mode -eq 'Player') { $arguments += @('-buildTarget', 'Win64') }
if ($Mode -eq 'AndroidBuild') { $arguments += @('-buildTarget', 'Android') }
if ($Mode -notin @('Preview', 'Diagnostics', 'Quality')) { $arguments += @('-nographics', '-quit') }
if ($Mode -in @('Diagnostics', 'Quality')) { $arguments += '-quit' }
$process = Start-Process 'D:/for unity/6000.0.51f1/Editor/Unity.exe' -WindowStyle Hidden -PassThru -ArgumentList $arguments
$process.WaitForExit()
if ($process.ExitCode -ne 0) { throw "Architecture $Mode failed. See $log" }
if ($Mode -eq 'Exterior') {
    & robocopy "$project/Assets/InfinityCastle/MobileArchitecture" "$root/Assets/InfinityCastle/MobileArchitecture" /E /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) { throw 'Exterior prefab copy failed.' }
}
Copy-Item -LiteralPath "$project/Assets/prefabs/InfinityCastleRuntime.prefab" -Destination "$root/Assets/prefabs/InfinityCastleRuntime.prefab"
& robocopy "$project/Assets/InfinityCastle/CastleModules/RuntimeLod" "$root/Assets/InfinityCastle/CastleModules/RuntimeLod" /E /NFL /NDL /NJH /NJS /NP | Out-Null
if ($LASTEXITCODE -ge 8) { throw 'Distance model metadata copy failed.' }
if (!(Test-Path -LiteralPath "$root/Assets/InfinityCastle/CastleModules/RuntimeLod.meta")) {
    Copy-Item -LiteralPath "$project/Assets/InfinityCastle/CastleModules/RuntimeLod.meta" -Destination "$root/Assets/InfinityCastle/CastleModules/RuntimeLod.meta"
}
foreach ($file in @('Assets/Scripts/DynamicInfinityCastle.Architecture.cs.meta', 'Assets/Scripts/DynamicInfinityCastle.Network.cs.meta', 'Assets/Scripts/DynamicInfinityCastle.DistrictsDense.cs.meta', 'Assets/Scripts/InfinityCastlePlayerCheck.cs.meta', 'Assets/Editor/InfinityCastleBuildValidation.cs.meta', 'Assets/Editor/InfinityCastleArchitectureValidation.cs.meta')) {
    if (!(Test-Path -LiteralPath "$root/$file")) { Copy-Item -LiteralPath "$project/$file" -Destination "$root/$file" }
}
if ($Mode -eq 'Test') { Get-Content -LiteralPath "$root/Temp/building-volume-tests.txt" }
if ($Mode -eq 'Volume') { Get-Content -LiteralPath "$root/Temp/building-volume-tests.txt" }
