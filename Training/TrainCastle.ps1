param([string]$RunId = 'castle-director-v1', [switch]$Resume)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$trainer = Join-Path $root 'Temp/CastleML/.venv/Scripts/mlagents-learn.exe'
$build = Join-Path $root 'Temp/CastleML/Build/CastleDirectorTraining.exe'
$config = Join-Path $PSScriptRoot 'castle-director.yaml'
if (!(Test-Path -LiteralPath $trainer) -or !(Test-Path -LiteralPath $build)) { throw 'Install the pinned trainer and build the training scene first. See Documentation/CastleLearning.md.' }
$arguments = @($config, '--env', $build, '--run-id', $RunId, '--results-dir', (Join-Path $root 'Temp/CastleML/results'), '--no-graphics', '--torch-device', 'cpu', '--seed', '1729')
if ($Resume) { $arguments += '--resume' }
& $trainer @arguments
if ($LASTEXITCODE -ne 0) { throw "Training failed: $LASTEXITCODE" }
$model = Join-Path $root "Temp/CastleML/results/$RunId/InfinityCastleDirector.onnx"
if (!(Test-Path -LiteralPath $model)) { throw 'Trainer did not export an ONNX model.' }
$destination = Join-Path $root 'Assets/Resources/CastleML'
New-Item -ItemType Directory -Path $destination -Force | Out-Null
Copy-Item -LiteralPath $model -Destination (Join-Path $destination 'CastleDirector.onnx')
Write-Output 'Exported trained castle director to Assets/Resources/CastleML/CastleDirector.onnx'
