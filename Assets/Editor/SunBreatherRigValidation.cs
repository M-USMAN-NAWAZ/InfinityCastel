using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Cinemachine;
using StarterAssets;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.InputSystem;

public static class SunBreatherRigValidation
{
    private const string Folder = "Assets/Characters/SunBreathers/";
    public static void Run()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        using var report = new StreamWriter(Path.GetFullPath("../sun-breather-rig-tests.txt")) { AutoFlush = true };
        void Check(bool condition, string message)
        { report.WriteLine((condition ? "PASS " : "FAIL ") + message); if (!condition) throw new InvalidOperationException(message); }
        var models = new GameObject[2];
        var actors = new GameObject[2];
        var animators = new Animator[2];
        for (int i=0; i<2; i++)
        {
            string name = i==0 ? "Tanjiro" : "Yoriichi";
            string path = Folder + name + "/" + name + "_Humanoid.fbx";
            var importer = (ModelImporter)AssetImporter.GetAtPath(path);
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.importAnimation = true; importer.importCameras = false; importer.importLights = false;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.isReadable = true; importer.bakeAxisConversion = true;
            importer.maxBonesPerVertex = 4; importer.skinWeights = ModelImporterSkinWeights.Custom;
            importer.SaveAndReimport();
            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Transform[] transforms = model.GetComponentsInChildren<Transform>(true);
            HumanBone[] human = Enumerable.Range(0,(int)HumanBodyBones.LastBone)
                .Where(bone => transforms.Any(value => value.name == ((HumanBodyBones)bone).ToString()))
                .Select(bone => new HumanBone { boneName=((HumanBodyBones)bone).ToString(), humanName=HumanTrait.BoneName[bone],
                    limit=new HumanLimit { useDefaultValues=true } }).ToArray();
            SkeletonBone[] referencePose=HumanoidReferencePose(model,human,report);
            importer.humanDescription = new HumanDescription {
                human=human, skeleton=referencePose,
                upperArmTwist=.5f,lowerArmTwist=.5f,upperLegTwist=.5f,lowerLegTwist=.5f,armStretch=.05f,legStretch=.05f,feetSpacing=0 };
            importer.animationType=ModelImporterAnimationType.Human;
            importer.avatarSetup=ModelImporterAvatarSetup.CreateFromThisModel;
            var clips=importer.defaultClipAnimations;
            Check(clips.Length>0,name+" contains the shared diagnostic motion");
            clips[0].name="SharedRigCheck"; clips[0].loopTime=true;
            clips[0].lockRootRotation=true; clips[0].lockRootHeightY=true; clips[0].lockRootPositionXZ=true;
            importer.clipAnimations=new[] { clips[0] }; importer.SaveAndReimport();
            models[i]=AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var avatar=AssetDatabase.LoadAllAssetsAtPath(path).OfType<Avatar>().FirstOrDefault();
            Check(avatar!=null && avatar.isHuman && avatar.isValid,name+" imports as a valid Unity Humanoid Avatar");
            foreach (string map in new[] { "Albedo","Normal" })
            {
                string texturePath=Folder+name+"/"+name+"_"+map+".png";
                var texture=(TextureImporter)AssetImporter.GetAtPath(texturePath);
                texture.textureType=map=="Normal" ? TextureImporterType.NormalMap : TextureImporterType.Default;
                texture.sRGBTexture=map!="Normal"; texture.mipmapEnabled=true; texture.isReadable=false;
                texture.maxTextureSize=2048;
                texture.SetPlatformTextureSettings(new TextureImporterPlatformSettings { name="Android",overridden=true,
                    maxTextureSize=2048,format=TextureImporterFormat.ASTC_6x6 }); texture.SaveAndReimport();
            }
            string materialPath=Folder+name+"/"+name+".mat";
            Material material=AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material==null) { material=new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(material,materialPath); }
            material.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>(Folder+name+"/"+name+"_Albedo.png"));
            material.SetTexture("_BumpMap",AssetDatabase.LoadAssetAtPath<Texture2D>(Folder+name+"/"+name+"_Normal.png"));
            material.SetFloat("_BumpScale",.6f); material.SetFloat("_Smoothness",.15f); material.EnableKeyword("_NORMALMAP");
            EditorUtility.SetDirty(material);
            actors[i]=new GameObject(name);
            GameObject visual=UnityEngine.Object.Instantiate(models[i],actors[i].transform);
            visual.name="Model";
            // Enforce T-Pose normalizes the Avatar to Unity +Z. Do not apply an
            // extra visual-axis rotation on top of that Humanoid correction.
            visual.transform.localRotation=Quaternion.identity;
            animators[i]=visual.GetComponent<Animator>();
            animators[i].avatar=avatar; animators[i].applyRootMotion=false; animators[i].cullingMode=AnimatorCullingMode.AlwaysAnimate;
            foreach (SkinnedMeshRenderer renderer in actors[i].GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                renderer.sharedMaterial=material; renderer.quality=SkinQuality.Bone4; renderer.updateWhenOffscreen=true;
                Check(renderer.sharedMesh.boneWeights.All(weight => Mathf.Abs(weight.weight0+weight.weight1+weight.weight2+weight.weight3-1)<.001f),
                    name+" has normalized skin weights with at most four influences");
            }
            Check(animators[i].GetBoneTransform(HumanBodyBones.LeftHand)!=null && animators[i].GetBoneTransform(HumanBodyBones.RightFoot)!=null,
                name+" resolves the Humanoid hand and foot mappings");
            report.WriteLine(name+" human scale="+animators[i].humanScale+"; height="+actors[i].GetComponentInChildren<SkinnedMeshRenderer>().bounds.size.y);
        }
        string controllerPath=Folder+"SharedRigCheck.controller";
        var controller=AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath) ?? AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
        AnimatorState state=controller.layers[0].stateMachine.states.FirstOrDefault().state;
        if (state==null) state=controller.layers[0].stateMachine.AddState("SharedRigCheck");
        AnimationClip shared=AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GetAssetPath(models[0])).OfType<AnimationClip>().First(clip=>clip.name=="SharedRigCheck");
        Check(shared.humanMotion,"The shared clip is Humanoid motion, not character-specific transform paths");
        state.motion=shared; controller.layers[0].stateMachine.defaultState=state; EditorUtility.SetDirty(controller);
        foreach (var animator in animators) { animator.runtimeAnimatorController=controller; animator.Rebind(); animator.Update(0); }
        string[] Skeleton(Animator animator) => animator.GetComponentsInChildren<Transform>(true)
            .Where(value=>value.name!="Tanjiro_Body" && value.name!="Yoriichi_Body" && value!=animator.transform)
            .Select(value=>value.name+":"+(value.parent==animator.transform ? "Model" : value.parent.name)).OrderBy(value=>value).ToArray();
        Check(Skeleton(animators[0]).SequenceEqual(Skeleton(animators[1])),"Both models have exactly the same bone names and hierarchy");
        var handlers=animators.Select(animator=>new HumanPoseHandler(animator.avatar,animator.transform)).ToArray();
        foreach (float time in new[] { 0f,.333f,.666f })
        {
            var poses=new HumanPose[2];
            for (int i=0; i<2; i++)
            {
                animators[i].Play(state.name,0,time); animators[i].Update(0);
                poses[i]=new HumanPose { muscles=new float[HumanTrait.MuscleCount] }; handlers[i].GetHumanPose(ref poses[i]);
                Check(poses[i].muscles.All(value=>!float.IsNaN(value) && !float.IsInfinity(value)),actors[i].name+" has a finite retargeted pose at "+time);
            }
            float error=poses[0].muscles.Zip(poses[1].muscles,(a,b)=>Mathf.Abs(a-b)).Average();
            Check(error<.03f,$"The same clip retargets onto both proportions at {time}: mean muscle delta={error:F5}");
            if (time>.3f && time<.4f) Check(poses[0].muscles.Any(value=>Mathf.Abs(value)>.2f),"The shared clip actually moves the body, not just an idle T-pose");
        }
        foreach (var handler in handlers) handler.Dispose();
        for (int i=0; i<2; i++)
        {
            actors[i].transform.position=Vector3.zero;
            animators[i].Play(state.name,0,0); animators[i].Update(0);
            PrefabUtility.SaveAsPrefabAsset(actors[i],Folder+actors[i].name+".prefab");
            actors[i].transform.position=new Vector3(i==0 ? .7f : -.7f,0,0);
            animators[i].Play(state.name,0,.333f); animators[i].Update(0);
        }
        RenderSettings.ambientMode=AmbientMode.Flat; RenderSettings.ambientLight=Color.white*.55f;
        var light=new GameObject("Rig preview light").AddComponent<Light>(); light.type=LightType.Directional;
        light.transform.rotation=Quaternion.Euler(35,-150,0); light.intensity=1;
        var camera=new GameObject("Rig preview camera").AddComponent<Camera>(); camera.tag="MainCamera";
        camera.transform.position=new Vector3(0,1.05f,4.5f); camera.transform.LookAt(new Vector3(0,.95f,0));
        camera.clearFlags=CameraClearFlags.SolidColor; camera.backgroundColor=new Color(.12f,.13f,.15f);
        camera.orthographic=true; camera.orthographicSize=1.15f;
        var target=RenderTexture.GetTemporary(1280,800,24);camera.targetTexture=target;camera.Render();camera.Render();
        RenderTexture.active=target;var image=new Texture2D(1280,800,TextureFormat.RGB24,false);
        image.ReadPixels(new Rect(0,0,1280,800),0,0);image.Apply();
        File.WriteAllBytes(Path.GetFullPath("../sun-breathers-shared-rig.png"),image.EncodeToPNG());
        camera.targetTexture=null;RenderTexture.active=null;RenderTexture.ReleaseTemporary(target);UnityEngine.Object.DestroyImmediate(image);
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(),Folder+"SharedRigDemo.unity");
        BuildThirdPersonPlayers(Check, report);
        ValidateGarments(report,Check);
        AssetDatabase.SaveAssets(); report.WriteLine("ALL SHARED HUMANOID RIG CHECKS PASSED");
    }

    private static SkeletonBone[] HumanoidReferencePose(GameObject model, HumanBone[] mapping, StreamWriter report)
    {
        // Use the editor's own Enforce T-Pose routine, including forward-axis
        // normalization. A valid Avatar alone does not prove retargeting works.
        var instance=UnityEngine.Object.Instantiate(model);
        try
        {
            var transforms=instance.GetComponentsInChildren<Transform>(true).ToDictionary(value=>value.name);
            var tool=typeof(ModelImporter).Assembly.GetType("UnityEditor.AvatarSetupTool",true);
            var wrapper=tool.GetNestedType("BoneWrapper",BindingFlags.NonPublic);
            var bones=Array.CreateInstance(wrapper,HumanTrait.BoneCount);
            for (int b=0; b<HumanTrait.BoneCount; b++)
            {
                string humanName=HumanTrait.BoneName[b];
                string boneName=mapping.FirstOrDefault(value=>value.humanName==humanName).boneName;
                Transform bone=boneName!=null && transforms.TryGetValue(boneName,out Transform found) ? found : null;
                bones.SetValue(Activator.CreateInstance(wrapper,new object[] { humanName,bone }),b);
            }
            var getError=tool.GetMethod("GetPoseError",BindingFlags.Public|BindingFlags.Static);
            report.WriteLine(model.name+" reference T-pose error before="+getError.Invoke(null,new object[] { bones }));
            tool.GetMethod("MakePoseValid",BindingFlags.Public|BindingFlags.Static).Invoke(null,new object[] { bones });
            float error=(float)getError.Invoke(null,new object[] { bones });
            report.WriteLine(model.name+" reference T-pose error after="+error);
            if (error>.001f) throw new InvalidOperationException(model.name+" does not have a valid reference T-pose");
            return (SkeletonBone[])tool.GetMethod("GetSkeletonBones",BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static)
                .Invoke(null,new object[] { instance.transform });
        }
        finally { UnityEngine.Object.DestroyImmediate(instance); }
    }

    private static void BuildThirdPersonPlayers(Action<bool,string> check, StreamWriter report)
    {
        const string starter = "Assets/StarterAssets/ThirdPersonController/";
        var template = AssetDatabase.LoadAssetAtPath<GameObject>(starter+"Prefabs/PlayerArmature.prefab");
        var locomotion = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(starter+"Character/Animations/StarterAssetsThirdPerson.controller");
        check(template != null && locomotion != null,"Reuse the project's Starter Assets player and locomotion controller");
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        floor.name="Movement test floor"; floor.transform.position=new Vector3(0,-.5f,0); floor.transform.localScale=new Vector3(40,1,40);
        var camera = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(starter+"Prefabs/MainCamera.prefab")).GetComponent<Camera>();
        camera.tag="MainCamera";
        camera.transform.SetPositionAndRotation(new Vector3(0,1.5f,-4),Quaternion.identity);
        var animatorField = typeof(ThirdPersonController).GetField("animator",BindingFlags.NonPublic|BindingFlags.Instance);
        foreach (string name in new[] { "Tanjiro","Yoriichi" })
        {
            float height=name=="Tanjiro" ? 1.65f : 1.85f;
            var player=UnityEngine.Object.Instantiate(template);
            player.name=name+"ThirdPerson";
            var controller=player.GetComponent<ThirdPersonController>();
            var target=controller.CinemachineCameraTarget.transform;
            UnityEngine.Object.DestroyImmediate(player.GetComponent<Animator>());
            foreach (Transform child in player.transform.Cast<Transform>().ToArray())
                if (child!=target) UnityEngine.Object.DestroyImmediate(child.gameObject);
            var visual=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Folder+name+".prefab").transform.Find("Model").gameObject,player.transform);
            visual.name="Model";
            var animator=visual.GetComponent<Animator>();
            animator.runtimeAnimatorController=locomotion; animator.applyRootMotion=false;
            visual.AddComponent<ThirdPersonAnimationEvents>().Bind(controller);
            var capsule=player.GetComponent<CharacterController>();
            capsule.height=height; capsule.center=Vector3.up*(height*.5f+.02f); capsule.radius=name=="Tanjiro" ? .25f : .28f;
            controller.GroundedRadius=capsule.radius;
            target.localPosition=Vector3.up*(height*.83f);
            var gravity=player.AddComponent<InfinityGravityBody>(); gravity.defaultGravityStrength=Mathf.Abs(controller.Gravity);
            var input=player.GetComponent<StarterAssetsInputs>();
            input.move=input.look=Vector2.zero; input.jump=input.sprint=input.ultraJump=false;
            var playerInput=player.GetComponent<PlayerInput>();
            check(playerInput.actions!=null && playerInput.defaultActionMap=="Player" &&
                playerInput.notificationBehavior==PlayerNotifications.SendMessages,name+" keeps the existing keyboard/gamepad/touch input contract");
            // Save before Initialize creates its runtime capsule/rigidbody.
            animator.Rebind(); animator.Play("Idle Walk Run Blend",0,0); animator.Update(0);
            PrefabUtility.SaveAsPrefabAsset(player,Folder+player.name+".prefab");
            controller.Initialize();
            check((Animator)animatorField.GetValue(controller)==animator,name+" controller resolves the child Humanoid Animator");
            check(player.GetComponent<Animator>()==null && !animator.applyRootMotion,name+" moves through the existing motor, not animation root motion");
            float dt=1f/60f;
            for (int f=0; f<60; f++) { controller.Simulate(dt); animator.Update(dt); }
            check(controller.Grounded && animator.GetBool("Grounded"),name+" settles on the floor with grounded animation");
            ReportPose(animator,report,"idle");
            check(animator.GetBoneTransform(HumanBodyBones.LeftHand).position.y <
                animator.GetBoneTransform(HumanBodyBones.LeftUpperArm).position.y-height*.1f,
                name+" retargeted idle keeps its hands below its shoulders");
            check(player.transform.InverseTransformPoint(animator.GetBoneTransform(HumanBodyBones.LeftUpperArm).position).x<0 &&
                player.transform.InverseTransformPoint(animator.GetBoneTransform(HumanBodyBones.RightUpperArm).position).x>0,
                name+" Humanoid faces the same +Z direction as its gameplay root");
            Vector3 idleHand=animator.transform.InverseTransformPoint(animator.GetBoneTransform(HumanBodyBones.LeftHand).position);
            CapturePose(player,height,name+"-idle");
            float initialZ=player.transform.position.z;
            input.MoveInput(Vector2.up);
            for (int f=0; f<90; f++) { controller.Simulate(dt); animator.Update(dt); }
            check(player.transform.position.z-initialZ>2 && animator.GetFloat("Speed")>1.5f,name+" walking input moves the capsule and drives Speed/MotionSpeed");
            ReportPose(animator,report,"walk");
            check((animator.transform.InverseTransformPoint(animator.GetBoneTransform(HumanBodyBones.LeftHand).position)-idleHand).magnitude>.05f,
                name+" walking actually changes the retargeted bone pose");
            CapturePose(player,height,name+"-walk");
            input.SprintInput(true);
            for (int f=0; f<60; f++) { controller.Simulate(dt); animator.Update(dt); }
            check(animator.GetFloat("Speed")>5,name+" sprint input drives the shared running blend");
            input.MoveInput(Vector2.zero); input.SprintInput(false); input.JumpInput(true);
            float initialY=player.transform.position.y;
            for (int f=0; f<12; f++) { controller.Simulate(dt); animator.Update(dt); }
            check(player.transform.position.y>initialY+.3f && animator.GetBool("Jump") && !animator.GetBool("Grounded"),name+" normal jump lifts the capsule and drives the jump animation");
            ReportPose(animator,report,"jump");
            CapturePose(player,height,name+"-jump");
            for (int f=0; f<150; f++) { controller.Simulate(dt); animator.Update(dt); }
            check(controller.Grounded && Mathf.Abs(player.transform.position.y-initialY)<.1f,name+" lands without animation moving it back to its spawn");
            input.LookInput(new Vector2(20,10));
            controller.SimulateCamera(.1f);
            check(Quaternion.Angle(target.rotation,Quaternion.identity)>1,name+" existing camera target responds to look input");
            gravity.SetGravity(Vector3.left,22,true,8); controller.Simulate(dt); animator.Update(dt);
            check(Vector3.Dot(player.transform.up,Vector3.right)>.99f && animator.avatar.isValid,name+" preserves the castle's sideways-gravity alignment");
            check(visual.GetComponent<ThirdPersonAnimationEvents>()!=null,name+" child animation events have a player-root receiver");
            report.WriteLine(name+" third-person test finished at "+player.transform.position);
            UnityEngine.Object.DestroyImmediate(player);
        }
        var legacy=UnityEngine.Object.Instantiate(template);
        var legacyController=legacy.GetComponent<ThirdPersonController>(); legacyController.Initialize();
        check((Animator)animatorField.GetValue(legacyController)==legacy.GetComponent<Animator>(),"Legacy root-Animator players still resolve their original Animator");
        var legacyAnimator=legacy.GetComponent<Animator>(); legacyAnimator.Rebind(); legacyAnimator.Play("Idle Walk Run Blend",0,0); legacyAnimator.Update(0);
        ReportPose(legacyAnimator,report,"legacy idle"); CapturePose(legacy,1.8f,"legacy-idle");
        UnityEngine.Object.DestroyImmediate(legacy);
        var demo=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Folder+"TanjiroThirdPerson.prefab"));
        var follow=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(starter+"Prefabs/PlayerFollowCamera.prefab")).GetComponent<CinemachineVirtualCamera>();
        follow.Follow=demo.GetComponent<ThirdPersonController>().CinemachineCameraTarget.transform;
        follow.LookAt=follow.Follow;
        RenderSettings.ambientMode=AmbientMode.Flat; RenderSettings.ambientLight=Color.white*.55f;
        var light=new GameObject("Demo light").AddComponent<Light>(); light.type=LightType.Directional;
        light.transform.rotation=Quaternion.Euler(40,-35,0);
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(),Folder+"ThirdPersonDemo.unity");
    }

    private static void ReportPose(Animator animator, StreamWriter report, string label)
    {
        report.WriteLine(label+" animator speed="+animator.speed+" state="+animator.GetCurrentAnimatorStateInfo(0).shortNameHash+
            " time="+animator.GetCurrentAnimatorStateInfo(0).normalizedTime+" clips="+
            string.Join(",",animator.GetCurrentAnimatorClipInfo(0).Select(info=>info.clip.name+" human="+info.clip.humanMotion+" weight="+info.weight)));
        foreach (var bone in new[] { HumanBodyBones.LeftUpperArm,HumanBodyBones.LeftLowerArm,HumanBodyBones.LeftHand,HumanBodyBones.LeftFoot })
            report.WriteLine(label+" "+bone+"="+animator.transform.InverseTransformPoint(animator.GetBoneTransform(bone).position));
    }

    private static void CapturePose(GameObject player, float height, string label, Vector3? direction=null)
    {
        // Manual batch-mode renders can reuse the frame's first skinning result.
        // Bake each sampled pose so visual QA sees the current bone matrices.
        var baked=new System.Collections.Generic.List<GameObject>();
        var renderers=player.GetComponentsInChildren<SkinnedMeshRenderer>();
        foreach (var skin in renderers)
        {
            var mesh=new Mesh(); skin.BakeMesh(mesh);
            var proxy=new GameObject("Baked pose"); proxy.transform.SetPositionAndRotation(skin.transform.position,skin.transform.rotation);
            proxy.AddComponent<MeshFilter>().sharedMesh=mesh;
            proxy.AddComponent<MeshRenderer>().sharedMaterials=skin.sharedMaterials;
            skin.enabled=false; baked.Add(proxy);
        }
        var camera=new GameObject("Pose review camera").AddComponent<Camera>();
        Bounds bounds=baked[0].GetComponent<Renderer>().bounds;
        foreach (var proxy in baked.Skip(1)) bounds.Encapsulate(proxy.GetComponent<Renderer>().bounds);
        Vector3 centre=bounds.center;
        camera.transform.position=centre+(direction??Vector3.forward)*4;
        camera.transform.LookAt(centre); camera.orthographic=true;
        camera.orthographicSize=Mathf.Max(bounds.extents.y,bounds.extents.x/.75f)*1.15f;
        camera.clearFlags=CameraClearFlags.SolidColor; camera.backgroundColor=new Color(.12f,.13f,.15f);
        var target=RenderTexture.GetTemporary(600,800,24); camera.targetTexture=target;
        camera.Render(); camera.Render(); RenderTexture.active=target;
        var image=new Texture2D(600,800,TextureFormat.RGB24,false);
        image.ReadPixels(new Rect(0,0,600,800),0,0); image.Apply();
        File.WriteAllBytes(Path.GetFullPath("../sun-breather-"+label+".png"),image.EncodeToPNG());
        camera.targetTexture=null; RenderTexture.active=null; RenderTexture.ReleaseTemporary(target);
        UnityEngine.Object.DestroyImmediate(image); UnityEngine.Object.DestroyImmediate(camera.gameObject);
        foreach (var proxy in baked) { UnityEngine.Object.DestroyImmediate(proxy.GetComponent<MeshFilter>().sharedMesh); UnityEngine.Object.DestroyImmediate(proxy); }
        foreach (var skin in renderers) skin.enabled=true;
    }

    private static void ValidateGarments(StreamWriter report, Action<bool,string> check)
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        RenderSettings.ambientMode=AmbientMode.Flat; RenderSettings.ambientLight=Color.white*.65f;
        foreach (string name in new[] { "Tanjiro","Yoriichi" })
        {
            var player=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Folder+name+"ThirdPerson.prefab"));
            var animator=player.GetComponentInChildren<Animator>();
            var skin=player.GetComponentInChildren<SkinnedMeshRenderer>();
            check(!skin.bones.Any(bone=>bone.name.Contains("Sleeve")),name+" uses compressed sleeve geometry without runtime garment anchors");
            Color[] colours=skin.sharedMesh.colors;
            check(colours.Length==skin.sharedMesh.vertexCount && colours.Count(colour=>colour.r>.65f)>500,name+" exports the loose-fabric vertex mask");
            int worst=0,total=colours.Count(colour=>colour.r>.65f);
            float maximumArmDistance=0;
            foreach (string motion in new[] { "walk","run","jump" })
                for (int phase=0; phase<12; phase++)
                {
                    animator.SetFloat("Speed",motion=="run" ? 6 : 2); animator.SetFloat("MotionSpeed",1);
                    animator.SetBool("Grounded",motion!="jump"); animator.SetBool("Jump",motion=="jump"); animator.SetBool("FreeFall",false);
                    animator.Play(motion=="jump" ? "JumpStart" : "Idle Walk Run Blend",0,phase/12f); animator.Update(0);
                    int fixedCount=GarmentIntrusions(player,animator,skin,colours);
                    float distance=SleeveArmDistance(animator,skin,colours);
                    maximumArmDistance=Mathf.Max(maximumArmDistance,distance);
                    worst=Mathf.Max(worst,fixedCount);
                    report.WriteLine($"{name} {motion} phase {phase}: sleeve/body intrusions {fixedCount} of {total}; maximum sleeve/arm distance={distance:F3}m");
                    if ((phase==0 || phase==3 || phase==6 || phase==9) && motion!="jump")
                    {
                        CapturePose(player,1.65f,name+"-garment-"+motion+"-"+phase+"-front");
                        CapturePose(player,1.65f,name+"-garment-"+motion+"-"+phase+"-back",Vector3.back);
                        CapturePose(player,1.65f,name+"-garment-"+motion+"-"+phase+"-side",Vector3.right);
                    }
                }
            check(worst<total*.08f,name+" keeps compressed sleeves outside the central torso throughout 36 locomotion poses: worst="+worst+"/"+total);
            check(maximumArmDistance<.19f,name+" sleeve fabric stays close to the arms instead of forming suspended hoops: maximum="+maximumArmDistance);
            UnityEngine.Object.DestroyImmediate(player);
        }
    }

    private static float SleeveArmDistance(Animator animator, SkinnedMeshRenderer skin, Color[] mask)
    {
        var mesh=new Mesh(); skin.BakeMesh(mesh); var points=mesh.vertices;
        float maximum=0;
        for (int v=0; v<points.Length; v++)
        {
            if (mask[v].r<.95f) continue;
            bool left=mask[v].g>.5f;
            Vector3 shoulder=animator.GetBoneTransform(left ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm).position;
            Vector3 elbow=animator.GetBoneTransform(left ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm).position;
            Vector3 hand=animator.GetBoneTransform(left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand).position;
            Vector3 point=skin.transform.position+skin.transform.rotation*points[v];
            float Segment(Vector3 a, Vector3 b) => Vector3.Distance(point,a+(b-a)*Mathf.Clamp01(Vector3.Dot(point-a,b-a)/(b-a).sqrMagnitude));
            maximum=Mathf.Max(maximum,Mathf.Min(Segment(shoulder,elbow),Segment(elbow,hand)));
        }
        UnityEngine.Object.DestroyImmediate(mesh); return maximum;
    }

    private static int GarmentIntrusions(GameObject player, Animator animator, SkinnedMeshRenderer skin, Color[] mask)
    {
        var mesh=new Mesh(); skin.BakeMesh(mesh);
        Vector3 hips=player.transform.InverseTransformPoint(animator.GetBoneTransform(HumanBodyBones.Hips).position);
        Vector3 chest=player.transform.InverseTransformPoint(animator.GetBoneTransform(HumanBodyBones.UpperChest).position);
        float scale=animator.humanScale/.9223f;
        Vector3[] points=mesh.vertices; int intrusions=0;
        for (int v=0; v<points.Length; v++)
        {
            if (mask[v].r<.65f) continue;
            // BakeMesh(false) includes the skin's FBX unit scale, but not its rotation.
            Vector3 p=player.transform.InverseTransformPoint(skin.transform.position+skin.transform.rotation*points[v]);
            if (p.y<hips.y-.12f*scale || p.y>chest.y+.04f*scale) continue;
            Vector3 centre=Vector3.Lerp(hips,chest,Mathf.InverseLerp(hips.y,chest.y,p.y));
            // An infinite half-space behind the chest incorrectly classified
            // fabric beside/behind the back as embedded inside the body.
            float across=(p.x-centre.x)/(.115f*scale);
            float depth=(p.z-centre.z)/(.085f*scale);
            if (across*across+depth*depth<1) intrusions++;
        }
        UnityEngine.Object.DestroyImmediate(mesh); return intrusions;
    }
}
