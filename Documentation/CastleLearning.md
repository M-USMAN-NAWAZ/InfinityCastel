# Learned Castle Director

Unity ML-Agents 4.0.3 controls high-level layout, motion phase, direction, and quiet intervals. The original pooled castle still owns placement, gravity, landing recovery, swept collision checks, and moving-platform support. The model never writes transforms directly.

The runtime loads `Assets/Resources/CastleML/CastleDirector.onnx` once during pool initialization. Its network has 40 numeric observations, two 64-unit hidden layers, and discrete branches of 6/4/6/3 actions. It requests a decision only when a shift or rebuilding wave is due, not every frame. Inference uses Burst on the CPU; no internet service or phone-side training is needed. The two most recent layout archetypes are action-masked, and an eight-decision frequency history discourages a repeating cycle. A deterministic fallback is explicitly used if the model is absent.

## Mixed Routes

- Attached: adjoining base courts with a short broad timber seam.
- Stairs: separated buildings joined by the original stair/walkway prefabs in the same gravity frame.
- Jump: level, same-gravity gaps of 1.0-2.2 metres. No automatic bridge is added across these optional gaps.
- Distant: deliberately disconnected architecture, including the outer pooled layers.

Two connected walking exits are prioritized before optional routes wherever placement clearance permits. Sideways-gravity branches get urgent walking connections rather than waiting for distant scenery generation. Different layout archetypes change the route mixture, stair-rise frequency, lateral offsets, and port order. Original model scale and orientation are preserved. This varies arrangements, not the authored artwork itself; more unique building prefabs would further reduce visual repetition.

## Baseline Model

The included policy was actually trained for 151,552 PPO decisions and exported as a 43,769-byte ONNX file. In 512 held-out contexts it used all six layouts (107/80/73/70/92/90 decisions), with mean curriculum reward 0.534 versus 0.512 for masked-random actions. That is a modest baseline improvement, not evidence of anime-quality choreography. CPU inference averaged 1.08 ms in the desktop editor evaluation; Android device performance has not been measured.

## Training

The initial curriculum is a lightweight high-level decision simulator, not a full player-physics simulation or a reconstruction learned from anime frames. Thirty-two Unity agents train with PPO on varied route mixtures, all six gravity frames, player movement, fall state, and previous rebuild success. Rewards favor diversity, balanced optional routes, and appropriate timing. Runtime geometry must pass independent playability and collision tests regardless of policy reward.

Use Python 3.10.12 and the exact versions in `Training/requirements.txt`. An isolated environment and training executable are kept in `Temp/CastleML` so they do not enter Android builds.

1. Install the requirements into a virtual environment with Python 3.10.12.
2. Build `InfinityCastleLearningTools.BuildTraining` in an isolated copy of the project. This generates the training scene and training prefab under `Assets/InfinityCastle/Training`.
3. Run `Training/TrainCastle.ps1 -RunId castle-director-v1`. Use a new run ID for a new model, or `-Resume` to resume an existing run. The script exports the trained ONNX file to the runtime Resources folder only on success.
4. Run `InfinityCastleLearningTools.ValidateModel` in the isolated project. It evaluates 512 held-out contexts against masked-random actions and checks all six archetypes and no consecutive/recent repeats. Its desktop timings are not Android performance claims.
5. Run topology, full regression, Playground rendering, and Android build checks before replacing a shipped model.

The exported ONNX model is the deployable policy. Checkpoints, TensorBoard event files, generated training executables, and Python packages remain under `Temp`.

`CastleCpuInferenceBuild` strips the inference package's unused GPU shader variants from player builds. Remove or scope that build processor before introducing any GPU-backed inference model; it does not strip the castle's own materials or URP shaders.

## Runtime Verification

Eight seeded layouts passed route-mixture, building/link clearance, authored stair fit, outward-facing gravity corners, and real-player jump-gap checks. The Playground play-mode check used the actual model (four learned decisions), completed a timed wave moving 187 modules including 59 connected buildings and 11 adjacent buildings, and verified keyboard/touch input, skybox preservation, close motion, and landing on castle architecture after a fall. These are isolated editor tests, not physical Android device benchmarks.

Stair runs and gravity-corner clearance are checked during placement and motion. Ceiling continuations use the existing pool, and optional jump gaps are reserved before surrounding routes consume their clearance. The full regression and Playground checks passed before the final jump-reservation change; all eight topology seeds passed afterward. The Android APK has not been rebuilt with this model.

Rebuild countdowns hold at zero while inference is pending. A due asynchronous decision must not restart the quiet interval. Walking tests follow the destination building's transform rather than a stale world-space endpoint, and occupied-platform tests stand clear of protected connector zones.
