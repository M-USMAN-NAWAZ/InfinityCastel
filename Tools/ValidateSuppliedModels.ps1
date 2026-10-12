$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$project = Join-Path $root 'Temp/SuppliedModelValidation'
New-Item -ItemType Directory -Force -Path "$project/Assets/Editor", "$project/Assets/Scripts", "$project/Packages", "$project/ProjectSettings", "$project/Library" | Out-Null
Copy-Item -LiteralPath "$root/ProjectSettings/ProjectVersion.txt" -Destination "$project/ProjectSettings"
Copy-Item -LiteralPath "$root/Assets/Editor/CastleCurrentModelImporter.cs" -Destination "$project/Assets/Editor"
Copy-Item -LiteralPath "$root/Assets/Scripts/CastleArchitectureModule.cs" -Destination "$project/Assets/Scripts"
foreach ($file in @('Assets/Editor/CastleCurrentModelImporter.cs.meta', 'Assets/Scripts/CastleArchitectureModule.cs.meta')) {
    if (Test-Path -LiteralPath "$root/$file") { Copy-Item -LiteralPath "$root/$file" -Destination "$project/$file" }
}
& robocopy "$root/Assets/InfinityCastle/CastleModules" "$project/Assets/InfinityCastle/CastleModules" /E /MT:8 /NFL /NDL /NJH /NJS /NP | Out-Null
if ($LASTEXITCODE -ge 8) { throw 'Asset copy failed.' }
& robocopy "$root/Assets/Shaders" "$project/Assets/Shaders" /E /MT:8 /NFL /NDL /NJH /NJS /NP | Out-Null
if ($LASTEXITCODE -ge 8) { throw 'Source model copy failed.' }
& robocopy "$root/Library/PackageCache" "$project/Library/PackageCache" /E /MT:16 /NFL /NDL /NJH /NJS /NP | Out-Null
if ($LASTEXITCODE -ge 8) { throw 'Package cache copy failed.' }
Copy-Item -LiteralPath "$root/Tools/SuppliedModelPackages.json" -Destination "$project/Packages/manifest.json"
$log = Join-Path $root 'Temp/supplied-model-unity.log'
$process = Start-Process 'D:/for unity/6000.0.51f1/Editor/Unity.exe' -WindowStyle Hidden -PassThru -ArgumentList @(
    '-batchmode', '-nographics', '-quit', '-projectPath', ('"' + $project + '"'),
    '-executeMethod', 'CastleCurrentModelImporter.Validate', '-logFile', ('"' + $log + '"'))
$process.WaitForExit()
if ($process.ExitCode -ne 0) { throw "Asset validation failed. See $log" }
& robocopy "$project/Assets/InfinityCastle/CastleModules" "$root/Assets/InfinityCastle/CastleModules" /E /NFL /NDL /NJH /NJS /NP | Out-Null
if ($LASTEXITCODE -ge 8) { throw 'Prefab copy failed.' }
& robocopy "$project/Assets/InfinityCastle/CastleTheme" "$root/Assets/InfinityCastle/CastleTheme" /E /NFL /NDL /NJH /NJS /NP | Out-Null
if ($LASTEXITCODE -ge 8) { throw 'Source metadata copy failed.' }
foreach ($file in @('Assets/Editor/CastleCurrentModelImporter.cs.meta', 'Assets/Scripts/CastleArchitectureModule.cs.meta', 'Assets/InfinityCastle/CastleModules.meta', 'Assets/InfinityCastle/CastleTheme.meta')) {
    if (!(Test-Path -LiteralPath "$root/$file")) { Copy-Item -LiteralPath "$project/$file" -Destination "$root/$file" }
}
Get-Content -LiteralPath "$root/Temp/supplied-model-validation.txt"
