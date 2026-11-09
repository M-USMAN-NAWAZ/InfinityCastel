# Procedural player in Playground 1

Open `Assets/Scenes/Playground 1.unity` and press Play. The original Playground,
shared Tanjiro prefabs, animation clips, castle, input bindings and build scene
list are preserved. This scene is a separate procedural movement prototype.

## What moves by code

`TanjiroProceduralBody` restores a calibrated neutral skeleton and solves its
pose each frame. The scene's Animator is disabled and has no assigned runtime
controller. Calibration reads the skin's bind matrices, bone lengths and hand
geometry. No animation clip is sampled, and no walk/run motion data or cached
motion cycles are used. The neutral pose is generated from anatomical joint
angles. Runtime motion is generated entirely by joint rules.

The scene uses a separate `ProceduralRig/Tanjiro_WaistCoatSkin.asset` variant,
derived from the preserved `Tanjiro_AnatomicalSkin.asset`. It retains the original
geometry, UVs and materials, adds four weighted upper-arm
and forearm twist joints, fits and weights both previously unweighted thumb-tip
joints, and provides ten fingertip control transforms. The existing three joints
in each finger generate the visible finger bends. Skinning remains limited to
four influences per vertex. Original FBX models and player prefabs are preserved.

Thigh/shin weights fade out at the hip crease and are removed above the waist;
their former influence on the abdomen transfers to the pelvis/lumbar bones.
The lower coat has three two-bone panels, separate from the legs. Gravity and
whole-body acceleration drive a restrained hem response; there is no gait-phase
or thigh-angle driver. A small geometric clearance constraint keeps the baggy
trousers from passing through the hanging panels; the lower segment drapes back
towards gravity. There is no runtime cloth simulation. The baked garment mask in
`Assets/Editor/TanjiroCoatMask.json` is editor-only. Rebuild it with Blender and
`Tools/bake_tanjiro_coat_mask.py`, then run the procedural scene installer.
The original anatomical skin is always the reweighting source, so repeated
installation does not compound changes.

The skin checks rotate just one thigh with the torso and cloth controls held
still. They require the abdomen and fully bound lower coat to move less than
1 mm, reject leg weights above the waist, and verify unchanged vertices, UVs
and triangles. Before/after renders are saved as `waist-before-leg-lift-*.png`
and `waist-after-leg-lift-*.png` in `Temp/procedural-preview`.

- Feet alternate between support contacts and lifted steps. Each contact stays
  relative to its supporting collider, including moving surfaces. The solver
  adapts to actual collider geometry; a smooth ramp alone cannot describe visible
  stair treads.
- Running uses a bounded cadence with brief support contacts and recovery phases
  at the actual scene speeds (5 and 8.335 m/s). Contacts follow travel direction
  during turns. Braking preserves its leading support contact; a trailing foot
  adjusts only when needed. Standing retains that stagger instead of repeatedly
  returning both feet to neutral targets. The existing sword guard can widen it.
- Walking includes overlapping foot support; running includes flight. Each leg
  tracks contact, loading, propulsion, toe-off, recovery, passing and placement.
  One shared clock drives both trajectories exactly half a cycle apart. Neither
  leg integrates a separate recovery timer. Starting or landing selects the
  actual supporting/leading foot without resetting either ankle or the stance.
  The rear foot enters recovery from its current pose, without first jumping
  into a late push-off pose. A missed contact discards its stale destination;
  a terrain/turn landing may finish its short acquisition step before rejoining
  the same cycle. Unreachable balancing steps cannot block both feet forever.
  Recovery from an invalidated landing probes beneath that leg's hip, instead
  of continually chasing another forward target during a direction reversal.
  Toe-off starts at the current pose before advancing time, preserving position
  and velocity instead of subtracting the capsule's movement a second time.
  Swing targets come from leg length, current hip position, take-off velocity and
  reachable landing geometry. A clamped cubic spline solves segment velocities
  together for continuous velocity and acceleration. Clearance and backsweep have
  separate timing so the knee does not fold abruptly after toe-off;
  terminal swing extends then retracts the foot to match the floor's world speed.
  Touchdown is prepared before contact and retained in the collider's frame.
  Take-off history retains its original direction during turns, rather than
  rotating an airborne foot's old path around the hips with the character.
  IK follows these trajectories; joint-angle oscillators no longer generate the
  targets. Contact planning and IK use the same bent-leg reach envelope.
  Contact registers on the frame the ankle reaches the floor. The forefoot pivot
  transfers while the sole is flat, before the heel raises, so sprint loading and
  propulsion remain visible. Landing forecasts include deceleration rather than
  projecting constant-speed travel while braking.
- Free arms use shoulder swing and elbow-flexion rules coupled to the opposite
  leg's actual solved thigh motion. They do not chase wrist targets beside the
  hips. The pelvis and chest
  counter-rotate, shoulders participate, and the head is partly stabilized.
  Elbows and wrists follow shoulder swing with a slight delay. Gait intensity,
  cadence and walk/run transitions ease in and out instead of changing abruptly.
  The armed hand follows that arm drive with the existing sword grip; there is
  no independent sinusoidal carry-hand path.
- The procedural player enables leading-sole step landing in the existing
  capsule motor. It checks full capsule clearance first, then samples the next
  tread rather than returning the previous tread's edge height. The actual
  switchback stair mesh and rail collision are retained. The original player
  keeps its existing motor behavior.
- One pelvis solve uses supporting-leg extension, actual contact heights and
  weight acceptance/push-off timing. There is no independent vertical bounce
  oscillator, minimum-of-both-feet lift, or unsmoothed reach drop after the solve.
  An unloaded swing leg adapts to the body; it cannot pull the torso up or down.
  World-height integration absorbs capsule stair steps, and the same solve
  handles genuine landing compression. Head and torso inherit this restrained
  connected motion instead of receiving another bobbing curve.
- Actual capsule travel advances a shared stride phase; cycle distance depends
  on speed and leg length. A blocked player cannot pedal on a timer. Fast Run
  increases stride distance as well as cadence and changes heel recovery,
  support duration, forefoot push-off and arm drive.
- Running pelvis height connects loaded support to ballistic visual flight.
  Landing/take-off heights come from leg reach, planned stance sweep and the
  forefoot rocker. The support segment joins the flight heights and vertical
  velocities. Terrain height is integrated separately; no sine bounce is added.
  Flat-ground reach uses current contacts, rather than anticipating another
  high-speed step and forcing the core down before the leg extends.
- Forward lean is distributed through pelvis, spine and chest, with restrained
  hip translation and an acceleration response. Pelvis yaw follows actual
  thigh separation; the chest counter-rotates, the head compensates and the arms
  inherit that shoulder frame. Arm drive uses a continuous, unsaturated measure
  of leg separation rather than a clipped wrist oscillator.
- Upper arms travel behind the ribs before forearm recovery. Elbow flexion
  follows shoulder motion and its angular velocity, with response scaled to
  cadence. Fast Run increases drive without a separate oscillator. Clavicle
  protraction stays restrained, with no shoulder-shrug driver.
- After the leg solve, one filtered signal reads which thigh leads. It drives
  both shoulders with opposite signs and supplies the chest's gait contribution.
  The chest also counterbalances the actual pelvis yaw. Relative twist is shared
  between spine and chest, while the head partly stabilizes. Arm directions use
  the resulting chest frame, calibrated from the neutral rig; they no longer use
  a separately reconstructed torso orientation. The shoulder is filtered once,
  elbow inertia has a smooth bounded response, and backward/forward drive joins
  with continuous slope. This replaces mixed shin/thigh timing and independent
  shoulder/torso delays.
- The supplied original Playground run is the visual posture reference. The
  procedural version uses a more compact shoulder arc, keeps more elbow bend
  on the return, and leans slightly through the spine and chest. The target
  posture was matched visually without sampling the original animation clips.
- Sword carry retains the shoulder/forearm motion of the free arm. The wrist
  adjusts towards the low guard while preserving the actual hand position.
  Only draw/guide gestures use the longer wrist damping. Applying that damping
  again during carry previously suppressed the swing and forced extra elbow
  flexion to maintain a fixed palm/blade target.
- The procedural player opts into a short forward deceleration in the existing
  controller. Ground checks prevent that braking motion from carrying the player
  off a landing. Original players keep their original controller behavior.
- The collision capsule keeps the existing gravity and camera behavior. The
  visual core turns progressively while feet, wrists and upper body follow with
  different response times.
- The existing sword button or **R** runs a three-second draw/sheath gesture.
  When running with the sword out, the right hand carries it and the left arm
  swings freely. Sword attachment, mesh, materials and input UI are reused.
- Two-bone arm solving is used for weapon contacts, with continuous wrist and
  grip transitions. Fingers use individual curl and thumb-opposition rules;
  running uses a loose hand rather than a rigid flat palm. The added twist joints
  distribute arm roll through the mesh.

This is visual contact, balance and inertia over the existing capsule motor. It
is not a whole-body physics simulation or an automatic generator of fighting
techniques. Sun Breathing forms still need deliberate sword paths, timing,
contact rules and effects; this driver supplies a procedural foundation for them.

## Tuning and verification

The scene component exposes resting step duration, foot lift, core/limb response
and sword-action duration. Moving gait uses contact/recovery trajectories and
speed-dependent support/cadence rules. Rig references and frame calibration should
be regenerated with **Infinity Castle > Procedural Player > Install in
Playground 1** after changing the character rig. Save scene edits first.

Run `Tools/RunArchitectureValidation.ps1 -Mode Procedural` for isolated Unity
checks. It validates tread adaptation, pelvis travel, probe count, sword state,
arm continuity, wrist bend and gravity transitions, plus the original clip-based
player. It also checks relaxed arm clearance, weighted thumb-tip deformation,
gait support phases and the actual authored switchback stairs. It exports a
twenty-three-second side-view Run/Fast Run sequence with both speed transitions,
both stops and repeated interruptions to
`Temp/procedural-preview/motion`, using the actual 5/8.335 m/s speeds.
It measures actual ankle trajectories as well as intended phase timing, and
records final pelvis/head heights in `run-height.csv` and `sprint-height.csv`.
`motion-phases.csv` records actual support states, stages, plant counts, ankle
positions and gait phase. Green target crosses denote support and cyan denotes
swing. The horizontal line is a diagnostic ground-height guide. The height
measurements include every pelvis correction, not an intermediate offset.
The checks reject large height steps and verify stable mean height and fixed
contacts at 30, 60 and 120 FPS. Foot support and terrain adaptation can briefly
change local ankle direction without changing the shared stride phase.
Acceptance also checks alternating actual plants, backward travel during support,
swing clearance, knee extension and leg continuity through speed changes. Opposite
sinusoidal ankle waves alone are not treated as proof of natural running. See
`Temp/procedural-player-tests.txt` for measurements from the latest run. The
rendered 24 FPS preview is `Temp/procedural-preview/coordination-side-review.mp4`.
It copies the verified Playground 1 scene and its separate rig skin asset after
checking the saved scene has not changed. A pre-install scene backup lives in
`Temp/Playground 1-before-procedural.unity`.

For native gameplay review, enter Play mode and invoke
`TanjiroProceduralLiveReview.Queue()` through Unity MCP. It waits for castle
preparation, exercises normal Update/LateUpdate and rendering, restores player
input, and records contacts and a native side-view video in `Temp/coordination-live`.
Prefer `TanjiroProceduralLiveReview.ArmAndPlay()` from Edit mode for automated
reviews: it survives the Play-mode domain reload, holds input during preparation,
waits for grounded support, and automatically returns to Edit mode when done.
It includes Run -> Fast Run -> Run, stopping from both speeds, interrupted starts,
and the existing draw/carry/sheath interface. A temporary orthographic camera
holds its side orientation and scale while following the motor root, so pelvis
motion remains visible. It is destroyed and the gameplay cameras are restored
after review; no camera change is saved in the scene. The castle sets runtime
Run to 4 m/s, while the saved-scene fixture also tests 5 m/s. Fast Run is 8.335 m/s.
Use `ArmFrontAndPlay()` for a separate fixed-front review in
`Temp/coordination-front-live`. The latest native measurements are recorded in
each review folder's `review.txt`.
The native report identifies the loaded skin, independent coat-panel count,
actual per-foot plants, reach discrepancy and unsupported intervals. The live
review counts cumulative real plants as well as support samples: Editor callbacks
can miss a short support frame while saving screenshots. An earlier uncontrolled
falling/terrain pass below the spawn roof recorded a transient 0.0579 m reach
error; the controlled grounded review does not cover that case.

The waist/coat comparison is `Temp/procedural-preview/waist-coat-comparison.png`;
`waist-coat-running.mp4` shows the repaired skin through Run, Fast Run, speed
changes, braking and interrupted starts using the existing gameplay controls.

The propulsion review is `Temp/procedural-preview/propulsion-contact-sheet.png`,
`propulsion-reference-comparison.png` and `propulsion-run-fast-run.mp4`.
`before-after-propulsion.mp4` compares the previous implementation on the left
with the revised gait on the right. The 23-second movie covers both speeds,
transitions, stops and interrupted starts. Steady-cycle panels use 96 Hz captures
to show actual loading, extension and flight; they are not runtime pose keys.
Measured loaded-knee peaks are approximately 44 degrees; subsequent push-off
extends to 27 degrees in Run and 15 degrees in Fast Run. The core rises roughly
2 cm from loading into propulsion, with total steady vertical travel of 4.0/3.1 cm.
Tests check real contact alternation, fixed support anchors, reach, stopping,
authored stairs, jump recovery and 30/60/120 FPS. The native review also writes
per-frame observations to `Temp/pure-joint-live/motion.csv`.

The subsequent upper-body correction is reviewed in
`Temp/procedural-preview/upper-body-gameplay-side.mp4` (native Play mode),
`upper-body-side-review.mp4`, `upper-body-front-review.mp4`,
`before-after-upper-body.mp4` (before left, after right), and
`upper-body-side-front-sheet.png`. The native recorder copies the completed
camera image asynchronously and uses Unity's built-in editor media encoder;
it does not compress PNGs on the gameplay thread or alter the game's clock.
Capture timestamps and per-frame observations are in `Temp/upper-body-live`.
The 552-frame before/after comparison retains identical foot targets, stride
phase, pelvis height and lean. Arm tests cover both arms' backward travel,
elbow range, lateral clearance and consistent swing at 30/60/120 FPS.

The subsequent coordination correction is reviewed in
`coordination-gameplay-side.mp4`, `coordination-gameplay-front.mp4`,
`coordination-side-front-sheet.png`, `coordination-side-review.mp4`,
`coordination-front-review.mp4` and `before-after-coordination.mp4`, all under
`Temp/procedural-preview`. Its checks measure actual opposite-thigh/upper-arm
timing and shoulder-line yaw against the hip line, rather than using ankle/wrist
opposition as a proxy. Steady opposite-thigh/arm correlation improves from
0.888/0.832 to 0.971/0.968 for Run/Fast Run; directional agreement is 100% in
the measured unambiguous thigh-leading samples. Shoulder yaw spans about
11.2/13.8 degrees and remains opposite the pelvis. Foot targets, stride phase,
pelvis height and lean remain identical across the 552-frame comparison.

Select the player's `TanjiroProceduralBody`, enable **Draw Gait Diagnostics** and
enable Scene-view Gizmos to inspect stages, phase, support normals and IK targets.
The yellow balance-centre marker is an approximation from the pelvis, not a
mass-weighted physics centre of mass. Diagnostics are editor-only and disabled by
default; they add no Android rendering work.

The runtime uses joint rotations, two leg contact solves, weapon-arm solves when needed and at most
three non-allocating foot probes per frame, plus the controller's existing
collision queries and one ground query while braking. Arm bend-plane history and wrist
gesture damping follow the body frame rather than staying behind during turns.
No extra stair colliders are created. Android device frame time has
not been measured for this new scene; no new APK is produced by these checks.

The gait design follows coordinated upper/lower-body movement rather than
independent wrist and ankle oscillation; see [arm-swing mechanics](https://pubmed.ncbi.nlm.nih.gov/19181900/)
and [body acceleration during running](https://nmbl.stanford.edu/publications/pdf/Hamner2012.pdf).
These inform the support/flight rules; no measured motion sequence is sampled.
