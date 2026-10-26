param([ValidateSet('Test', 'Volume', 'Preview', 'Install', 'Exterior', 'Diagnostics', 'Traversal', 'FreeJump', 'Quality', 'Corners', 'Recovery', 'Controls', 'Streaming', 'Player', 'AndroidBuild', 'Assembly', 'TrainingBuild', 'Model', 'Retopology', 'Junctions', 'Skybox', 'Characters', 'CharacterPlayer', 'Katana', 'YoriichiKatana', 'SwordInspect', 'SwordAnimation')][string]$Mode = 'Test',
    [ValidateSet('Default','Vulkan','OpenGLCore')][string]$GraphicsAPI = 'Default')
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
if ($Mode -in @('Diagnostics', 'Traversal', 'FreeJump', 'Quality', 'Corners', 'Recovery', 'Controls', 'Streaming')) { $method = "InfinityCastleTraversalValidation.$Mode" }
if ($Mode -in @('Player', 'AndroidBuild')) { $method = "InfinityCastleBuildValidation.$Mode" }
if ($Mode -eq 'Assembly') { $method = 'CastleTimberAssemblyBake.Assembly' }
if ($Mode -eq 'TrainingBuild') { $method = 'InfinityCastleLearningTools.BuildTraining' }
if ($Mode -eq 'Model') { $method = 'InfinityCastleLearningTools.ValidateModel' }
if ($Mode -eq 'Retopology') { $method = 'CastleRetopologyBake.Validate' }
if ($Mode -eq 'Junctions') { $method = 'CastlePathJunctionValidation.Junctions' }
if ($Mode -eq 'Skybox') { $method = 'CastleBackgroundValidation.Run' }
if ($Mode -eq 'Characters') { $method = 'SunBreatherRigValidation.Run' }
if ($Mode -eq 'CharacterPlayer') { $method = 'SunBreatherPlayerSetup.Validate' }
if ($Mode -eq 'Katana') { $method = 'RedKatanaSetup.Run' }
if ($Mode -eq 'YoriichiKatana') { $method = 'RedKatanaSetup.Yoriichi' }
if ($Mode -eq 'SwordInspect') { $method = 'TanjiroSwordAnimationSetup.Inspect' }
if ($Mode -eq 'SwordAnimation') { $method = 'TanjiroSwordAnimationSetup.Build' }
$arguments = @('-batchmode', '-projectPath', ('"' + $project + '"'), '-executeMethod', $method, '-logFile', ('"' + $log + '"'))
if ($GraphicsAPI -eq 'Vulkan') { $arguments += '-force-vulkan' }
if ($GraphicsAPI -eq 'OpenGLCore') { $arguments += '-force-glcore' }
if ($Mode -in @('Player', 'TrainingBuild', 'Model')) { $arguments += @('-buildTarget', 'Win64') }
if ($Mode -eq 'AndroidBuild') { $arguments += @('-buildTarget', 'Android') }
if ($Mode -notin @('Preview', 'Diagnostics', 'Quality', 'Controls', 'Streaming', 'Assembly', 'Junctions', 'Skybox', 'Characters', 'CharacterPlayer', 'Katana', 'YoriichiKatana', 'SwordInspect', 'SwordAnimation')) { $arguments += @('-nographics', '-quit') }
if ($Mode -in @('Diagnostics', 'Quality', 'Controls', 'Streaming', 'Assembly', 'Junctions', 'Skybox', 'Characters', 'CharacterPlayer', 'Katana', 'YoriichiKatana', 'SwordInspect', 'SwordAnimation')) { $arguments += '-quit' }
$process = Start-Process 'D:/for unity/6000.0.51f1/Editor/Unity.exe' -WindowStyle Hidden -PassThru -ArgumentList $arguments
$process.WaitForExit()
if ($process.ExitCode -ne 0) { throw "Architecture $Mode failed. See $log" }
if ($Mode -in @('TrainingBuild', 'Model')) { exit 0 }
if ($Mode -eq 'SwordInspect') { Get-Content -LiteralPath "$root/Temp/sword-animation-inspection.txt"; exit 0 }
if ($Mode -eq 'SwordAnimation') {
    & robocopy "$project/Assets/Characters/SunBreathers/SwordActions" "$root/Assets/Characters/SunBreathers/SwordActions" /E /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) { throw 'Sword animation asset copy failed.' }
    foreach ($file in @('Assets/Characters/SunBreathers/Tanjiro.prefab','Assets/Characters/SunBreathers/TanjiroThirdPerson.prefab','Assets/Characters/SunBreathers/SwordActions.meta','Assets/Scripts/TanjiroSword.cs.meta','Assets/Editor/TanjiroSwordAnimationSetup.cs.meta','Assets/Editor/TanjiroSwordPoseBake.cs.meta')) {
        Copy-Item -LiteralPath "$project/$file" -Destination "$root/$file"
    }
    Get-Content -LiteralPath "$root/Temp/sword-animation-tests.txt"; exit 0
}
if ($Mode -in @('Katana','YoriichiKatana')) {
    $weaponName = if ($Mode -eq 'Katana') { 'RedNichirinKatana' } else { 'YoriichiKatana' }
    $reportPrefix = if ($Mode -eq 'Katana') { 'red-katana' } else { 'yoriichi-katana' }
    & robocopy "$project/Assets/Weapons/$weaponName" "$root/Assets/Weapons/$weaponName" /E /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) { throw 'Katana asset copy failed.' }
    foreach ($file in @('Assets/Weapons.meta',"Assets/Weapons/$weaponName.meta",'Assets/Editor/RedKatanaSetup.cs.meta')) {
        if (!(Test-Path -LiteralPath "$root/$file")) { Copy-Item -LiteralPath "$project/$file" -Destination "$root/$file" }
    }
    Get-Content -LiteralPath "$root/Temp/$reportPrefix-tests.txt"
    exit 0
}
if ($Mode -eq 'FreeJump') {
    Get-Content -LiteralPath "$root/Temp/castle-free-jump-tests.txt"
    exit 0
}
if ($Mode -eq 'CharacterPlayer') {
    Get-Content -LiteralPath "$root/Temp/tanjiro-player-tests.txt"
    exit 0
}
if ($Mode -eq 'Characters') {
    & robocopy "$project/Assets/Characters/SunBreathers" "$root/Assets/Characters/SunBreathers" /E /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) { throw 'Character asset copy failed.' }
    foreach ($file in @('Assets/Characters.meta','Assets/Characters/SunBreathers.meta','Assets/Editor/SunBreatherRigValidation.cs.meta','Assets/StarterAssets/ThirdPersonController/Scripts/ThirdPersonAnimationEvents.cs.meta')) {
        if (!(Test-Path -LiteralPath "$root/$file")) { Copy-Item -LiteralPath "$project/$file" -Destination "$root/$file" }
    }
    Get-Content -LiteralPath "$root/Temp/sun-breather-rig-tests.txt"
    exit 0
}
if ($Mode -eq 'Skybox') {
    foreach ($file in @('Assets/Images/CastleFromGameModels.png', 'Assets/Images/CastleFromGameModels.png.meta', 'Assets/Mat/MyMat/InfintyCastelSkyBox.mat')) {
        Copy-Item -LiteralPath "$project/$file" -Destination "$root/$file"
    }
    Get-Content -LiteralPath "$root/Temp/castle-background-tests.txt"
    exit 0
}
if ($Mode -eq 'Exterior') {
    & robocopy "$project/Assets/InfinityCastle/MobileArchitecture" "$root/Assets/InfinityCastle/MobileArchitecture" /E /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) { throw 'Exterior prefab copy failed.' }
}
$runtimeSource = "$project/Assets/prefabs/InfinityCastleRuntime.prefab"
$runtimeTarget = "$root/Assets/prefabs/InfinityCastleRuntime.prefab"
# Avoid touching an identical prefab, which Unity may still have memory-mapped.
if (!(Test-Path -LiteralPath $runtimeTarget) -or (Get-FileHash -LiteralPath $runtimeSource).Hash -ne (Get-FileHash -LiteralPath $runtimeTarget).Hash) {
    Copy-Item -LiteralPath $runtimeSource -Destination $runtimeTarget
}
& robocopy "$project/Assets/InfinityCastle/CastleModules/RuntimeLod" "$root/Assets/InfinityCastle/CastleModules/RuntimeLod" /E /NFL /NDL /NJH /NJS /NP | Out-Null
if ($LASTEXITCODE -ge 8) { throw 'Distance model metadata copy failed.' }
if (!(Test-Path -LiteralPath "$root/Assets/InfinityCastle/CastleModules/RuntimeLod.meta")) {
    Copy-Item -LiteralPath "$project/Assets/InfinityCastle/CastleModules/RuntimeLod.meta" -Destination "$root/Assets/InfinityCastle/CastleModules/RuntimeLod.meta"
}
& robocopy "$project/Assets/InfinityCastle/CastleModules/RuntimeMobile" "$root/Assets/InfinityCastle/CastleModules/RuntimeMobile" /E /NFL /NDL /NJH /NJS /NP | Out-Null
if ($LASTEXITCODE -ge 8) { throw 'Mobile model metadata copy failed.' }
if (!(Test-Path -LiteralPath "$root/Assets/InfinityCastle/CastleModules/RuntimeMobile.meta")) {
    Copy-Item -LiteralPath "$project/Assets/InfinityCastle/CastleModules/RuntimeMobile.meta" -Destination "$root/Assets/InfinityCastle/CastleModules/RuntimeMobile.meta"
}
foreach ($file in @('Assets/Scripts/DynamicInfinityCastle.Architecture.cs.meta', 'Assets/Scripts/DynamicInfinityCastle.Network.cs.meta', 'Assets/Scripts/DynamicInfinityCastle.DistrictsDense.cs.meta', 'Assets/Scripts/DynamicInfinityCastle.DistrictLayout.cs.meta', 'Assets/Scripts/InfinityCastlePlayerCheck.cs.meta', 'Assets/Editor/InfinityCastleBuildValidation.cs.meta', 'Assets/Editor/InfinityCastleArchitectureValidation.cs.meta')) {
    if (!(Test-Path -LiteralPath "$root/$file")) { Copy-Item -LiteralPath "$project/$file" -Destination "$root/$file" }
}
& robocopy "$project/Assets/InfinityCastle/CastleModules/RuntimeAssembly" "$root/Assets/InfinityCastle/CastleModules/RuntimeAssembly" /E /NFL /NDL /NJH /NJS /NP | Out-Null
if ($LASTEXITCODE -ge 8) { throw 'Timber assembly asset copy failed.' }
foreach ($file in @('Assets/InfinityCastle/CastleModules/RuntimeAssembly.meta', 'Assets/Scripts/DynamicInfinityCastle.Assembly.cs.meta', 'Assets/Scripts/DynamicInfinityCastle.Doors.cs.meta', 'Assets/Scripts/CastleStairRoute.cs.meta', 'Assets/Editor/CastleTimberAssemblyBake.cs.meta', 'Assets/Editor/CastleDoorwayBake.cs.meta', 'Assets/Editor/CastlePackStairsBake.cs.meta')) {
    if (!(Test-Path -LiteralPath "$root/$file")) { Copy-Item -LiteralPath "$project/$file" -Destination "$root/$file" }
}
& robocopy "$project/Assets/InfinityCastle/CastleModules/Retopology" "$root/Assets/InfinityCastle/CastleModules/Retopology" /E /NFL /NDL /NJH /NJS /NP | Out-Null
if ($LASTEXITCODE -ge 8) { throw 'Retopology atlas and metadata copy failed.' }
foreach ($file in @('Assets/InfinityCastle/CastleModules/Retopology.meta', 'Assets/Editor/CastleRetopologyBake.cs.meta', 'Assets/Editor/CastlePathJunctionValidation.cs.meta')) {
    if (!(Test-Path -LiteralPath "$root/$file")) { Copy-Item -LiteralPath "$project/$file" -Destination "$root/$file" }
}
if ($Mode -eq 'Test') { Get-Content -LiteralPath "$root/Temp/building-volume-tests.txt" }
if ($Mode -eq 'Volume') { Get-Content -LiteralPath "$root/Temp/building-volume-tests.txt" }
