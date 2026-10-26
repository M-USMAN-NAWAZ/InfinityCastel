using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

// Temporary additive scene: never saves or replaces the gameplay scene.
public static class TanjiroSwordReview
{
    private const string Folder="Assets/Characters/SunBreathers/SwordActions/";

    public static string Run(bool rebuild=false,bool sequence=false,bool locomotion=false)
    {
        string output=Path.GetFullPath(locomotion ? "Temp/sword-run" : "Temp/sword-pro");Directory.CreateDirectory(output);
        var original=SceneManager.GetActiveScene();
        var review=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
        SceneManager.SetActiveScene(review);
        try
        {
            var actor=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Characters/SunBreathers/TanjiroThirdPerson.prefab"));
            actor.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);
            var sword=actor.GetComponentInChildren<TanjiroSword>();var animator=sword.GetComponent<Animator>();
            animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
            using var report=new StreamWriter(output+"/review.txt") {AutoFlush=true};
            if(rebuild)
            {
                animator.runtimeAnimatorController=AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>("Assets/StarterAssets/ThirdPersonController/Character/Animations/StarterAssetsThirdPerson.controller");
                animator.Rebind();animator.Play("Idle Walk Run Blend",0,0);animator.Update(0);
                if(locomotion)
                {
                    var motion=TanjiroSwordPoseBake.BakeLocomotion(animator,Folder,report);
                    TanjiroSwordAnimationSetup.ConfigureLocomotion((UnityEditor.Animations.AnimatorController)sword.actionController,
                        AssetDatabase.LoadAssetAtPath<AnimationClip>(Folder+"KatanaReady.anim"),motion.run,motion.sprint);
                }
                else
                {
                    var motion=TanjiroSwordPoseBake.Bake(animator,new Vector3(-.085f,1.105f,.14f),new Vector3(-.25f,-.96f,-.12f).normalized,Folder,report);
                    AnimationUtility.SetAnimationEvents(motion.draw,new[]{new AnimationEvent {time=.60f,functionName="OnSwordDrawGrip"},new AnimationEvent {time=2.98f,functionName="OnSwordDrawFinished"}});
                    AnimationUtility.SetAnimationEvents(motion.sheathe,new[]{new AnimationEvent {time=2.40f,functionName="OnSwordSheatheGrip"},new AnimationEvent {time=2.98f,functionName="OnSwordSheatheFinished"}});
                }
                AssetDatabase.SaveAssets();
                // Recreate the preview Animator after editing clips/tree; an
                // already bound Animator can retain its old clip field curves.
                Object.DestroyImmediate(actor);
                actor=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Characters/SunBreathers/TanjiroThirdPerson.prefab"));
                actor.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);
                sword=actor.GetComponentInChildren<TanjiroSword>();animator=sword.GetComponent<Animator>();
            }
            sword.enableSwordLocomotion=locomotion;
            sword.Initialize();int layer=animator.GetLayerIndex("Sword Actions");
            animator.Rebind();sword.ResetSword();animator.Play("Idle Walk Run Blend",0,0);animator.Update(0);
            var idleChest=animator.GetBoneTransform(HumanBodyBones.Chest);
            Vector3 idleTorso=idleChest.position-animator.GetBoneTransform(HumanBodyBones.Hips).position;
            RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=Color.white*.7f;
            var light=new GameObject("Review light").AddComponent<Light>();light.type=LightType.Directional;light.intensity=1.2f;light.transform.rotation=Quaternion.Euler(35,145,0);
            var camera=new GameObject("Review camera").AddComponent<Camera>();camera.scene=review;camera.orthographic=true;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.08f,.09f,.11f);
            void Capture(string name,Vector3 view,bool close=false)
            {
                var centre=new Vector3(0,close ? 1.12f : .92f,.10f);
                camera.transform.position=centre+view*3;camera.transform.LookAt(centre);camera.orthographicSize=close ? .41f : 1.05f;
                var skins=actor.GetComponentsInChildren<SkinnedMeshRenderer>();
                var proxies=skins.Select(skin=>
                {
                    var mesh=new Mesh();skin.BakeMesh(mesh);var proxy=new GameObject("Review posed mesh",typeof(MeshFilter),typeof(MeshRenderer));
                    proxy.transform.SetPositionAndRotation(skin.transform.position,skin.transform.rotation);proxy.GetComponent<MeshFilter>().sharedMesh=mesh;
                    proxy.GetComponent<MeshRenderer>().sharedMaterials=skin.sharedMaterials;skin.enabled=false;return proxy;
                }).ToArray();
                var target=RenderTexture.GetTemporary(720,900,24);var previous=RenderTexture.active;
                try
                {
                    camera.targetTexture=target;camera.Render();camera.Render();RenderTexture.active=target;
                    var image=new Texture2D(720,900,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,720,900),0,0);image.Apply();
                    File.WriteAllBytes(output+"/"+name+".png",image.EncodeToPNG());Object.DestroyImmediate(image);
                }
                finally
                {
                    camera.targetTexture=null;RenderTexture.active=previous;RenderTexture.ReleaseTemporary(target);
                    foreach(var proxy in proxies) {Object.DestroyImmediate(proxy.GetComponent<MeshFilter>().sharedMesh);Object.DestroyImmediate(proxy);}
                    foreach(var skin in skins) skin.enabled=true;
                }
            }
            bool BladeIntersects(string name)
            {
                var mesh=new Mesh();sword.body.BakeMesh(mesh);
                var probe=new GameObject("Editor blade clearance").AddComponent<MeshCollider>();probe.sharedMesh=mesh;
                probe.transform.SetPositionAndRotation(sword.body.transform.position,sword.body.transform.rotation);Physics.SyncTransforms();
                Vector3 start=sword.sword.TransformPoint(new Vector3(0,.18f,0)),end=sword.sword.TransformPoint(new Vector3(0,1.19f,0));
                bool hit=probe.Raycast(new Ray(start,(end-start).normalized),out var contact,Vector3.Distance(start,end)) ||
                    probe.Raycast(new Ray(end,(start-end).normalized),out contact,Vector3.Distance(start,end));
                if(hit)
                {
                    int vertex=mesh.triangles[contact.triangleIndex*3];var weight=sword.body.sharedMesh.boneWeights[vertex];
                    report.WriteLine(name+" blade/character intersection at "+actor.transform.InverseTransformPoint(contact.point).ToString("F4")+" bone="+sword.body.bones[weight.boneIndex0].name);
                }
                Object.DestroyImmediate(probe.gameObject);Object.DestroyImmediate(mesh);
                return hit;
            }
            void Metrics(string name)
            {
                using var human=new HumanPoseHandler(animator.avatar,animator.transform);
                var pose=new HumanPose {muscles=new float[HumanTrait.MuscleCount]};human.GetHumanPose(ref pose);
                report.WriteLine("Arm muscles: "+string.Join("; ",Enumerable.Range(0,HumanTrait.MuscleCount).Where(m=>new[]{" Arm "," Forearm "," Shoulder "}.Any(part=>HumanTrait.MuscleName[m].Contains(part))).Select(m=>HumanTrait.MuscleName[m]+"="+pose.muscles[m].ToString("F3"))));
                report.WriteLine(name+" IK="+sword.IKCalls+" weight="+sword.handIKWeight.ToString("F2")+" blade="+actor.transform.InverseTransformDirection(sword.sword.up).ToString("F4")+
                    " torso change="+Vector3.Angle(idleTorso,idleChest.position-animator.GetBoneTransform(HumanBodyBones.Hips).position).ToString("F2"));
                foreach(bool left in new[]{false,true})
                {
                    HumanBodyBones Bone(string part)=>(HumanBodyBones)Enum.Parse(typeof(HumanBodyBones),(left ? "Left" : "Right")+part);
                    var hand=animator.GetBoneTransform(Bone("Hand"));var elbow=animator.GetBoneTransform(Bone("LowerArm"));
                    var index=animator.GetBoneTransform(Bone("IndexProximal"));var little=animator.GetBoneTransform(Bone("LittleProximal"));
                    float bend=Vector3.Angle((index.position+little.position)*.5f-hand.position,hand.position-elbow.position);
                    report.WriteLine((left ? "Left" : "Right")+" wrist="+bend.ToString("F2")+" elbow="+actor.transform.InverseTransformPoint(elbow.position).ToString("F4"));
                    report.WriteLine("hand="+actor.transform.InverseTransformPoint(hand.position).ToString("F4")+" fingers="+actor.transform.InverseTransformDirection(((index.position+little.position)*.5f-hand.position).normalized).ToString("F4")+
                        " shoulder="+actor.transform.InverseTransformPoint(animator.GetBoneTransform(Bone("UpperArm")).position).ToString("F4")+" grip="+sword.sword.InverseTransformPoint(Vector3.Lerp(hand.position,(index.position+little.position)*.5f,.65f)).ToString("F4"));
                }
            }
            if(locomotion)
            {
                var motor=actor.GetComponent<StarterAssets.ThirdPersonController>();motor.Grounded=true;
                animator.SetBool("Grounded",true);animator.SetLayerWeight(layer,1);
                sword.OnSwordDrawGrip();sword.OnSwordDrawFinished();animator.Play("Sword Actions.Ready",layer,0);animator.Update(0);
                var bones=new[]{HumanBodyBones.RightUpperArm,HumanBodyBones.RightLowerArm,HumanBodyBones.RightHand,HumanBodyBones.LeftUpperArm,HumanBodyBones.LeftLowerArm,HumanBodyBones.LeftHand}.Select(animator.GetBoneTransform).ToArray();
                var baselineActor=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Characters/SunBreathers/TanjiroThirdPerson.prefab"));
                baselineActor.GetComponentInChildren<TanjiroSword>().enabled=false;
                foreach(var renderer in baselineActor.GetComponentsInChildren<Renderer>()) renderer.enabled=false;
                var baseline=baselineActor.GetComponentInChildren<Animator>();
                baseline.runtimeAnimatorController=AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>("Assets/StarterAssets/ThirdPersonController/Character/Animations/StarterAssetsThirdPerson.controller");
                baseline.Rebind();baseline.SetBool("Grounded",true);baseline.Play("Idle Walk Run Blend",0,0);baseline.Update(0);
                var freeIds=new[]{HumanBodyBones.LeftShoulder,HumanBodyBones.LeftUpperArm,HumanBodyBones.LeftLowerArm,HumanBodyBones.LeftHand,HumanBodyBones.LeftIndexProximal,HumanBodyBones.LeftThumbProximal};
                Quaternion initialSwing=animator.GetBoneTransform(HumanBodyBones.LeftUpperArm).localRotation;
                float worst=0,freeArmError=0,freeArmSwing=0;int bladeHits=0;
                var elbowMin=new[]{Vector3.one*10,Vector3.one*10};var elbowMax=new[]{-Vector3.one*10,-Vector3.one*10};
                for(int frame=0;frame<144;frame++)
                {
                    // Idle -> run -> sprint -> run -> idle, with actual base gait.
                    float speed=frame<12 || frame>=120 ? 0 : frame<48 || frame>=96 ? motor.MoveSpeed : motor.SprintSpeed;
                    animator.SetFloat("MotionSpeed",speed>0 ? 1 : 0);
                    for(int sub=0;sub<4;sub++)
                    {
                        var before=bones.Select(b=>b.localRotation).ToArray();animator.SetFloat("Speed",speed,.1f,1f/96);sword.UpdateLocomotion(1f/96);animator.Update(1f/96);sword.ApplySheathPose();
                        if(animator.GetFloat("SwordStride")>.999f)
                        {
                            baseline.SetFloat("Speed",animator.GetFloat("Speed"));baseline.SetFloat("MotionSpeed",speed>0 ? 1 : 0);
                            baseline.Play("Idle Walk Run Blend",0,animator.GetCurrentAnimatorStateInfo(0).normalizedTime);baseline.Update(0);
                            foreach(var id in freeIds) freeArmError=Mathf.Max(freeArmError,Quaternion.Angle(animator.GetBoneTransform(id).localRotation,baseline.GetBoneTransform(id).localRotation));
                            freeArmSwing=Mathf.Max(freeArmSwing,Quaternion.Angle(initialSwing,animator.GetBoneTransform(HumanBodyBones.LeftUpperArm).localRotation));
                            float stride=animator.GetFloat("SwordStride");int gait=stride<1.001f ? 0 : stride>1.999f ? 1 : -1;
                            if(gait>=0)
                            {
                                Vector3 up=(idleChest.position-animator.GetBoneTransform(HumanBodyBones.Hips).position).normalized;
                                Vector3 forward=Vector3.ProjectOnPlane(actor.transform.forward,up).normalized;
                                Vector3 limb=bones[1].position-bones[0].position;
                                Vector3 upper=new Vector3(Vector3.Dot(limb,actor.transform.right),Vector3.Dot(limb,up),Vector3.Dot(limb,forward));
                                elbowMin[gait]=Vector3.Min(elbowMin[gait],upper);elbowMax[gait]=Vector3.Max(elbowMax[gait],upper);
                            }
                        }
                        for(int b=0;b<bones.Length;b++)
                        {float step=Quaternion.Angle(before[b],bones[b].localRotation);if(frame==0) continue;if(step>15) report.WriteLine("Large step frame="+frame+" sub="+sub+" bone="+bones[b].name+" degrees="+step);worst=Mathf.Max(worst,step);}
                    }
                    if(animator.GetFloat("SwordStride")>.01f && BladeIntersects("Run "+frame)) bladeHits++;
                    if(sequence) Capture("Run-"+frame.ToString("D3"),new Vector3(1,0,.65f));
                    if(new[]{0,36,72,108,143}.Contains(frame))
                    {Metrics("Run "+frame);Capture("run-front-"+frame,Vector3.forward);Capture("run-side-"+frame,Vector3.right);Capture("run-back-"+frame,Vector3.back);}
                    if(frame==36 || frame==72) {Capture("run-arm-front-"+frame,Vector3.forward,true);Capture("run-arm-side-"+frame,Vector3.right,true);Capture("run-reference-"+frame,new Vector3(-.7f,0,1));}
                }
                report.WriteLine("Maximum run/stop/sprint arm step at 96 FPS="+worst.ToString("F2"));
                report.WriteLine("Run/sprint/transition blade intersection frames="+bladeHits);
                report.WriteLine("Free arm maximum difference from base gait="+freeArmError.ToString("F3")+" degrees; swing="+freeArmSwing.ToString("F2"));
                foreach(int gait in new[]{0,1})
                {
                    Vector3 travel=elbowMax[gait]-elbowMin[gait];
                    report.WriteLine((gait==0 ? "Run" : "Sprint")+" torso-space elbow min="+elbowMin[gait].ToString("F4")+" max="+elbowMax[gait].ToString("F4")+" travel="+travel.ToString("F4"));
                    if(travel.z<.06f) throw new InvalidOperationException("The sword-side elbow must swing forward, not just bob vertically.");
                    if(elbowMin[gait].z<-.205f || elbowMax[gait].y>-.035f) throw new InvalidOperationException("The sword-side elbow moved beyond the checked rear/lowered pose.");
                }
                if(worst>15) throw new InvalidOperationException("Sword locomotion has a discontinuous arm rotation: "+worst);
                if(bladeHits>0) throw new InvalidOperationException("Sword locomotion blade intersects the character in "+bladeHits+" sampled frames.");
                if(freeArmError>.25f || freeArmSwing<15) throw new InvalidOperationException("The free arm does not preserve the original run/sprint gait.");
                return output;
            }
            foreach(string action in new[]{"Draw","Sheathe"})
            {
                sword.ToggleSword();animator.SetLayerWeight(layer,1);animator.Play("Sword Actions."+action,layer,0);animator.Update(0);
                var armBones=new[]{HumanBodyBones.RightUpperArm,HumanBodyBones.RightLowerArm,HumanBodyBones.RightHand,HumanBodyBones.LeftUpperArm,HumanBodyBones.LeftLowerArm,HumanBodyBones.LeftHand}
                    .Select(animator.GetBoneTransform).ToArray();
                float worstStep=0;
                for(int frame=0;frame<=72;frame++)
                {
                    if(frame>0) for(int sub=0;sub<4;sub++)
                    {
                        var before=armBones.Select(b=>b.localRotation).ToArray();animator.Update(1f/96);
                        for(int bone=0;bone<armBones.Length;bone++) worstStep=Mathf.Max(worstStep,Quaternion.Angle(before[bone],armBones[bone].localRotation));
                    }
                    sword.ApplySheathPose();
                    // Preview scenes suppress gameplay AnimationEvents. Reproduce
                    // only their attachment/state effects at the authored times.
                    if(action=="Draw" && frame==15) sword.OnSwordDrawGrip();
                    if(action=="Sheathe" && frame==58) sword.OnSwordSheatheGrip();
                    if(frame==72)
                    {
                        if(action=="Draw") {sword.OnSwordDrawFinished();animator.Play("Sword Actions.Ready",layer,0);}
                        else {sword.OnSwordSheatheFinished();animator.Play("Sword Actions.Stowed",layer,0);}
                        animator.Update(0);sword.ApplySheathPose();
                    }
                    if(sequence && frame<72) Capture(action+"-"+frame.ToString("D3"),new Vector3(.28f,0,1));
                    if(new[]{8,15,32,54,64,72}.Contains(frame))
                    {Metrics(action+" "+frame);Capture(action+"-front-"+frame,Vector3.forward);Capture(action+"-side-"+frame,Vector3.right);}
                }
                report.WriteLine(action+" maximum 96 FPS joint step="+worstStep.ToString("F2"));
                if(worstStep>=10) throw new InvalidOperationException(action+" has a discontinuous arm rotation: "+worstStep);
                if(action=="Draw")
                {
                    if(Mathf.Abs(sword.sword.up.x)>.015f) throw new InvalidOperationException("Holding pose has a sideways blade cant.");
                    if(Vector3.Angle(idleTorso,idleChest.position-animator.GetBoneTransform(HumanBodyBones.Hips).position)>3)
                        throw new InvalidOperationException("Holding pose changed the upright torso.");
                    Capture("ready-hands",new Vector3(.2f,0,1),true);
                }
            }
            return output;
        }
        finally {SceneManager.SetActiveScene(original);EditorSceneManager.CloseScene(review,true);}
    }
}
