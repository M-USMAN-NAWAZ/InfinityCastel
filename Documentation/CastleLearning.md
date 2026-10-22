# Learned Castle Director

Unity ML-Agents 4.0.3 controls high-level layout, structure recipe, motion phase, direction, and quiet intervals. The original pooled castle still owns placement, gravity, landing recovery, swept collision checks, and moving-platform support. The model never writes transforms directly.

The runtime loads `Assets/Resources/CastleML/CastleDirector.onnx` once during pool initialization. Its network has 48 numeric observations, two 64-unit hidden layers, and discrete branches of 6/4/6/3/4 actions. Each pooled building requests a separate startup plan; later decisions are requested for shift/rebuilding waves, not every frame. Inference uses Burst on the CPU; no internet service or phone-side training is needed. The two most recent layout archetypes and the last structure recipe are action-masked. An eight-decision frequency/combinations history discourages repeating cycles. A seeded fallback is explicitly used if the model is absent.

The four structure choices influence safe extension branching and the growth bias of streamed doorway graphs. Each floor now starts with an actual turn hallway or three-way junction and grows independently through free, matching doorways, mixing rooms and additional hallway branches. The learned state biases root-heavy, chain-like and free branching; a world-cell seed chooses module counts, types, ports, dimensions and floor layouts. Clear facing doors can form extra loops, and an eligible pair of floors connects with the intact `stair_2`. Unused doors are visibly closed. This replaces the previous fixed two-by-two room stack: six layout choices and four structure choices are director inputs, not a limit of 24 possible generated graphs. Geometry clearance remains authoritative; this is arrangement of authored meshes, not mesh synthesis.

## Mixed Routes

- Attached: adjoining base courts with a short broad timber seam.
- Stairs: separated buildings joined by the original stair/walkway prefabs in the same gravity frame.
- Jump: level, same-gravity gaps of 1.0-2.2 metres. No automatic bridge is added across these optional gaps.
- Distant: deliberately disconnected architecture, including the outer pooled layers.

Two connected walking exits are prioritized before optional routes wherever placement clearance permits. Sideways-gravity branches get urgent walking connections rather than waiting for distant scenery generation. Different layout archetypes change the route mixture, stair-rise frequency, lateral offsets, and port order. Original model scale and orientation are preserved. This varies arrangements, not the authored artwork itself; more unique building prefabs would further reduce visual repetition.

## Current Model

The included v2 policy was actually trained for 600,064 PPO decisions (run `castle-director-v2-structures-20261022`) and exported as a 49,125-byte ONNX file. In 512 held-out contexts it used all six layouts (98/86/79/74/86/89 decisions), all four structures (124/131/113/144), and all 24 layout/structure combinations. No immediate structure repeats or repeats of the two most recent layouts occurred. Mean curriculum reward was 0.678 versus 0.466 for masked-random actions under the same masks. The revised reward differs from v1, so its score is not directly comparable to v1. CPU inference averaged 1.15 ms in the desktop editor evaluation; Android device performance has not been measured. See `Training/model-card.json` for the model hash and counts.

## Training

The curriculum is a lightweight high-level decision simulator, not a full player-physics simulation or a reconstruction learned from anime frames. Thirty-two Unity agents train with PPO on varied route mixtures, all six gravity frames, player movement, fall state, structure history, and previous rebuild success. Rewards favor diversity, less-used layout/structure combinations, balanced optional routes, and appropriate timing. Runtime geometry must pass independent playability and collision tests regardless of policy reward.

Use Python 3.10.12 and the exact versions in `Training/requirements.txt`. An isolated environment and training executable are kept in `Temp/CastleML` so they do not enter Android builds.

1. Install the requirements into a virtual environment with Python 3.10.12.
2. Run `Tools/RunArchitectureValidation.ps1 -Mode TrainingBuild`. This builds the training scene and training prefab in an isolated project under `Temp`, without changing the gameplay scene.
3. Run `Training/TrainCastle.ps1 -RunId castle-director-v3`. Use a new run ID for a new model, or `-Resume` to resume an existing run. The script exports the trained ONNX file to the runtime Resources folder only on success. An old four-branch checkpoint cannot resume into this five-branch policy.
4. Run `Tools/RunArchitectureValidation.ps1 -Mode Model`. It evaluates 512 held-out contexts against masked-random actions and checks all six archetypes, all four structures, all 24 combinations and no immediate/recent masked repeats. Its desktop timings are not Android performance claims.
5. Run topology, full regression, Playground rendering, and Android build checks before replacing a shipped model.

The exported ONNX model is the deployable policy. Checkpoints, TensorBoard event files, generated training executables, and Python packages remain under `Temp`.

`CastleCpuInferenceBuild` strips the inference package's unused GPU shader variants from player builds. Remove or scope that build processor before introducing any GPU-backed inference model; it does not strip the castle's own materials or URP shaders.

## Runtime Verification

The mixed-graph Volume regression passed connected traversal in all six gravity frames, building clearance, real jumps and a 44 m fall without a spawn reset. It checked 22,554 connected distributed door endpoints, 15,786 closed shoji doors and 2,203 intact switchbacks, including walking the U-shaped `stair_2` through both turns to the next floor. Across 880 active cells it found 668 distinct module/connection graphs and five different module counts; fingerprints exclude rotation and scale so cosmetic variation cannot pass. It counted 8,453 rooms, 1,974 turn hallways and 1,695 junctions. Reports are under `Temp/building-volume-tests.txt` and `Temp/castle-ml-model-validation.txt`.

The current compiled Windows development player passed startup controls, 96 independent learned startup decisions, transformed/nonuniform collision cooking for every module (including `stair_2`), mixed-graph variety/door state, normal/deep jumps without a spawn reset, six-direction coverage and fixed-pool streaming at a simulated 45 m/s. Its uncapped headless preload was 6.63 s on this PC, not Android. See `Temp/PlayerCheck/checks.txt`.

The current OpenGL Core Streaming regression passed 480 m legs in all six directions, checking local rooms AND hallways at each leg endpoint, hidden preparation and fixed pools. Far samples had 599–617 complete modules within 100 m, including 186–196 turn hallways/junctions. Graph fingerprints remained varied across the streamed volume, and collider promotion changed no rendered pixels. See `Temp/castle-streaming-tests.txt`.

Stair runs and gravity-corner clearance are checked during placement and motion. Ceiling continuations use the existing pool, and optional jump gaps are reserved before surrounding routes consume their clearance. These are automated PC checks, not physical Android device benchmarks. The existing Android APK has not been rebuilt with this model or the new staircase.

Rebuild countdowns hold at zero while inference is pending. A due asynchronous decision must not restart the quiet interval. Walking tests follow the destination building's transform rather than a stale world-space endpoint, and occupied-platform tests stand clear of protected connector zones.
