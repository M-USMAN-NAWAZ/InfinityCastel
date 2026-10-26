using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

public static class TanjiroSwordAnimationSetup
{
    private const string Characters = "Assets/Characters/SunBreathers/";
    private const string Folder = Characters + "SwordActions/";
    private const string Locomotion = "Assets/StarterAssets/ThirdPersonController/Character/Animations/StarterAssetsThirdPerson.controller";
    private const float DrawDuration = 3f, SheatheDuration = 3f;
    private static Vector3 BladeAxis = new Vector3(-.25f, -.96f, -.12f).normalized;
    private static Vector3 Grip = new Vector3(-.085f, 1.105f, .14f);
    private static Quaternion SwordRotation(Vector3 axis) => TanjiroSwordPoseBake.BladeRotation(axis);

    public static void Inspect()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var actor = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Characters + "TanjiroThirdPerson.prefab"));
        var animator = actor.GetComponentInChildren<Animator>();
        animator.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(Locomotion);
        animator.Rebind(); animator.Play("Idle Walk Run Blend", 0, 0); animator.Update(0);
        using var report = new StreamWriter(Path.GetFullPath("../sword-animation-inspection.txt"));
        foreach (var bone in new[] { HumanBodyBones.Hips, HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand,
            HumanBodyBones.RightIndexProximal, HumanBodyBones.RightLittleProximal, HumanBodyBones.LeftHand })
        {
            var t=animator.GetBoneTransform(bone);
            report.WriteLine(bone+" position="+actor.transform.InverseTransformPoint(t.position).ToString("F5")+" rotation="+t.rotation+" scale="+t.lossyScale);
        }
        var skin=actor.GetComponentInChildren<SkinnedMeshRenderer>(); var mesh=new Mesh(); skin.BakeMesh(mesh);
        report.WriteLine("Skin transform="+skin.transform.localToWorldMatrix+" mesh bounds="+mesh.bounds);
        report.WriteLine("Muscles="+string.Join("|",HumanTrait.MuscleName));
        var clip=AssetDatabase.LoadAllAssetsAtPath(Characters+"Tanjiro/Tanjiro_Humanoid.fbx").OfType<AnimationClip>().First(c=>c.humanMotion);
        report.WriteLine("Bindings="+string.Join("|",AnimationUtility.GetCurveBindings(clip).Select(b=>b.propertyName)));
        var oldDraw=AssetDatabase.LoadAssetAtPath<AnimationClip>(Folder+"DrawKatana.anim");
        foreach(var binding in AnimationUtility.GetCurveBindings(oldDraw))
        {
            var curve=AnimationUtility.GetEditorCurve(oldDraw,binding);float min=curve.keys.Min(k=>k.value),max=curve.keys.Max(k=>k.value);
            if(min<-1.01f || max>1.01f) report.WriteLine($"OUTSIDE JOINT LIMIT {binding.propertyName}: {min:F3} .. {max:F3}");
        }
        animator.runtimeAnimatorController=AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(Folder+"TanjiroSword.controller");
        animator.Rebind();animator.SetLayerWeight(1,1);
        var armBones=new[] { HumanBodyBones.RightUpperArm,HumanBodyBones.RightLowerArm,HumanBodyBones.RightHand,HumanBodyBones.LeftUpperArm,HumanBodyBones.LeftLowerArm,HumanBodyBones.LeftHand };
        var previous=new Quaternion[armBones.Length];
        for(int f=0;f<=72;f++)
        {
            animator.Play("Sword Actions.Draw",1,f/72f);animator.Update(0);
            for(int b=0;b<armBones.Length;b++)
            {
                Quaternion rotation=animator.GetBoneTransform(armBones[b]).localRotation;float angle=Quaternion.Angle(previous[b],rotation);
                if(f>0 && angle>15) report.WriteLine($"JOINT STEP frame {f} {armBones[b]}: {angle:F2} degrees");
                previous[b]=rotation;
            }
        }
        Capture(actor,"inspect",true);
        Object.DestroyImmediate(mesh); Object.DestroyImmediate(actor);
    }

    public static void Build()
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("Build in the isolated validation project.");
        Directory.CreateDirectory(Folder); AssetDatabase.Refresh();
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        using var report=new StreamWriter(Path.GetFullPath("../sword-animation-tests.txt")) { AutoFlush=true };
        void Check(bool ok,string text) { report.WriteLine((ok ? "PASS " : "FAIL ")+text); if (!ok) throw new InvalidOperationException(text); }
        var actor=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Characters+"TanjiroThirdPerson.prefab"));
        var animator=actor.GetComponentInChildren<Animator>();
        animator.runtimeAnimatorController=AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(Locomotion);
        animator.Rebind();animator.Play("Idle Walk Run Blend",0,0);animator.Update(0);
        var skin=actor.GetComponentInChildren<SkinnedMeshRenderer>();
        var bodyMesh=RemoveFixedHilt(actor,skin,report);
        var motion=TanjiroSwordPoseBake.Bake(animator,Grip,BladeAxis,Folder,report);
        var locomotion=TanjiroSwordPoseBake.BakeLocomotion(animator,Folder,report);
        var draw=motion.draw;var sheathe=motion.sheathe;var hold=motion.hold;
        var gripLocal=motion.gripLocal;var gripRotation=motion.gripRotation;
        AnimationUtility.SetAnimationEvents(draw,new[] { Event(.60f,"OnSwordDrawGrip"),Event(2.98f,"OnSwordDrawFinished") });
        AnimationUtility.SetAnimationEvents(sheathe,new[] { Event(2.40f,"OnSwordSheatheGrip"),Event(2.98f,"OnSwordSheatheFinished") });
        Check(draw.humanMotion && sheathe.humanMotion && hold.humanMotion,"Draw, ready and sheathe clips are retargetable Humanoid muscle animation");
        Check(AnimationUtility.GetCurveBindings(draw).All(b=>!b.propertyName.StartsWith("Root") && !b.propertyName.Contains("Leg")),"No root-motion or leg curves: existing motor remains authoritative");

        string controllerPath=Folder+"TanjiroSword.controller";
        if(AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath)==null) AssetDatabase.CopyAsset(Locomotion,controllerPath);
        var controller=AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
        for(int l=controller.layers.Length-1;l>=0;l--)
            if(new[]{"Sword Actions","Sword Locomotion","Sword Free Arm"}.Contains(controller.layers[l].name)) controller.RemoveLayer(l);
        var mask=new AvatarMask { name="SwordUpperBody" };
        for(int p=0;p<(int)AvatarMaskBodyPart.LastBodyPart;p++) mask.SetHumanoidBodyPartActive((AvatarMaskBodyPart)p,false);
        foreach(var p in new[] { AvatarMaskBodyPart.Body,AvatarMaskBodyPart.LeftArm,AvatarMaskBodyPart.RightArm,AvatarMaskBodyPart.LeftFingers,AvatarMaskBodyPart.RightFingers,AvatarMaskBodyPart.LeftHandIK,AvatarMaskBodyPart.RightHandIK }) mask.SetHumanoidBodyPartActive(p,true);
        var existingMask=AssetDatabase.LoadAssetAtPath<AvatarMask>(Folder+"SwordUpperBody.mask");
        if(existingMask!=null) { EditorUtility.CopySerialized(mask,existingMask);Object.DestroyImmediate(mask);mask=existingMask; }
        else AssetDatabase.CreateAsset(mask,Folder+"SwordUpperBody.mask");
        var machine=new AnimatorStateMachine { name="Sword Actions" };AssetDatabase.AddObjectToAsset(machine,controller);
        var stowedState=machine.AddState("Stowed");stowedState.writeDefaultValues=false;machine.defaultState=stowedState;
        var drawState=machine.AddState("Draw");drawState.motion=draw;drawState.writeDefaultValues=false;
        var readyState=machine.AddState("Ready");readyState.motion=hold;readyState.writeDefaultValues=false;
        var sheatheState=machine.AddState("Sheathe");sheatheState.motion=sheathe;sheatheState.writeDefaultValues=false;
        foreach(var pair in new[] { (drawState,readyState),(sheatheState,stowedState) })
        { var transition=pair.Item1.AddTransition(pair.Item2);transition.hasExitTime=true;transition.exitTime=1;transition.duration=.08f;transition.hasFixedDuration=true; }
        controller.AddLayer(new AnimatorControllerLayer { name="Sword Actions",defaultWeight=0,avatarMask=mask,stateMachine=machine,blendingMode=AnimatorLayerBlendingMode.Override,iKPass=true });
        ConfigureLocomotion(controller,hold,locomotion.run,locomotion.sprint);
        EditorUtility.SetDirty(controller);AssetDatabase.SaveAssets();

        animator.Rebind();animator.Play("Idle Walk Run Blend",0,0);animator.Update(0);
        var weaponImporter=(ModelImporter)AssetImporter.GetAtPath("Assets/Weapons/YoriichiKatana/YoriichiKatana.fbx");
        weaponImporter.isReadable=true;weaponImporter.SaveAndReimport();
        var weaponPrefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Weapons/YoriichiKatana/YoriichiKatana.prefab");
        var sourceFilter=weaponPrefab.GetComponentInChildren<MeshFilter>();
        var swordMesh=Object.Instantiate(sourceFilter.sharedMesh);swordMesh.name="DrawableYoriichiKatana";
        Matrix4x4 meshSpace=weaponPrefab.transform.worldToLocalMatrix*sourceFilter.transform.localToWorldMatrix;
        swordMesh.vertices=swordMesh.vertices.Select(p=>meshSpace.MultiplyPoint3x4(p)).ToArray();
        swordMesh.normals=swordMesh.normals.Select(n=>meshSpace.inverse.transpose.MultiplyVector(n).normalized).ToArray();
        swordMesh.tangents=swordMesh.tangents.Select(t=> { Vector3 v=meshSpace.MultiplyVector(new Vector3(t.x,t.y,t.z)).normalized;return new Vector4(v.x,v.y,v.z,t.w); }).ToArray();
        swordMesh.RecalculateBounds();swordMesh=SaveMesh(swordMesh,"DrawableKatana.asset");
        Check(Mathf.Abs(swordMesh.bounds.size.y-1.196758f)<.02f,"Weapon mesh is normalized out of FBX centimetres and axis correction");
        var swordMaterial=weaponPrefab.GetComponentInChildren<MeshRenderer>().sharedMaterial;
        var hilt=Object.Instantiate(swordMesh);hilt.name="YoriichiStowedHilt";
        int[] swordTriangles=swordMesh.triangles;Vector3[] swordVertices=swordMesh.vertices;
        hilt.triangles=Enumerable.Range(0,swordTriangles.Length/3).Where(t=>
            (swordVertices[swordTriangles[t*3]].y+swordVertices[swordTriangles[t*3+1]].y+swordVertices[swordTriangles[t*3+2]].y)/3<.175f)
            .SelectMany(t=>new[] { swordTriangles[t*3],swordTriangles[t*3+1],swordTriangles[t*3+2] }).ToArray();
        hilt.RecalculateBounds();hilt=SaveMesh(hilt,"StowedHilt.asset");
        var saya=BuildSaya(swordMesh);
        Material SayaMaterial(string name,Color colour,float metallic,float smoothness)
        {
            var material=AssetDatabase.LoadAssetAtPath<Material>(Folder+name+".mat");
            if(material==null) { material=new Material(Shader.Find("Universal Render Pipeline/Lit"));AssetDatabase.CreateAsset(material,Folder+name+".mat"); }
            material.SetColor("_BaseColor",colour);material.SetFloat("_Metallic",metallic);material.SetFloat("_Smoothness",smoothness);EditorUtility.SetDirty(material);return material;
        }
        var sayaMaterials=new[] { SayaMaterial("SayaBlackLacquer",new Color(.012f,.015f,.021f),.25f,.45f),SayaMaterial("SayaGoldRim",new Color(.65f,.39f,.10f),.8f,.5f) };
        foreach(string name in new[] { "Tanjiro","TanjiroThirdPerson" })
        {
            string path=Characters+name+".prefab";var prefab=PrefabUtility.LoadPrefabContents(path);
            try
            {
                var a=prefab.GetComponentInChildren<Animator>();var s=a.GetComponent<TanjiroSword>() ?? a.gameObject.AddComponent<TanjiroSword>();
                Transform Socket(string label,Transform parent,Vector3 position,Quaternion rotation)
                {
                    var socket=parent.Find(label);if(socket==null) socket=new GameObject(label).transform;
                    socket.SetParent(parent,false);socket.localPosition=position;socket.localRotation=rotation;socket.localScale=Vector3.one;return socket;
                }
                var rightHand=a.GetBoneTransform(HumanBodyBones.RightHand);var hip=a.GetBoneTransform(HumanBodyBones.Hips);
                s.handSocket=Socket("KatanaGrip",rightHand,gripLocal,gripRotation);
                // Align the sheath socket in the same relaxed idle used for authoring.
                var old=a.runtimeAnimatorController;a.runtimeAnimatorController=AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(Locomotion);
                a.Rebind();a.Play("Idle Walk Run Blend",0,0);a.Update(0);
                s.sheathSocket=Socket("KatanaSheath",hip,hip.InverseTransformPoint(prefab.transform.TransformPoint(Grip)),
                    Quaternion.Inverse(hip.rotation)*prefab.transform.rotation*SwordRotation(BladeAxis));
                s.sheathPosition=a.transform.InverseTransformPoint(s.sheathSocket.position);s.sheathRotation=Quaternion.Inverse(a.transform.rotation)*s.sheathSocket.rotation;
                a.runtimeAnimatorController=old;
                var sheath=s.sheath;if(sheath==null) sheath=new GameObject("Drawable Saya",typeof(MeshFilter),typeof(MeshRenderer)).transform;
                sheath.SetParent(s.sheathSocket,false);sheath.localPosition=Vector3.up*(.16f*.58f/s.sheathSocket.lossyScale.y);sheath.localRotation=Quaternion.identity;
                sheath.localScale=Vector3.one*(.58f/s.sheathSocket.lossyScale.x);
                sheath.GetComponent<MeshFilter>().sharedMesh=saya;sheath.GetComponent<MeshRenderer>().sharedMaterials=sayaMaterials;sheath.gameObject.SetActive(false);s.sheath=sheath;
                var sword=s.sword;if(sword==null) sword=new GameObject("Drawable Yoriichi Katana",typeof(MeshFilter),typeof(MeshRenderer)).transform;
                sword.SetParent(s.sheathSocket,false);sword.localPosition=Vector3.zero;sword.localRotation=Quaternion.identity;
                sword.localScale=Vector3.one*(.58f/s.sheathSocket.lossyScale.x);
                sword.GetComponent<MeshFilter>().sharedMesh=hilt;sword.GetComponent<MeshRenderer>().sharedMaterial=swordMaterial;sword.gameObject.SetActive(false);
                s.enableSwordLocomotion=true;
                s.swordScale=.58f;s.sword=sword;s.actionController=controller;s.fullSwordMesh=swordMesh;s.stowedHiltMesh=hilt;s.drawableBodyMesh=bodyMesh;s.body=prefab.GetComponentInChildren<SkinnedMeshRenderer>();
                PrefabUtility.SaveAsPrefabAsset(prefab,path);
            }
            finally { PrefabUtility.UnloadPrefabContents(prefab); }
        }
        Object.DestroyImmediate(actor);AssetDatabase.SaveAssets();
        Validate(Check,report);
        weaponImporter.isReadable=false;weaponImporter.SaveAndReimport();
        report.WriteLine("ALL SWORD ANIMATION CHECKS PASSED");
    }

    private static AnimationEvent Event(float time,string name) => new AnimationEvent { time=time,functionName=name };

    public static void ConfigureLocomotion(AnimatorController controller,AnimationClip hold,AnimationClip run,AnimationClip sprint)
    {
        if(!controller.parameters.Any(p=>p.name=="SwordStride")) controller.AddParameter("SwordStride",AnimatorControllerParameterType.Float);
        var ready=controller.layers.First(l=>l.name=="Sword Actions").stateMachine.states.First(s=>s.state.name=="Ready").state;
        ready.motion=hold;
        var carry=controller.layers.FirstOrDefault(l=>l.name=="Sword Locomotion");
        var mask=AssetDatabase.LoadAssetAtPath<AvatarMask>(Folder+"SwordRunningRight.mask");
        if(mask==null) {mask=new AvatarMask {name="SwordRunningRight"};AssetDatabase.CreateAsset(mask,Folder+"SwordRunningRight.mask");}
        for(int p=0;p<(int)AvatarMaskBodyPart.LastBodyPart;p++) mask.SetHumanoidBodyPartActive((AvatarMaskBodyPart)p,false);
        foreach(var p in new[]{AvatarMaskBodyPart.Body,AvatarMaskBodyPart.RightArm,AvatarMaskBodyPart.RightFingers,AvatarMaskBodyPart.RightHandIK}) mask.SetHumanoidBodyPartActive(p,true);
        EditorUtility.SetDirty(mask);
        if(carry==null)
        {
            var machine=new AnimatorStateMachine {name="Sword Locomotion"};AssetDatabase.AddObjectToAsset(machine,controller);
            var state=machine.AddState("Carry");state.writeDefaultValues=false;machine.defaultState=state;
            carry=new AnimatorControllerLayer {name="Sword Locomotion",stateMachine=machine,defaultWeight=0,avatarMask=mask,blendingMode=AnimatorLayerBlendingMode.Override};
            controller.AddLayer(carry);
        }
        var running=carry.stateMachine.states.First().state;
        var tree=running.motion as BlendTree;
        if(tree==null) {tree=new BlendTree {name="Sword one-hand run sprint"};AssetDatabase.AddObjectToAsset(tree,controller);}
        tree.blendType=BlendTreeType.Simple1D;tree.blendParameter="SwordStride";tree.useAutomaticThresholds=false;
        tree.children=new[] {new ChildMotion {motion=run,threshold=1,timeScale=1},new ChildMotion {motion=sprint,threshold=2,timeScale=1.25f}};
        running.motion=tree;EditorUtility.SetDirty(tree);EditorUtility.SetDirty(controller);
        var freeMask=AssetDatabase.LoadAssetAtPath<AvatarMask>(Folder+"SwordFreeArm.mask");
        if(freeMask==null) {freeMask=new AvatarMask {name="SwordFreeArm"};AssetDatabase.CreateAsset(freeMask,Folder+"SwordFreeArm.mask");}
        for(int p=0;p<(int)AvatarMaskBodyPart.LastBodyPart;p++) freeMask.SetHumanoidBodyPartActive((AvatarMaskBodyPart)p,false);
        freeMask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftArm,true);freeMask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftFingers,true);
        EditorUtility.SetDirty(freeMask);
        if(!controller.layers.Any(l=>l.name=="Sword Free Arm")) controller.AddLayer(new AnimatorControllerLayer {
            name="Sword Free Arm",stateMachine=controller.layers[0].stateMachine,syncedLayerIndex=0,syncedLayerAffectsTiming=false,
            avatarMask=freeMask,defaultWeight=0,blendingMode=AnimatorLayerBlendingMode.Override});
        // Synced layers created through the API need explicit motion overrides;
        // otherwise Unity leaves them empty instead of replaying the base gait.
        var layers=controller.layers;var free=layers.First(l=>l.name=="Sword Free Arm");
        foreach(var state in layers[0].stateMachine.states) free.SetOverrideMotion(state.state,state.state.motion);
        controller.layers=layers;EditorUtility.SetDirty(controller);
    }
    private static Mesh BuildSaya(Mesh blade)
    {
        var verts=new List<Vector3>();var black=new List<int>();var gold=new List<int>();
        float[] rings={0,.018f,.10f,.25f,.45f,.65f,.78f,.85f};const int sides=12;
        var points=blade.vertices;
        for(int ring=0;ring<rings.Length;ring++)
        {
            float y=rings[ring];var section=points.Where(p=>Mathf.Abs(p.y-Mathf.Clamp(y+.16f,.20f,.99f))<.035f).ToArray();
            float centre=section.Length>0 ? (section.Min(p=>p.x)+section.Max(p=>p.x))*.5f : .04f;
            float taper=ring==rings.Length-1 ? .35f : 1;
            for(int s=0;s<sides;s++) { float a=s*Mathf.PI*2/sides;verts.Add(new Vector3(centre+Mathf.Cos(a)*.026f*taper,y,Mathf.Sin(a)*.013f*taper)); }
        }
        for(int ring=0;ring<rings.Length-1;ring++) for(int s=0;s<sides;s++)
        {
            int a=ring*sides+s,b=ring*sides+(s+1)%sides,c=a+sides,d=b+sides;
            var triangles=ring==0 ? gold : black;triangles.AddRange(new[] {a,c,b,b,c,d});
        }
        int end=verts.Count;verts.Add(verts.Skip(verts.Count-sides).Aggregate(Vector3.zero,(sum,p)=>sum+p)/sides);
        for(int s=0;s<sides;s++) black.AddRange(new[] {end,(rings.Length-1)*sides+s,(rings.Length-1)*sides+(s+1)%sides});
        // Mouth rim has a real opening; the blade is occluded by the opaque saya.
        int inner=verts.Count;for(int s=0;s<sides;s++) { Vector3 p=verts[s];verts.Add(new Vector3(p.x*.82f,.002f,p.z*.65f)); }
        for(int s=0;s<sides;s++) gold.AddRange(new[] {s,(s+1)%sides,inner+s,inner+s,(s+1)%sides,inner+(s+1)%sides});
        var mesh=new Mesh {name="Lightweight movable saya"};mesh.SetVertices(verts);mesh.subMeshCount=2;
        mesh.SetTriangles(black,0);mesh.SetTriangles(gold,1);mesh.RecalculateNormals();mesh.RecalculateBounds();return SaveMesh(mesh,"MovableSaya.asset");
    }
    private static Mesh SaveMesh(Mesh mesh,string name)
    {
        var existing=AssetDatabase.LoadAssetAtPath<Mesh>(Folder+name);
        if(existing==null) AssetDatabase.CreateAsset(mesh,Folder+name);
        else { EditorUtility.CopySerialized(mesh,existing);Object.DestroyImmediate(mesh);mesh=existing; }
        return mesh;
    }
    private static Mesh RemoveFixedHilt(GameObject actor,SkinnedMeshRenderer skin,StreamWriter report)
    {
        var baked=new Mesh();skin.BakeMesh(baked);
        Vector3[] points=baked.vertices.Select(p=>actor.transform.InverseTransformPoint(skin.transform.position+skin.transform.rotation*p)).ToArray();
        string texturePath=AssetDatabase.GetAssetPath(skin.sharedMaterial.GetTexture("_BaseMap"));
        var importer=(TextureImporter)AssetImporter.GetAtPath(texturePath);bool readable=importer.isReadable;
        importer.isReadable=true;importer.SaveAndReimport();
        var texture=AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);Vector2[] uv=skin.sharedMesh.uv;
        var red=new List<Vector3>();var whiteTip=new List<Vector3>();var redVertices=new HashSet<int>();var goldVertices=new HashSet<int>();
        for(int v=0;v<points.Length;v++)
        {
            Vector3 p=points[v];
            if(p.x<-.10f && p.y>.33f && p.y<.56f && p.z>-.01f)
            { Color tip=texture.GetPixelBilinear(uv[v].x,uv[v].y);if(Mathf.Min(tip.r,Mathf.Min(tip.g,tip.b))>.3f) whiteTip.Add(p); }
            if(p.x<-.23f || p.x>.12f || p.y<.95f || p.y>1.25f || p.z<.07f) continue;
            Color c=texture.GetPixelBilinear(uv[v].x,uv[v].y);
            if(c.r>.08f && c.r>c.g*1.8f && c.r>c.b*1.4f) { red.Add(p);redVertices.Add(v); }
            if(p.y<1.025f && c.r>.12f && c.g>.08f && c.r>c.g*1.12f && c.g>c.b*1.4f) goldVertices.Add(v);
        }
        importer.isReadable=readable;importer.SaveAndReimport();
        if(red.Count<15) throw new InvalidOperationException("Could not identify the fixed red hilt without touching clothing.");
        red=red.OrderBy(p=>p.y).ToList();int sample=Mathf.Max(5,red.Count/5);
        Vector3 bottom=red.Take(sample).Aggregate(Vector3.zero,(a,b)=>a+b)/sample;
        Vector3 top=red.Skip(red.Count-sample).Aggregate(Vector3.zero,(a,b)=>a+b)/sample;
        BladeAxis=(bottom-top).normalized;
        Vector3 mouth=bottom+BladeAxis*.016f;Grip=mouth-BladeAxis*(.16f*.58f);
        var forefront=whiteTip.OrderByDescending(p=>p.z).Take(12).ToArray();
        Vector3 oldTip=forefront.Length>0 ? forefront.Aggregate(Vector3.zero,(sum,p)=>sum+p)/forefront.Length : mouth+BladeAxis*.65f;
        report.WriteLine("Old sheath tip candidates="+whiteTip.Count+"; front cap="+oldTip.ToString("F4"));
        bottom+=BladeAxis*.025f;top-=BladeAxis*.027f;
        report.WriteLine("Hilt red vertices="+red.Count+"; bottom="+bottom.ToString("F4")+"; top="+top.ToString("F4")+"; grip="+Grip.ToString("F4")+"; blade axis="+BladeAxis);
        Vector3 segment=top-bottom;
        float Distance(Vector3 p) => Vector3.Distance(p,bottom+segment*Mathf.Clamp01(Vector3.Dot(p-bottom,segment)/segment.sqrMagnitude));
        var source=skin.sharedMesh;var result=Object.Instantiate(source);result.name="TanjiroDrawableBody";
        int[] triangles=source.triangles;
        bool Keep(int t)
        {
            if(Enumerable.Range(0,3).Any(c=>redVertices.Contains(triangles[t*3+c]) || goldVertices.Contains(triangles[t*3+c]))) return false;
            Vector3 p=(points[triangles[t*3]]+points[triangles[t*3+1]]+points[triangles[t*3+2]])/3;
            Vector3 tube=oldTip-mouth;float along=Vector3.Dot(p-mouth,tube)/tube.sqrMagnitude;
            if(along>0 && along<1.08f && Vector3.Distance(p,mouth+tube*Mathf.Clamp01(along))<.032f) return false;
            return Distance(p)>.032f && Vector3.Distance(p,bottom)>.051f;
        }
        int[] kept=Enumerable.Range(0,triangles.Length/3).Where(Keep)
            .SelectMany(t=>new[] { triangles[t*3],triangles[t*3+1],triangles[t*3+2] }).ToArray();
        result.triangles=kept;report.WriteLine("Removed fixed hilt triangles: "+(triangles.Length-kept.Length)/3);
        CloseHiltCut(result,source,triangles,kept,report);
        Object.DestroyImmediate(baked);return SaveMesh(result,"TanjiroDrawableBody.asset");
    }
    private static void CloseHiltCut(Mesh mesh,Mesh original,int[] all,int[] kept,StreamWriter report)
    {
        // FBX duplicates vertices at UV/normal seams. Match geometric positions
        // before finding only the new boundaries introduced by removing the hilt.
        Vector3[] originalPoints=original.vertices;
        var welded=new Dictionary<Vector3Int,int>();var representative=new Dictionary<int,int>();int[] weld=new int[originalPoints.Length];
        for(int i=0;i<weld.Length;i++)
        {
            var key=Vector3Int.RoundToInt(originalPoints[i]*10000000);
            if(!welded.TryGetValue(key,out int id)) { id=welded.Count;welded.Add(key,id);representative[id]=i; } weld[i]=id;
        }
        var totals=new Dictionary<(int,int),int>();var retained=new Dictionary<(int,int),(int count,int a,int b)>();
        (int,int) Edge(int a,int b) => weld[a]<weld[b] ? (weld[a],weld[b]) : (weld[b],weld[a]);
        for(int i=0;i<all.Length;i+=3) for(int e=0;e<3;e++)
        { var edge=Edge(all[i+e],all[i+(e+1)%3]);totals.TryGetValue(edge,out int count);totals[edge]=count+1; }
        for(int i=0;i<kept.Length;i+=3) for(int e=0;e<3;e++)
        {
            int a=kept[i+e],b=kept[i+(e+1)%3];var edge=Edge(a,b);
            retained.TryGetValue(edge,out var value);retained[edge]=(value.count+1,a,b);
        }
        var edges=retained.Where(p=>p.Value.count==1 && totals[p.Key]==2).Select(p=>(p.Value.a,p.Value.b)).ToList();
        var verts=originalPoints.ToList();var normals=original.normals.ToList();var tangents=original.tangents.ToList();
        var uv=original.uv.ToList();var weights=original.boneWeights.ToList();var colours=original.colors.ToList();var triangles=kept.ToList();
        // A dark uniform texel, not the removed red grip/gold fittings, closes the
        // cut. The rest of the character retains its original UVs and material.
        Vector2 clothUV=original.uv[Enumerable.Range(0,original.vertexCount).OrderBy(v=>
            Vector3.Distance(originalPoints[v],originalPoints[representative[weld[edges[0].a]]])).First()];
        string albedoPath=Characters+"Tanjiro/Tanjiro_Albedo.png";var textureImporter=(TextureImporter)AssetImporter.GetAtPath(albedoPath);
        bool wasReadable=textureImporter.isReadable;textureImporter.isReadable=true;textureImporter.SaveAndReimport();
        var texture=AssetDatabase.LoadAssetAtPath<Texture2D>(albedoPath);
        float best=float.MaxValue;
        for(int i=0;i<originalPoints.Length;i++)
        {
            var colour=texture.GetPixelBilinear(uv[i].x,uv[i].y);if(colour.maxColorComponent>.16f || colour.g>colour.r*1.3f) continue;
            float distance=Vector3.Distance(originalPoints[i],originalPoints[edges[0].a]);
            if(distance<best) { best=distance;clothUV=uv[i]; }
        }
        textureImporter.isReadable=wasReadable;textureImporter.SaveAndReimport();
        int capTriangles=0;
        while(edges.Count>0)
        {
            var connected=new HashSet<int> { weld[edges[0].a],weld[edges[0].b] };bool changed;
            do
            {
                changed=false;foreach(var edge in edges)
                    if(connected.Contains(weld[edge.a]) || connected.Contains(weld[edge.b]))
                    { changed|=connected.Add(weld[edge.a]);changed|=connected.Add(weld[edge.b]); }
            } while(changed);
            var boundary=edges.Where(e=>connected.Contains(weld[e.a])).ToArray();edges.RemoveAll(e=>connected.Contains(weld[e.a]));
            int[] indices=connected.Select(v=>representative[v]).ToArray();
            Vector3 centre=indices.Aggregate(Vector3.zero,(sum,i)=>sum+originalPoints[i])/indices.Length;
            Vector3 normal=indices.Aggregate(Vector3.zero,(sum,i)=>sum+original.normals[i]).normalized;
            var influences=new Dictionary<int,float>();
            foreach(int i in indices)
            {
                var w=original.boneWeights[i];
                foreach(var pair in new[] { (w.boneIndex0,w.weight0),(w.boneIndex1,w.weight1),(w.boneIndex2,w.weight2),(w.boneIndex3,w.weight3) })
                { influences.TryGetValue(pair.Item1,out float value);influences[pair.Item1]=value+pair.Item2; }
            }
            var main=influences.OrderByDescending(p=>p.Value).Take(4).ToArray();float total=main.Sum(p=>p.Value);
            var weight=new BoneWeight();for(int j=0;j<main.Length;j++)
            {
                if(j==0) { weight.boneIndex0=main[j].Key;weight.weight0=main[j].Value/total; }
                if(j==1) { weight.boneIndex1=main[j].Key;weight.weight1=main[j].Value/total; }
                if(j==2) { weight.boneIndex2=main[j].Key;weight.weight2=main[j].Value/total; }
                if(j==3) { weight.boneIndex3=main[j].Key;weight.weight3=main[j].Value/total; }
            }
            int Add(Vector3 point,Vector3 n,Vector4 tangent,BoneWeight w)
            { int index=verts.Count;verts.Add(point);normals.Add(n);tangents.Add(tangent);uv.Add(clothUV);weights.Add(w);if(colours.Count>0) colours.Add(Color.clear);return index; }
            int centreIndex=Add(centre,normal,original.tangents[indices[0]],weight);
            foreach(var edge in boundary)
            {
                int a=Add(originalPoints[edge.a],normal,original.tangents[edge.a],original.boneWeights[edge.a]);
                int b=Add(originalPoints[edge.b],normal,original.tangents[edge.b],original.boneWeights[edge.b]);
                triangles.Add(b);triangles.Add(a);triangles.Add(centreIndex);capTriangles++;
            }
        }
        mesh.SetVertices(verts);mesh.SetNormals(normals);mesh.SetTangents(tangents);mesh.SetUVs(0,uv);mesh.boneWeights=weights.ToArray();
        if(colours.Count>0) mesh.SetColors(colours);mesh.SetTriangles(triangles,0);mesh.RecalculateBounds();
        report.WriteLine("Closed fixed-hilt boundary with "+capTriangles+" skinned uniform triangles");
    }
    private static void Validate(Action<bool,string> check,StreamWriter report)
    {
        var actor=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Characters+"TanjiroThirdPerson.prefab"));
        var sword=actor.GetComponentInChildren<TanjiroSword>();sword.Initialize();var animator=sword.GetComponent<Animator>();
        int layer=animator.GetLayerIndex("Sword Actions");var start=actor.transform.position;
        animator.Rebind();animator.Play("Idle Walk Run Blend",0,0);animator.Update(0);sword.ResetSword();
        Vector3 idleTorso=animator.GetBoneTransform(HumanBodyBones.Chest).position-animator.GetBoneTransform(HumanBodyBones.Hips).position;
        check(!sword.IsDrawn && sword.sword.parent==sword.sheathSocket,"Starts sheathed, with a single mounted sword");
        Capture(actor,"stowed");
        var drawClip=AssetDatabase.LoadAssetAtPath<AnimationClip>(Folder+"DrawKatana.anim");
        check(Mathf.Abs(drawClip.length-3)<.0001f && drawClip.frameRate==24,"Draw is exactly 3 seconds at 24 FPS");
        foreach(var clip in new[] { drawClip,AssetDatabase.LoadAssetAtPath<AnimationClip>(Folder+"SheatheKatana.anim") })
            check(AnimationUtility.GetCurveBindings(clip).Where(b=>b.type==typeof(Animator)).All(b=>AnimationUtility.GetEditorCurve(clip,b).keys.All(k=>k.value>=-1.001f && k.value<=1.001f)),clip.name+" stays inside Humanoid joint limits");
        // Editor-only, posed torso surface. Arms/hands are excluded by skin
        // weights so this checks body penetration, not the intended sword grip.
        var skin=sword.body;var torsoBones=new HashSet<Transform>(new[] {HumanBodyBones.Hips,HumanBodyBones.Spine,HumanBodyBones.Chest,HumanBodyBones.UpperChest}
            .Select(animator.GetBoneTransform).Where(t=>t!=null));
        var weights=skin.sharedMesh.boneWeights;var skinBones=skin.bones;
        float TorsoWeight(int v)
        {
            var w=weights[v];float sum=0;
            foreach(var pair in new[] {(w.boneIndex0,w.weight0),(w.boneIndex1,w.weight1),(w.boneIndex2,w.weight2),(w.boneIndex3,w.weight3)})
                if(torsoBones.Contains(skinBones[pair.Item1])) sum+=pair.Item2;
            return sum;
        }
        var sourceTriangles=skin.sharedMesh.triangles;
        var torsoTriangles=Enumerable.Range(0,sourceTriangles.Length/3).Where(t=>
            (TorsoWeight(sourceTriangles[t*3])+TorsoWeight(sourceTriangles[t*3+1])+TorsoWeight(sourceTriangles[t*3+2]))/3>.60f)
            .SelectMany(t=>new[] {sourceTriangles[t*3],sourceTriangles[t*3+1],sourceTriangles[t*3+2]}).ToArray();
        var torso=new GameObject("Editor torso clearance probe").AddComponent<MeshCollider>();var torsoMesh=new Mesh();
        int clearanceHits=0;float minimumClearance=float.PositiveInfinity;string closestProbe="";
        void MeasureTorsoClearance(string sample)
        {
            skin.BakeMesh(torsoMesh);torsoMesh.triangles=torsoTriangles;torso.sharedMesh=null;torso.sharedMesh=torsoMesh;
            torso.transform.SetPositionAndRotation(skin.transform.position,skin.transform.rotation);Physics.SyncTransforms();
            var hand=animator.GetBoneTransform(HumanBodyBones.RightHand);
            var index=animator.GetBoneTransform(HumanBodyBones.RightIndexProximal);var little=animator.GetBoneTransform(HumanBodyBones.RightLittleProximal);
            var points=new List<(Vector3 point,float radius)> {(hand.position,.018f),(Vector3.Lerp(hand.position,(index.position+little.position)*.5f,.65f),.025f),
                (index.position,.012f),(little.position,.012f)};
            foreach(var bone in new[] {HumanBodyBones.RightThumbDistal,HumanBodyBones.RightIndexDistal,HumanBodyBones.RightMiddleDistal,HumanBodyBones.RightLittleDistal})
                points.Add((animator.GetBoneTransform(bone).position,.009f));
            foreach(var point in points)
            {
                var local=actor.transform.InverseTransformPoint(point.point);
                var ray=new Ray(actor.transform.TransformPoint(new Vector3(local.x,local.y,1)), -actor.transform.forward);
                if(!torso.Raycast(ray,out var hit,2)) continue;
                clearanceHits++;float gap=Vector3.Dot(point.point-hit.point,actor.transform.forward)-point.radius;
                if(gap<minimumClearance) { minimumClearance=gap;closestProbe=sample+" hand="+local.ToString("F4")+" torso="+actor.transform.InverseTransformPoint(hit.point).ToString("F4"); }
            }
        }
        Directory.CreateDirectory(Path.GetFullPath("../sword-reference-preview"));
        sword.ToggleSword();sword.ToggleSword();check(sword.IsBusy,"Repeated requests cannot interrupt a draw/sheathing transfer");
        animator.SetLayerWeight(layer,1);animator.Play("Sword Actions.Draw",layer,0);animator.Update(0);
        var bones=new[] { HumanBodyBones.RightUpperArm,HumanBodyBones.RightLowerArm,HumanBodyBones.RightHand,HumanBodyBones.LeftUpperArm,HumanBodyBones.LeftLowerArm,HumanBodyBones.LeftHand };
        Quaternion[] previous=bones.Select(b=>animator.GetBoneTransform(b).localRotation).ToArray();float worstStep=0,maxBladeSheathAngle=0,maxBladeOffset=0,worstSubstep=0,maxEarlyWristBend=0;
        for(int frame=0;frame<72;frame++)
        {
            if(frame>0) for(int substep=0;substep<4;substep++)
            {
                var before=bones.Select(b=>animator.GetBoneTransform(b).localRotation).ToArray();animator.Update(1f/96);
                for(int b=0;b<bones.Length;b++) worstSubstep=Mathf.Max(worstSubstep,Quaternion.Angle(before[b],animator.GetBoneTransform(bones[b]).localRotation));
                if(frame>=2 && frame<=17) MeasureTorsoClearance("draw "+frame+"."+substep);
            }
            sword.ApplySheathPose();
            if(frame>=15 && frame<=32)
            {
                var hand=animator.GetBoneTransform(HumanBodyBones.RightHand);var elbow=animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
                Vector3 knuckles=(animator.GetBoneTransform(HumanBodyBones.RightIndexProximal).position+animator.GetBoneTransform(HumanBodyBones.RightLittleProximal).position)*.5f;
                maxEarlyWristBend=Mathf.Max(maxEarlyWristBend,Vector3.Angle(knuckles-hand.position,hand.position-elbow.position));
                if(frame==15 || frame==24) Capture(actor,"elbow-draw-"+frame,true);
                if(frame==15) Capture(actor,"reach-profile-15",true,true);
            }
            if(frame==5 || frame==8 || frame==11)
            { Capture(actor,"reach-clearance-"+frame,true);Capture(actor,"reach-profile-"+frame,true,true); }
            for(int b=0;b<bones.Length;b++)
            {
                var rotation=animator.GetBoneTransform(bones[b]).localRotation;
                float step=Quaternion.Angle(previous[b],rotation);worstStep=Mathf.Max(worstStep,step);previous[b]=rotation;
                if(step>18) report.WriteLine($"Joint frame {frame} {bones[b]} step={step:F3}");
            }
            Capture(actor,"reference-preview/"+frame.ToString("D4"));
            if(frame>=17 && frame<=59)
            {
                maxBladeSheathAngle=Mathf.Max(maxBladeSheathAngle,Vector3.Angle(sword.sword.up,sword.sheathSocket.up));
                maxBladeOffset=Mathf.Max(maxBladeOffset,Vector3.ProjectOnPlane(sword.sword.position-sword.sheathSocket.position,sword.sheathSocket.up).magnitude);
            }
            if(frame==12 || frame==36 || frame==52 || frame==71) Capture(actor,"draw-"+frame);
        }
        animator.Update(2f/24);sword.ApplySheathPose();
        check(sword.IKCalls>0,"Native hand IK is evaluated during the animation");
        report.WriteLine("Hand bone / IK goal rotation offset="+sword.GoalSpaceOffsetDegrees.ToString("F2")+"; blade/saya angular mismatch="+maxBladeSheathAngle.ToString("F2"));
        report.WriteLine("Draw torso probes="+clearanceHits+"; minimum clearance="+minimumClearance.ToString("F4")+"m; "+closestProbe);
        check(maxBladeSheathAngle<8,"Blade tracks the sheath axis throughout extraction");
        check(maxBladeOffset<.02f,"Blade stays on the physical sheath line; maximum lateral offset="+maxBladeOffset.ToString("F4")+"m");
        check(maxEarlyWristBend<60,"Right wrist bend is reduced through grip and early extraction (previously 79 degrees); maximum bend="+maxEarlyWristBend.ToString("F2")+" degrees");
        check(worstStep<20,"All 72 draw frames avoid arm/wrist flips; worst per-frame joint step="+worstStep.ToString("F2")+" degrees");
        check(worstSubstep<10,"In-between animation checked at 96 FPS; worst joint step="+worstSubstep.ToString("F2")+" degrees");
        check(sword.IsDrawn && !sword.IsBusy && sword.sword.parent==sword.handSocket,"Draw completes and transfers the katana to the right hand");
        check(Vector3.Distance(sword.sword.position,sword.handSocket.position)<.001f,"Grip remains attached to the hand socket");
        Capture(actor,"ready");
        Capture(actor,"ready-profile",false,true);
        Capture(actor,"grip-ready",true);
        Vector3 readyAxis=actor.transform.InverseTransformDirection(sword.sword.up);
        check(Mathf.Abs(readyAxis.x)<.015f && readyAxis.y>.5f && readyAxis.z>.3f,"Ready blade aims forward on the centre line, without a sideways cant");
        Vector3 readyTorso=animator.GetBoneTransform(HumanBodyBones.Chest).position-animator.GetBoneTransform(HumanBodyBones.Hips).position;
        check(Vector3.Angle(idleTorso,readyTorso)<3,"Sword holding preserves the upright locomotion torso instead of leaning backward");
        foreach(bool left in new[] {false,true})
        {
            HumanBodyBones Bone(string part) => (HumanBodyBones)Enum.Parse(typeof(HumanBodyBones),(left ? "Left" : "Right")+part);
            var hand=animator.GetBoneTransform(Bone("Hand"));var forearm=animator.GetBoneTransform(Bone("LowerArm"));
            Vector3 index=animator.GetBoneTransform(Bone("IndexProximal")).position,little=animator.GetBoneTransform(Bone("LittleProximal")).position;
            Vector3 palm=Vector3.Lerp(hand.position,(index+little)*.5f,.65f);
            Vector3 onHandle=sword.sword.InverseTransformPoint(palm);
            float bend=Vector3.Angle((index+little)*.5f-hand.position,hand.position-forearm.position);
            float gripAxis=Vector3.Angle(index-little,sword.sword.up);
            report.WriteLine($"{(left ? "Left" : "Right")} ready grip in sword space={onHandle.ToString("F4")}; wrist bend={bend:F2}; grip axis={gripAxis:F2}");
            report.WriteLine("Ready forearm direction="+actor.transform.InverseTransformDirection((hand.position-forearm.position).normalized).ToString("F4")+
                "; hand direction="+actor.transform.InverseTransformDirection(((index+little)*.5f-hand.position).normalized).ToString("F4"));
            float expected=left ? TanjiroSwordPoseBake.LeftGripY : TanjiroSwordPoseBake.RightGripY;
            check(Mathf.Abs(onHandle.y-expected)<.02f && new Vector2(onHandle.x,onHandle.z).magnitude<.02f,
                (left ? "Left hand stays below the right hand on the lower handle" : "Right hand grips immediately behind the guard"));
            check(Mathf.Abs(gripAxis-(left ? 0 : TanjiroSwordPoseBake.RightGripTilt))<5,
                (left ? "Left grip follows the lower handle" : "Right hand maintains its diagonal palm grip beside the guard"));
            check(bend<(left ? 40 : 28),(left ? "Left" : "Right")+" ready wrist avoids excessive bend");
            if(!left)
            {
                Vector3 elbow=actor.transform.InverseTransformPoint(forearm.position),wrist=actor.transform.InverseTransformPoint(hand.position);
                report.WriteLine("Lowered right elbow="+elbow.ToString("F4")+"; wrist="+wrist.ToString("F4"));
                check(elbow.y<=wrist.y+.03f,"Right elbow is lowered to near wrist height in the holding pose");
            }
        }
        sword.ToggleSword();animator.Update(.13f);
        float maxInsertionAngle=0,maxInsertionOffset=0;
        for(int frame=0;frame<80;frame++)
        {
            for(int sub=0;sub<4;sub++)
            { animator.Update(1f/96);if(frame>=54 && frame<=68) MeasureTorsoClearance("return "+frame+"."+sub); }
            sword.ApplySheathPose();
            var state=animator.GetCurrentAnimatorStateInfo(layer);
            if(state.shortNameHash==Animator.StringToHash("Sheathe") && state.normalizedTime>=.18f && state.normalizedTime<=.80f)
            {
                maxInsertionAngle=Mathf.Max(maxInsertionAngle,Vector3.Angle(sword.sword.up,sword.sheathSocket.up));
                maxInsertionOffset=Mathf.Max(maxInsertionOffset,Vector3.ProjectOnPlane(sword.sword.position-sword.sheathSocket.position,sword.sheathSocket.up).magnitude);
            }
            if(frame==20 || frame==48) Capture(actor,"sheathe-"+frame);
        }
        check(!sword.IsDrawn && !sword.IsBusy && sword.sword.parent==sword.sheathSocket,"Sheathing returns the blade to its original hip socket");
        check(maxInsertionAngle<8 && maxInsertionOffset<.02f,"Reinsertion stays aligned with the sheath: angle="+maxInsertionAngle.ToString("F2")+" degrees; offset="+maxInsertionOffset.ToString("F4")+"m");
        report.WriteLine("Torso clearance surface probes="+clearanceHits+"; minimum hand clearance="+minimumClearance.ToString("F4")+"m; "+closestProbe);
        check(clearanceHits>20 && minimumClearance>=-.002f,"The moving right hand clears the posed torso during reaching and return");
        Object.DestroyImmediate(torso.gameObject);Object.DestroyImmediate(torsoMesh);
        check(actor.transform.position==start && !animator.applyRootMotion,"Draw/sheathing does not teleport or move the player root");
        check(actor.GetComponent<StarterAssets.ThirdPersonController>()!=null && animator.parameters.Any(p=>p.name=="Speed"),"Existing third-person locomotion and parameters remain available");
        sword.ToggleSword();animator.Update(.5f);sword.enabled=false;
        // Ordinary MonoBehaviour disable callbacks do not execute in edit-mode batch tests.
        sword.SendMessage("OnDisable",SendMessageOptions.RequireReceiver);
        check(!sword.IsBusy && sword.sword.parent==sword.sheathSocket,"Disabling midway through an action resets the sword safely");
        check(sword.sword.GetComponent<MeshRenderer>().bounds.size.magnitude>.1f,"Mounted weapon has visible, metre-scale bounds, not centimetre-scale source geometry");
        report.WriteLine("Draw seconds="+DrawDuration+"; sheathe seconds="+SheatheDuration+"; upper-body layer="+layer);
        Object.DestroyImmediate(actor);
        var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);floor.transform.position=new Vector3(0,-.5f,0);floor.transform.localScale=new Vector3(120,1,120);Physics.SyncTransforms();
        actor=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Characters+"TanjiroThirdPerson.prefab"));
        sword=actor.GetComponentInChildren<TanjiroSword>();sword.Initialize();animator=sword.GetComponent<Animator>();
        var controller=actor.GetComponent<StarterAssets.ThirdPersonController>();var input=actor.GetComponent<StarterAssets.StarterAssetsInputs>();controller.Initialize();
        for(int f=0;f<30;f++) { controller.Simulate(1f/30);animator.Update(1f/30); }
        float initialZ=actor.transform.position.z;sword.ToggleSword();animator.SetLayerWeight(layer,1);input.MoveInput(Vector2.up);
        for(int f=0;f<110;f++) { controller.Simulate(1f/30);animator.Update(1f/30);sword.ApplySheathPose(); }
        check(actor.transform.position.z-initialZ>2 && animator.GetFloat("Speed")>1 && sword.IsDrawn,"Walking remains controllable throughout the draw animation");
        check(sword.enableSwordLocomotion,"The approved running pose is enabled on the gameplay prefab");
        sword.UpdateLocomotion(1);
        check(animator.GetFloat("SwordStride")>.9f,"Gameplay movement selects the approved running carry with the sword drawn");
        sword.enableSwordLocomotion=false;
        sword.UpdateLocomotion(1);
        check(animator.GetFloat("SwordStride")<.001f,"The optional sword-locomotion toggle still disables the carry");
        sword.enableSwordLocomotion=true;
        foreach(string name in new[]{"KatanaRun","KatanaSprint"})
        {
            var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>(Folder+name+".anim");
            check(clip!=null && clip.isLooping && clip.frameRate==24 && Mathf.Abs(clip.length-2f/3)<.001f,name+" is a seamless 24 FPS upper-body loop");
            check(AnimationUtility.GetCurveBindings(clip).All(b=>!b.propertyName.StartsWith("Root") && !b.propertyName.Contains("Leg") && !b.propertyName.Contains("Foot")),name+" leaves the root and existing leg gait untouched");
            check(AnimationUtility.GetCurveBindings(clip).All(b=>!b.propertyName.StartsWith("Left") && !b.propertyName.StartsWith("left")),name+" does not key the free arm or its IK targets");
        }
        void Tick(float dt)
        {controller.Simulate(dt);sword.UpdateLocomotion(dt);animator.Update(dt);sword.ApplySheathPose();}
        float leanRun=0,leanSprint=0,maxRunStep=0,maxGripError=0,maxWrist=0,maxForearmTwist=0,maxUpperTwist=0,maxElbowHeight=-10;
        Quaternion standingGripRotation=sword.handSocket.localRotation;Vector3 standingGripPosition=sword.handSocket.localPosition;
        using var runningPoseHandler=new HumanPoseHandler(animator.avatar,animator.transform);
        var runningPose=new HumanPose {muscles=new float[HumanTrait.MuscleCount]};
        var armBones=bones.Select(animator.GetBoneTransform).ToArray();
        foreach(int stage in new[]{0,1,2,3})
        {
            input.MoveInput(stage==3 ? Vector2.zero : Vector2.up);input.SprintInput(stage==2);
            Vector3 elbowMin=Vector3.one*10,elbowMax=-Vector3.one*10;
            for(int f=0;f<192;f++)
            {
                var before=armBones.Select(b=>b.localRotation).ToArray();Tick(1f/96);
                for(int b=0;b<armBones.Length;b++) maxRunStep=Mathf.Max(maxRunStep,Quaternion.Angle(before[b],armBones[b].localRotation));
                if(f<96 || stage==0 || stage==3) continue;
                runningPoseHandler.GetHumanPose(ref runningPose);
                foreach(bool left in new[]{false})
                {
                    HumanBodyBones Bone(string part)=>(HumanBodyBones)Enum.Parse(typeof(HumanBodyBones),(left ? "Left" : "Right")+part);
                    var hand=animator.GetBoneTransform(Bone("Hand"));var elbow=animator.GetBoneTransform(Bone("LowerArm"));
                    Vector3 knuckles=(animator.GetBoneTransform(Bone("IndexProximal")).position+animator.GetBoneTransform(Bone("LittleProximal")).position)*.5f;
                    Vector3 grip=sword.sword.InverseTransformPoint(Vector3.Lerp(hand.position,knuckles,.65f));
                    maxGripError=Mathf.Max(maxGripError,Vector3.Distance(grip,new Vector3(0,left ? TanjiroSwordPoseBake.LeftGripY : TanjiroSwordPoseBake.RightGripY,0))*sword.sword.lossyScale.x);
                    maxWrist=Mathf.Max(maxWrist,Vector3.Angle(knuckles-hand.position,hand.position-elbow.position));
                    string side=left ? "Left" : "Right";
                    maxForearmTwist=Mathf.Max(maxForearmTwist,Mathf.Abs(runningPose.muscles[Array.IndexOf(HumanTrait.MuscleName,side+" Forearm Twist In-Out")]));
                    maxUpperTwist=Mathf.Max(maxUpperTwist,Mathf.Abs(runningPose.muscles[Array.IndexOf(HumanTrait.MuscleName,side+" Arm Twist In-Out")]));
                    maxElbowHeight=Mathf.Max(maxElbowHeight,actor.transform.InverseTransformPoint(elbow.position).y-actor.transform.InverseTransformPoint(animator.GetBoneTransform(Bone("UpperArm")).position).y);
                    Vector3 up=(animator.GetBoneTransform(HumanBodyBones.Chest).position-animator.GetBoneTransform(HumanBodyBones.Hips).position).normalized;
                    Vector3 forward=Vector3.ProjectOnPlane(actor.transform.forward,up).normalized;
                    Vector3 limb=elbow.position-animator.GetBoneTransform(Bone("UpperArm")).position;
                    Vector3 upper=new Vector3(Vector3.Dot(limb,actor.transform.right),Vector3.Dot(limb,up),Vector3.Dot(limb,forward));
                    elbowMin=Vector3.Min(elbowMin,upper);elbowMax=Vector3.Max(elbowMax,upper);
                }
            }
            if(stage==1 || stage==2)
            {
                float stride=animator.GetFloat("SwordStride");
                check(Mathf.Abs(stride-stage)<.02f,(stage==1 ? "Run" : "Sprint")+" pose follows the motor's actual speed");
                Vector3 blade=actor.transform.InverseTransformDirection(sword.sword.up);
                check(blade.z>.9f && blade.y<-.1f,"One-handed blade points forward and slightly down while "+(stage==1 ? "running" : "sprinting"));
                Vector3 gripPosition=actor.transform.InverseTransformPoint(sword.handSocket.position);
                check(gripPosition.x>.25f && gripPosition.y<1.15f,"The sword hand stays back beside the right waist, as in the reference video");
                Vector3 travel=elbowMax-elbowMin;report.WriteLine("Torso-space elbow min="+elbowMin.ToString("F4")+" max="+elbowMax.ToString("F4")+" travel="+travel.ToString("F4"));
                check(travel.z>.06f,"The sword-side elbow swings forward during "+(stage==1 ? "running" : "sprinting"));
                check(elbowMin.z>=-.205f && elbowMax.y<=-.035f,"The lowered checked elbow pose is the rear limit, not the centre of a backward swing");
                Vector3 torsoDirection=actor.transform.InverseTransformDirection(animator.GetBoneTransform(HumanBodyBones.Chest).position-animator.GetBoneTransform(HumanBodyBones.Hips).position);
                float lean=Mathf.Atan2(torsoDirection.z,torsoDirection.y)*Mathf.Rad2Deg;
                if(stage==1) leanRun=lean;else leanSprint=lean;
                Capture(actor,stage==1 ? "run-profile" : "sprint-profile",false,true);
            }
        }
        report.WriteLine($"Sword locomotion: forward lean run={leanRun:F2}, sprint={leanSprint:F2}; maximum 96 FPS arm step={maxRunStep:F2}; grip error={maxGripError:F4}m; wrist bend={maxWrist:F2}");
        check(leanRun>7 && leanSprint>leanRun+7,"Run leans forward; sprint has a visibly stronger forward lean");
        check(maxRunStep<15 && maxGripError<.02f && maxWrist<60,"Run/sprint/stop avoid arm flips, hand detachment and excessive wrist bend");
        report.WriteLine($"Running arm limits: forearm twist={maxForearmTwist:F3}; upper-arm twist={maxUpperTwist:F3}; elbow vs shoulder={maxElbowHeight:F3}m");
        check(maxForearmTwist<.65f && maxUpperTwist<.7f && maxElbowHeight<.10f,"The sword arm keeps bounded forearm rotation without an elbow/shoulder shrug");
        check(animator.GetFloat("SwordStride")<.01f && actor.transform.InverseTransformDirection(sword.sword.up).z>.3f,"Stopping returns smoothly to the upright forward guard");
        check(Quaternion.Angle(standingGripRotation,sword.handSocket.localRotation)<.1f && Vector3.Distance(standingGripPosition,sword.handSocket.localPosition)<.001f,"One-handed carry preserves the original draw/guard hand socket");
        input.MoveInput(Vector2.up);input.SprintInput(true);for(int f=0;f<96;f++) Tick(1f/96);
        sword.ToggleSword();check(sword.IsBusy && sword.IsDrawn,"Sheathing while sprinting first brings the trailing sword back to guard");
        input.MoveInput(Vector2.zero);input.SprintInput(false);for(int f=0;f<384;f++) Tick(1f/96);
        check(!sword.IsDrawn && !sword.IsBusy && sword.sword.parent==sword.sheathSocket,"The sprint-to-sheathe request finishes safely");
        sword.ToggleSword();for(int f=0;f<320;f++) Tick(1f/96);
        input.MoveInput(Vector2.zero);input.JumpInput(true);float initialY=actor.transform.position.y;
        for(int f=0;f<10;f++) { controller.Simulate(1f/30);animator.Update(1f/30); }
        check(actor.transform.position.y>initialY+.2f && sword.sword.parent==sword.handSocket,"Normal jump remains functional while holding the katana");
        actor.GetComponent<InfinityGravityBody>().SetGravity(Vector3.left,22,true,8);controller.Simulate(1f/30);animator.Update(1f/30);
        check(Vector3.Dot(actor.transform.up,Vector3.right)>.99f && sword.sword.parent==sword.handSocket,"Sideways-gravity alignment preserves the hand attachment");
        Object.DestroyImmediate(actor);Object.DestroyImmediate(floor);
        var scene=EditorSceneManager.OpenScene("Assets/StarterAssets/ThirdPersonController/Scenes/Playground.unity");
        var scenePlayer=Object.FindFirstObjectByType<StarterAssets.ThirdPersonController>();
        var sceneSword=scenePlayer.GetComponentInChildren<TanjiroSword>();
        check(sceneSword!=null,"Existing Playground Tanjiro inherits sword actions without replacing the scene/player");
        sceneSword.Initialize();check(sceneSword.GetComponent<Animator>().runtimeAnimatorController==sceneSword.actionController,"Scene locomotion override is upgraded to the same locomotion plus sword layer at runtime");
    }
    private static void Capture(GameObject actor,string name,bool close=false,bool profile=false)
    {
        RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=Color.white*.7f;
        var light=new GameObject("Sword preview light").AddComponent<Light>();light.type=LightType.Directional;light.intensity=1.2f;light.transform.rotation=Quaternion.Euler(35,-35,0);
        var proxies=new List<GameObject>();var skins=actor.GetComponentsInChildren<SkinnedMeshRenderer>();
        foreach(var skin in skins)
        {
            var mesh=new Mesh();skin.BakeMesh(mesh);var proxy=new GameObject("Baked preview",typeof(MeshFilter),typeof(MeshRenderer));
            proxy.transform.SetPositionAndRotation(skin.transform.position,skin.transform.rotation);proxy.GetComponent<MeshFilter>().sharedMesh=mesh;
            proxy.GetComponent<MeshRenderer>().sharedMaterials=skin.sharedMaterials;skin.enabled=false;proxies.Add(proxy);
        }
        var camera=new GameObject("Sword preview camera").AddComponent<Camera>();Vector3 centre=actor.transform.position+new Vector3(0,close ? 1.06f : .9f,.03f);
        camera.transform.position=centre+(profile ? new Vector3(3.5f,0,0) : new Vector3(1,0,3.5f));camera.transform.LookAt(centre);camera.orthographic=true;camera.orthographicSize=close ? .32f : 1.05f;
        camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.08f,.09f,.11f);
        var target=RenderTexture.GetTemporary(800,900,24);camera.targetTexture=target;camera.Render();camera.Render();RenderTexture.active=target;
        var image=new Texture2D(800,900,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,800,900),0,0);image.Apply();
        File.WriteAllBytes(Path.GetFullPath("../sword-"+name+".png"),image.EncodeToPNG());
        camera.targetTexture=null;RenderTexture.active=null;RenderTexture.ReleaseTemporary(target);Object.DestroyImmediate(image);
        foreach(var proxy in proxies) { Object.DestroyImmediate(proxy.GetComponent<MeshFilter>().sharedMesh);Object.DestroyImmediate(proxy); }
        foreach(var skin in skins) skin.enabled=true;
        Object.DestroyImmediate(camera.gameObject);Object.DestroyImmediate(light.gameObject);
    }
}
