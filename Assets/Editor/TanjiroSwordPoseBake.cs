using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

// Editor-only constrained pose authoring. The game plays ordinary baked clips.
public static class TanjiroSwordPoseBake
{
    public const int FPS=24, Frames=72;
    public const float Duration=3;
    public const float SwordScale=.58f, GuardY=.16f, RightGripY=.10f, LeftGripY=-.04f;
    public const float RightGripTilt=28;
    public static Quaternion BladeRotation(Vector3 axis) => Quaternion.LookRotation(Vector3.ProjectOnPlane(Vector3.forward,axis).normalized,axis);
    private static Vector3 ElbowHint(bool left,float clear) => left ?
        Vector3.Lerp(new Vector3(-.36f,1.055f,.10f),new Vector3(-.38f,.86f,.20f),clear) :
        Vector3.Lerp(new Vector3(.55f,1.10f,.25f),new Vector3(.30f,.60f,.35f),clear);
    private static readonly float[] Times={0,.20f,.28f,.36f,.44f,.52f,.60f,.68f,.76f,.82f,.88f,.94f,1};
    private static readonly float[] Extraction={0,0,.018f,.05f,.095f,.15f,.23f,.32f,.49f,.51f,.51f,.51f,.51f};

    private static (Vector3 point,Quaternion rotation) GripBasis(Animator animator,bool left)
    {
        Transform Bone(string part) => animator.GetBoneTransform((HumanBodyBones)Enum.Parse(typeof(HumanBodyBones),(left ? "Left" : "Right")+part));
        var hand=Bone("Hand");Vector3 index=Bone("IndexProximal").position,little=Bone("LittleProximal").position;
        Vector3 point=hand.InverseTransformPoint(Vector3.Lerp(hand.position,(index+little)*.5f,.65f));
        Vector3 across=hand.InverseTransformDirection(index-little).normalized;
        Vector3 forward=hand.InverseTransformDirection((index+little)*.5f-hand.position).normalized;
        // Mirrored palm frames; preserve the established diagonal dominant grip.
        Vector3 back=Vector3.Cross(forward,across)*(left ? 1 : -1);
        Quaternion frame=Quaternion.LookRotation(back,across);
        if(!left) frame*=Quaternion.AngleAxis(RightGripTilt,Vector3.forward);
        return (point,frame);
    }

    public static (AnimationClip draw,AnimationClip sheathe,AnimationClip hold,Vector3 gripLocal,Quaternion gripRotation)
        Bake(Animator animator,Vector3 stowedGrip,Vector3 stowedAxis,string folder,StreamWriter report)
    {
        var root=animator.transform;var hip=animator.GetBoneTransform(HumanBodyBones.Hips);
        using var handler=new HumanPoseHandler(animator.avatar,root);
        var idle=new HumanPose { muscles=new float[HumanTrait.MuscleCount] };handler.GetHumanPose(ref idle);
        for(int m=0;m<idle.muscles.Length;m++) idle.muscles[m]=Mathf.Clamp(idle.muscles[m],-.85f,.85f);
        handler.SetHumanPose(ref idle);
        Transform Bone(bool left,string part) => animator.GetBoneTransform((HumanBodyBones)Enum.Parse(typeof(HumanBodyBones),(left ? "Left" : "Right")+part));
        var rightGrip=GripBasis(animator,false);var leftGrip=GripBasis(animator,true);
        Vector3 Palm(bool left)
        { var hand=Bone(left,"Hand");return hand.TransformPoint(left ? leftGrip.point : rightGrip.point); }
        Vector3 initialRight=root.InverseTransformPoint(Palm(false)),initialLeft=root.InverseTransformPoint(Palm(true));
        Vector3 initialRightElbow=root.InverseTransformPoint(Bone(false,"LowerArm").position),initialLeftElbow=root.InverseTransformPoint(Bone(true,"LowerArm").position);
        Quaternion initialRightRotation=Bone(false,"Hand").rotation*rightGrip.rotation;
        Quaternion initialLeftRotation=Bone(true,"Hand").rotation*leftGrip.rotation;
        Vector3 idleMouth=stowedGrip+stowedAxis*(GuardY*SwordScale);
        var poses=new HumanPose[Frames+1];var sheathPositions=new Vector3[Frames+1];var sheathRotations=new Quaternion[Frames+1];
        var rightWrists=new Vector3[Frames+1];var leftWrists=new Vector3[Frames+1];var rightRotations=new Quaternion[Frames+1];var leftRotations=new Quaternion[Frames+1];
        var rightPoles=new Vector3[Frames+1];var leftPoles=new Vector3[Frames+1];var ikWeights=new float[Frames+1];
        float maxRightError=0,maxLeftError=0,maxRightAngle=0;
        var extraction=new AnimationCurve(Times.Select((time,index)=>new Keyframe(time,Extraction[index])).ToArray());
        for(int key=0;key<extraction.length;key++)
        {
            AnimationUtility.SetKeyLeftTangentMode(extraction,key,AnimationUtility.TangentMode.ClampedAuto);
            AnimationUtility.SetKeyRightTangentMode(extraction,key,AnimationUtility.TangentMode.ClampedAuto);
        }
        for(int sample=0;sample<=Frames;sample++)
        {
            float phase=sample/(float)Frames;
            float engage=Mathf.SmoothStep(0,1,Mathf.InverseLerp(0,.20f,phase));
            float clear=Mathf.SmoothStep(0,1,Mathf.InverseLerp(.82f,.98f,phase));
            float handover=Mathf.SmoothStep(0,1,Mathf.InverseLerp(.82f,1,phase));
            Vector3 Pole(bool left) => Vector3.Lerp(left ? initialLeftElbow : initialRightElbow,ElbowHint(left,clear),engage);
            // Keep the established draw path; open the elbow before extraction,
            // then bring it forward into the two-handed guard.
            Vector3 drawAxis=new Vector3(-1,-.025f,.06f).normalized;
            Vector3 mouth=Vector3.Lerp(idleMouth,new Vector3(-.08f,1.025f,.235f),engage);
            mouth+=Vector3.left*Mathf.Lerp(0,.14f,Mathf.InverseLerp(.20f,.76f,phase));
            mouth=Vector3.Lerp(mouth,idleMouth,clear);
            Vector3 axis=Vector3.Slerp(stowedAxis,drawAxis,engage).normalized;
            axis=Vector3.Slerp(axis,stowedAxis,clear).normalized;
            Vector3 rightTarget=mouth-drawAxis*((GuardY-RightGripY)*SwordScale+extraction.Evaluate(phase));
            rightTarget=Vector3.Lerp(initialRight,rightTarget,engage);
            // Lift away from the hip first, then cross in front of the jacket.
            // The same arc is reversed during sheathing.
            if(phase<.20f) rightTarget+=new Vector3(.03f,.065f,.16f)*Mathf.Sin(Mathf.PI*engage);
            rightTarget=Vector3.Lerp(rightTarget,new Vector3(.00f,1.10f,.30f),clear);
            Vector3 uprightAxis=new Vector3(0,.82f,.57f).normalized;
            Quaternion drawRotation=BladeRotation(drawAxis)*Quaternion.AngleAxis(65,Vector3.up);
            // Palm grip tilt belongs in GripBasis, not on the weapon's aiming axis.
            Quaternion readyRotation=BladeRotation(uprightAxis)*Quaternion.AngleAxis(45,Vector3.up);
            Vector3 drawnAxis=readyRotation*Vector3.up;
            Quaternion leftReadyRotation=BladeRotation(drawnAxis)*Quaternion.AngleAxis(-50,Vector3.up);
            Quaternion rightRotation=Quaternion.Slerp(initialRightRotation,drawRotation,engage);
            rightRotation=Quaternion.Slerp(rightRotation,readyRotation,clear);
            Vector3 leftTarget=Vector3.Lerp(initialLeft,mouth+drawAxis*.025f+Vector3.forward*.012f,engage);
            leftTarget=Vector3.Lerp(leftTarget,rightTarget-drawnAxis*((RightGripY-LeftGripY)*SwordScale),handover);
            Quaternion leftRotation=Quaternion.Slerp(initialLeftRotation,BladeRotation(-drawAxis),engage);
            leftRotation=Quaternion.Slerp(leftRotation,leftReadyRotation,handover);
            var pose=new HumanPose { bodyPosition=idle.bodyPosition,bodyRotation=idle.bodyRotation,
                muscles=(float[])(sample==0 ? idle.muscles : poses[sample-1].muscles).Clone() };
            for(int m=0;m<pose.muscles.Length;m++)
            {
                string name=HumanTrait.MuscleName[m];
                if(name=="Chest Front-Back") pose.muscles[m]=idle.muscles[m];
                if(name=="Chest Twist Left-Right") pose.muscles[m]=idle.muscles[m]-.035f*engage*(1-clear);
                if(name.Contains("Stretched")) pose.muscles[m]=Mathf.Lerp(idle.muscles[m],name.Contains("Thumb") ? -.50f : -.65f,engage);
                if(name.Contains("Spread")) pose.muscles[m]=Mathf.Lerp(idle.muscles[m],name.Contains("Thumb") ? -.35f : 0,engage);
            }
            handler.SetHumanPose(ref pose);
            void FitArm(bool left,Vector3 target,Quaternion rotation)
            {
                string side=left ? "Left" : "Right";
                int[] muscles=Enumerable.Range(0,HumanTrait.MuscleCount).Where(m=>HumanTrait.MuscleName[m].StartsWith(side+" ") &&
                    new[] { " Shoulder "," Arm "," Forearm "," Hand " }.Any(part=>HumanTrait.MuscleName[m].Contains(part))).ToArray();
                float[] previous=(float[])pose.muscles.Clone();
                // Preserve the established clavicle/shoulder seed. The elbow
                // opening itself is applied by the baked native IK hint below.
                Vector3 pole=root.TransformPoint(new Vector3(left ? -.36f : .38f,1.055f,.10f));
                float Cost()
                {
                    handler.SetHumanPose(ref pose);
                    float position=(Palm(left)-root.TransformPoint(target)).sqrMagnitude*240;
                    float angle=Quaternion.Angle(Bone(left,"Hand").rotation*(left ? leftGrip.rotation : rightGrip.rotation),root.rotation*rotation)/180;
                    float cost=position+angle*angle*3f+(Bone(left,"LowerArm").position-pole).sqrMagnitude*.15f;
                    // Do not obtain a grip by shrugging the shoulder or lifting
                    // the elbow above the wrist in the settled guard.
                    float highElbow=Mathf.Max(0,root.InverseTransformPoint(Bone(left,"LowerArm").position).y-target.y+.02f);
                    cost+=highElbow*highElbow*40*clear;
                    foreach(int m in muscles) cost+=Mathf.Pow(pose.muscles[m]-previous[m],2)*.0004f;
                    return cost;
                }
                foreach(float step in new[] { .12f,.06f,.03f,.015f,.006f }) for(int sweep=0;sweep<5;sweep++)
                    foreach(int m in muscles)
                    {
                        string name=HumanTrait.MuscleName[m];float limit=name.Contains("Hand In-Out") ? .65f : name.Contains("Hand Down-Up") ? .8f : .95f;
                        float lower=-limit,upper=limit;
                        if(name.Contains("Shoulder Down-Up"))
                        {lower=Mathf.Lerp(-limit,idle.muscles[m]-.20f,clear);upper=Mathf.Lerp(limit,idle.muscles[m]+.20f,clear);}
                        pose.muscles[m]=Mathf.Clamp(pose.muscles[m],lower,upper);
                        float original=pose.muscles[m],best=Cost(),chosen=original;
                        foreach(float sign in new[] { -1f,1f })
                        { pose.muscles[m]=Mathf.Clamp(original+step*sign,lower,upper);float error=Cost();if(error<best) { best=error;chosen=pose.muscles[m]; } }
                        pose.muscles[m]=chosen;
                    }
                // The warm start plus bounded per-frame muscle change prevents
                // choosing the opposite Euler/twist branch at the elbow/wrist.
                if(sample>0) foreach(int m in muscles) pose.muscles[m]=Mathf.Clamp(pose.muscles[m],poses[sample-1].muscles[m]-.12f,poses[sample-1].muscles[m]+.12f);
                handler.SetHumanPose(ref pose);
            }
            FitArm(false,rightTarget,rightRotation);FitArm(true,leftTarget,leftRotation);
            poses[sample]=pose;
            maxRightError=Mathf.Max(maxRightError,Vector3.Distance(Palm(false),root.TransformPoint(rightTarget)));
            maxLeftError=Mathf.Max(maxLeftError,Vector3.Distance(Palm(true),root.TransformPoint(leftTarget)));
            maxRightAngle=Mathf.Max(maxRightAngle,Quaternion.Angle(Bone(false,"Hand").rotation*rightGrip.rotation,root.rotation*rightRotation));
            Vector3 socketGrip=mouth-axis*(GuardY*SwordScale);
            sheathPositions[sample]=socketGrip;
            Quaternion q=Quaternion.Slerp(BladeRotation(stowedAxis),drawRotation,engage);
            q=Quaternion.Slerp(q,BladeRotation(stowedAxis),clear);
            if(sample>0 && Quaternion.Dot(q,sheathRotations[sample-1])<0) q=new Quaternion(-q.x,-q.y,-q.z,-q.w);
            sheathRotations[sample]=q;
            void IKTarget(bool left,Vector3 target,Quaternion rotation,Vector3[] wrists,Quaternion[] rotations,Vector3[] poles)
            {
                var hand=Bone(left,"Hand");var grip=left ? leftGrip : rightGrip;
                Quaternion wristRotation=root.rotation*rotation*Quaternion.Inverse(grip.rotation);
                if(left) wristRotation=Quaternion.Slerp(hand.rotation,wristRotation,handover);
                Vector3 palmOffset=Vector3.Scale(grip.point,hand.lossyScale);
                wrists[sample]=root.InverseTransformPoint(root.TransformPoint(target)-wristRotation*palmOffset);
                Quaternion local=Quaternion.Inverse(root.rotation)*wristRotation;
                if(sample>0 && Quaternion.Dot(local,rotations[sample-1])<0) local=new Quaternion(-local.x,-local.y,-local.z,-local.w);
                rotations[sample]=local;
                poles[sample]=Pole(left);
            }
            IKTarget(false,rightTarget,rightRotation,rightWrists,rightRotations,rightPoles);
            IKTarget(true,leftTarget,leftRotation,leftWrists,leftRotations,leftPoles);
            ikWeights[sample]=engage;
        }
        report.WriteLine($"Reference bake: 12 chronological pose beats; 72 intervals / 24 FPS / 3 seconds. Pre-contact-IK seed error R={maxRightError:F4}m L={maxLeftError:F4}m; seed wrist error={maxRightAngle:F2} degrees");
        AnimationClip Make(string name,bool reverse,bool hold)
        {
            float duration=hold ? 1 : Duration;int count=hold ? 1 : Frames;
            var clip=new AnimationClip { name=name,frameRate=FPS };
            void Curve(Type type,string property,Func<int,float> value)
            {
                var keys=new Keyframe[count+1];for(int f=0;f<=count;f++) keys[f]=new Keyframe(duration*f/count,value(f));
                if(keys.All(k=>Mathf.Abs(k.value-keys[0].value)<.00001f)) keys=new[] { keys[0],keys[keys.Length-1] };
                var curve=new AnimationCurve(keys);
                for(int k=0;k<curve.length;k++) { AnimationUtility.SetKeyLeftTangentMode(curve,k,AnimationUtility.TangentMode.ClampedAuto);AnimationUtility.SetKeyRightTangentMode(curve,k,AnimationUtility.TangentMode.ClampedAuto); }
                AnimationUtility.SetEditorCurve(clip,EditorCurveBinding.FloatCurve("",type,property),curve);
            }
            int Index(int frame) => hold ? Frames : reverse ? Frames-frame : frame;
            for(int m=0;m<HumanTrait.MuscleCount;m++)
            {
                string muscleName=HumanTrait.MuscleName[m];if(muscleName.Contains("Leg") || muscleName.Contains("Foot") || muscleName.Contains("Toes")) continue;
                int muscle=m;string[] words=muscleName.Split(' ');
                if(words.Length>=3 && new[] { "Thumb","Index","Middle","Ring","Little" }.Contains(words[1])) muscleName=words[0]+"Hand."+words[1]+"."+string.Join(" ",words.Skip(2));
                Curve(typeof(Animator),muscleName,f=>poses[Index(f)].muscles[muscle]);
            }
            foreach(int channel in new[] {0,1,2})
            { int c=channel;Curve(typeof(TanjiroSword),"sheathPosition."+"xyz"[c],f=>sheathPositions[Index(f)][c]); }
            foreach(int channel in new[] {0,1,2,3})
            { int c=channel;Curve(typeof(TanjiroSword),"sheathRotation."+"xyzw"[c],f=>sheathRotations[Index(f)][c]); }
            foreach(var field in new[] { ("rightWrist",rightWrists),("leftWrist",leftWrists),("rightElbow",rightPoles),("leftElbow",leftPoles) })
                foreach(int channel in new[] {0,1,2}) { int c=channel;Curve(typeof(TanjiroSword),field.Item1+"."+"xyz"[c],f=>field.Item2[Index(f)][c]); }
            foreach(var field in new[] { ("rightWristRotation",rightRotations),("leftWristRotation",leftRotations) })
                foreach(int channel in new[] {0,1,2,3}) { int c=channel;Curve(typeof(TanjiroSword),field.Item1+"."+"xyzw"[c],f=>field.Item2[Index(f)][c]); }
            Curve(typeof(TanjiroSword),"handIKWeight",f=>ikWeights[Index(f)]);
            var settings=AnimationUtility.GetAnimationClipSettings(clip);settings.loopTime=hold;AnimationUtility.SetAnimationClipSettings(clip,settings);
            var existing=AssetDatabase.LoadAssetAtPath<AnimationClip>(folder+name+".anim");
            if(existing!=null) { EditorUtility.CopySerialized(clip,existing);Object.DestroyImmediate(clip);clip=existing; }
            else AssetDatabase.CreateAsset(clip,folder+name+".anim");
            return clip;
        }
        Vector3 socketOffset=rightGrip.point-rightGrip.rotation*Vector3.up*(RightGripY*SwordScale/Bone(false,"Hand").lossyScale.x);
        return (Make("DrawKatana",false,false),Make("SheatheKatana",true,false),Make("KatanaReady",false,true),socketOffset,rightGrip.rotation);
    }

    public static (AnimationClip run,AnimationClip sprint) BakeLocomotion(Animator animator,string folder,StreamWriter report)
    {
        var root=animator.transform;
        var ready=AssetDatabase.LoadAssetAtPath<AnimationClip>(folder+"KatanaReady.anim");
        var sword=animator.GetComponent<TanjiroSword>();sword.Initialize();
        int layer=animator.GetLayerIndex("Sword Actions");animator.SetLayerWeight(layer,1);
        animator.SetFloat("SwordStride",0);animator.Play("Sword Actions.Ready",layer,0);animator.Update(0);
        using var handler=new HumanPoseHandler(animator.avatar,root);
        var seed=new HumanPose {muscles=new float[HumanTrait.MuscleCount]};handler.GetHumanPose(ref seed);
        var grip=GripBasis(animator,false);
        Transform Bone(bool left,string part) => animator.GetBoneTransform((HumanBodyBones)Enum.Parse(typeof(HumanBodyBones),(left ? "Left" : "Right")+part));
        Vector3 Torso() => root.InverseTransformDirection(animator.GetBoneTransform(HumanBodyBones.Chest).position-animator.GetBoneTransform(HumanBodyBones.Hips).position);
        Vector3 upright=Torso();
        AnimationClip Make(bool sprint)
        {
            var pose=new HumanPose {bodyPosition=seed.bodyPosition,bodyRotation=seed.bodyRotation,muscles=(float[])seed.muscles.Clone()};
            // Spread the lean along the spine instead of pitching the player,
            // capsule, camera target or pelvis (which still belongs to the gait).
            int spine=Array.IndexOf(HumanTrait.MuscleName,"Spine Front-Back"),chest=Array.IndexOf(HumanTrait.MuscleName,"Chest Front-Back");
            pose.muscles[spine]=seed.muscles[spine]-(sprint ? .65f : .55f);
            pose.muscles[chest]=seed.muscles[chest]-(sprint ? .35f : .30f);
            pose.muscles[Array.IndexOf(HumanTrait.MuscleName,"Right Shoulder Front-Back")]=-.15f;
            pose.muscles[Array.IndexOf(HumanTrait.MuscleName,"Right Shoulder Down-Up")]=-.10f;
            // Reference: right hand carried back at the waist, blade pointing
            // forward/down outside the leg; the left arm is entirely unkeyed.
            Vector3 right=sprint ? new Vector3(.34f,.94f,.06f) : new Vector3(.33f,1.03f,-.06f);
            Vector3 axis=new Vector3(.08f,-.27f,.96f).normalized;
            Quaternion palmRotation=BladeRotation(axis)*Quaternion.AngleAxis(-50,Vector3.up);
            Vector3 pole=sprint ? new Vector3(.1060f,1.2891f,-.0453f) : new Vector3(.1241f,1.3566f,-.1008f);
            handler.SetHumanPose(ref pose);
            report.WriteLine((sprint ? "Sprint" : "Run")+" authored torso lean="+Vector3.Angle(upright,Torso()).ToString("F2")+" degrees; trailing blade="+axis);
            var clip=Object.Instantiate(ready);clip.name=sprint ? "KatanaSprint" : "KatanaRun";clip.frameRate=FPS;
            // Do not bake or mask a replacement gait for the free arm.
            foreach(var binding in AnimationUtility.GetCurveBindings(clip).Where(b=>
                b.type==typeof(Animator) && b.propertyName.StartsWith("Left") ||
                b.type==typeof(TanjiroSword) && b.propertyName.StartsWith("left")))
                AnimationUtility.SetEditorCurve(clip,binding,null);
            const int count=16;const float duration=2f/3;
            void Curve(Type type,string property,Func<float,float> value)
            {
                var curve=new AnimationCurve(Enumerable.Range(0,count+1).Select(f=>new Keyframe(f*duration/count,value(f/(float)count))).ToArray());
                for(int k=0;k<curve.length;k++)
                {AnimationUtility.SetKeyLeftTangentMode(curve,k,AnimationUtility.TangentMode.ClampedAuto);AnimationUtility.SetKeyRightTangentMode(curve,k,AnimationUtility.TangentMode.ClampedAuto);}
                AnimationUtility.SetEditorCurve(clip,EditorCurveBinding.FloatCurve("",type,property),curve);
            }
            // Only the torso and sword arm are authored; legs and free arm
            // continue to come from the third-person controller's base gait.
            foreach(var binding in AnimationUtility.GetCurveBindings(clip).Where(b=>b.type==typeof(Animator)))
            {
                string name=binding.propertyName;
                if(name.Contains("Hand.")) name=name.Replace("Hand."," ").Replace("."," ");
                int muscle=Array.IndexOf(HumanTrait.MuscleName,name);
                bool torso=name.StartsWith("Spine ") || name.StartsWith("Chest ") || name.Contains(" Shoulder ");
                float value=torso ? pose.muscles[muscle] : AnimationUtility.GetEditorCurve(ready,binding).Evaluate(0);
                Curve(typeof(Animator),binding.propertyName,t=>value);
            }
            {
                string label="right";var hand=Bone(false,"Hand");
                Quaternion rotation=palmRotation*Quaternion.Inverse(grip.rotation);
                var component=animator.GetComponent<TanjiroSword>();
                Quaternion reference=component.rightWristRotation;
                if(Quaternion.Dot(rotation,reference)<0) rotation=new Quaternion(-rotation.x,-rotation.y,-rotation.z,-rotation.w);
                Vector3 wrist=right-rotation*Vector3.Scale(grip.point,hand.lossyScale);
                for(int channel=0;channel<3;channel++)
                {
                    int c=channel;
                    Curve(typeof(TanjiroSword),label+"Wrist."+"xyz"[c],t=>wrist[c]+(c==1 ? .006f*(1-Mathf.Cos(t*Mathf.PI*4)) : 0));
                    Curve(typeof(TanjiroSword),label+"Elbow."+"xyz"[c],t=>pole[c]);
                }
                for(int channel=0;channel<4;channel++) {int c=channel;Curve(typeof(TanjiroSword),label+"WristRotation."+"xyzw"[c],t=>rotation[c]);}
            }
            // Remove the copied 1-second hold endpoints so the loop is 16/24s.
            foreach(var binding in AnimationUtility.GetCurveBindings(clip).Where(b=>b.type==typeof(TanjiroSword) && (b.propertyName.StartsWith("sheath")||b.propertyName=="handIKWeight")))
            {float value=AnimationUtility.GetEditorCurve(clip,binding).Evaluate(0);Curve(binding.type,binding.propertyName,t=>value);}
            var settings=AnimationUtility.GetAnimationClipSettings(clip);settings.loopTime=true;settings.stopTime=duration;AnimationUtility.SetAnimationClipSettings(clip,settings);
            string path=folder+clip.name+".anim";var existing=AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if(existing==null) AssetDatabase.CreateAsset(clip,path);
            else {EditorUtility.CopySerialized(clip,existing);Object.DestroyImmediate(clip);clip=existing;}
            return clip;
        }
        return (Make(false),Make(true));
    }
}
