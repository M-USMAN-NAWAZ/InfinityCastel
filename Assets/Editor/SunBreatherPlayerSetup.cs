using System;
using System.IO;
using System.Linq;
using Cinemachine;
using StarterAssets;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

public static class SunBreatherPlayerSetup
{
    private const string ScenePath="Assets/StarterAssets/ThirdPersonController/Scenes/Playground.unity";
    private const string Folder="Assets/Characters/SunBreathers/";

    [MenuItem("Infinity Castle/Player/Use Tanjiro on Current Player")]
    public static void Install()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play mode before changing the saved player.");
        Scene scene=SceneManager.GetActiveScene();
        if (scene.path!=ScenePath) throw new InvalidOperationException("Open Playground before installing Tanjiro.");
        var player=scene.GetRootGameObjects().SelectMany(root=>root.GetComponentsInChildren<ThirdPersonController>(true))
            .Single(value=>value.gameObject.activeInHierarchy);
        File.Copy(ScenePath,"Temp/Playground-before-Tanjiro.unity",true);
        Undo.IncrementCurrentGroup(); int group=Undo.GetCurrentGroup(); Undo.SetCurrentGroupName("Replace Akaza with Tanjiro");
        try
        {
            Apply(player);
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Could not save the Tanjiro player scene.");
            Selection.activeGameObject=player.gameObject;
            Debug.Log("Tanjiro installed on the existing PlayerArmature; old visuals disabled, controls and camera references preserved.",player);
        }
        catch { Undo.RevertAllDownToGroup(group); throw; }
        finally { Undo.CollapseUndoOperations(group); }
    }

    private static void Apply(ThirdPersonController player)
    {
        var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(Folder+"Tanjiro.prefab");
        var fitted=AssetDatabase.LoadAssetAtPath<GameObject>(Folder+"TanjiroThirdPerson.prefab");
        if (prefab==null || fitted==null) throw new InvalidOperationException("The prepared Tanjiro prefabs are missing.");
        var existing=player.GetComponentsInChildren<Animator>(true).FirstOrDefault(animator=>animator.avatar!=null &&
            AssetDatabase.GetAssetPath(animator.avatar)==Folder+"Tanjiro/Tanjiro_Humanoid.fbx");
        if (existing!=null && existing.isActiveAndEnabled) return;
        var oldAnimator=player.GetComponent<Animator>();
        var locomotion=oldAnimator!=null ? oldAnimator.runtimeAnimatorController :
            fitted.GetComponentInChildren<Animator>().runtimeAnimatorController;
        var oldVisuals=player.GetComponentsInChildren<SkinnedMeshRenderer>(true).Select(renderer=>
        {
            Transform root=renderer.transform;
            while (root.parent!=null && root.parent!=player.transform) root=root.parent;
            return root.gameObject;
        }).Distinct().ToArray();
        if (oldVisuals.Contains(player.gameObject)) throw new InvalidOperationException("Unexpected renderer on the player root.");
        // Reuse the user's already-placed Tanjiro preview instead of leaving a duplicate in the castle.
        var visual=player.gameObject.scene.GetRootGameObjects().FirstOrDefault(root=>
            PrefabUtility.GetCorrespondingObjectFromSource(root)==prefab);
        if (visual==null)
        {
            visual=(GameObject)PrefabUtility.InstantiatePrefab(prefab,player.gameObject.scene);
            Undo.RegisterCreatedObjectUndo(visual,"Add Tanjiro visual");
        }
        Undo.SetTransformParent(visual.transform,player.transform,"Attach Tanjiro to player");
        Undo.RecordObject(visual.transform,"Align Tanjiro visual");
        visual.transform.localPosition=Vector3.zero; visual.transform.localRotation=Quaternion.identity; visual.transform.localScale=Vector3.one;
        foreach (var old in oldVisuals) { Undo.RecordObject(old,"Disable old player visual"); old.SetActive(false); }
        if (oldAnimator!=null) { Undo.RecordObject(oldAnimator,"Disable legacy Animator"); oldAnimator.enabled=false; }
        var animator=visual.GetComponentInChildren<Animator>();
        Undo.RecordObject(animator,"Use existing locomotion controller");
        animator.runtimeAnimatorController=locomotion; animator.applyRootMotion=false;
        var events=animator.GetComponent<ThirdPersonAnimationEvents>() ?? Undo.AddComponent<ThirdPersonAnimationEvents>(animator.gameObject);
        Undo.RecordObject(events,"Route player animation events"); events.Bind(player);
        var capsule=player.GetComponent<CharacterController>(); var settings=fitted.GetComponent<CharacterController>();
        Undo.RecordObject(capsule,"Fit Tanjiro collision capsule");
        capsule.height=settings.height; capsule.radius=settings.radius; capsule.center=settings.center;
        Undo.RecordObject(player,"Fit ground probe"); player.GroundedRadius=settings.radius;
        foreach (var component in new UnityEngine.Object[] { visual.transform,animator,events,capsule,player })
            PrefabUtility.RecordPrefabInstancePropertyModifications(component);
        foreach (var old in oldVisuals) PrefabUtility.RecordPrefabInstancePropertyModifications(old);
        if (oldAnimator!=null) PrefabUtility.RecordPrefabInstancePropertyModifications(oldAnimator);
        animator.Rebind(); animator.Play("Idle Walk Run Blend",0,0); animator.Update(0);
    }

    public static void Validate()
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("Validate in the isolated project.");
        var scene=EditorSceneManager.OpenScene(ScenePath);
        var player=UnityEngine.Object.FindFirstObjectByType<ThirdPersonController>();
        var position=player.transform.position; var rotation=player.transform.rotation;
        var target=player.CinemachineCameraTarget; var input=player.GetComponent<PlayerInput>();
        float move=player.MoveSpeed,sprint=player.SprintSpeed;
        var follows=UnityEngine.Object.FindObjectsByType<CinemachineVirtualCamera>(FindObjectsSortMode.None).Select(camera=>(camera,camera.Follow)).ToArray();
        var castles=UnityEngine.Object.FindObjectsByType<DynamicInfinityCastle>(FindObjectsInactive.Include,FindObjectsSortMode.None).Select(castle=>(castle,castle.player)).ToArray();
        Install();
        using var report=new StreamWriter(Path.GetFullPath("../tanjiro-player-tests.txt"));
        void Check(bool passed,string message) { report.WriteLine((passed ? "PASS " : "FAIL ")+message); report.Flush(); if (!passed) throw new InvalidOperationException(message); }
        Check(position==player.transform.position && rotation==player.transform.rotation,"Existing player root and spawn transform preserved");
        Check(player.CinemachineCameraTarget==target && player.GetComponent<PlayerInput>()==input && player.MoveSpeed==move && player.SprintSpeed==sprint,
            "Existing camera target, input component, and movement tuning preserved");
        Check(follows.All(pair=>pair.camera.Follow==pair.Follow) && castles.All(pair=>pair.castle.player==pair.player),"Camera and castle player references unchanged");
        var animators=player.GetComponentsInChildren<Animator>().Where(animator=>animator.enabled).ToArray();
        Check((player.GetComponent<Animator>()==null || !player.GetComponent<Animator>().enabled) && animators.Length==1,"Only Tanjiro's child Animator controls the visible player");
        var animator=animators.Single();
        Check(AssetDatabase.GetAssetPath(animator.avatar)==Folder+"Tanjiro/Tanjiro_Humanoid.fbx" && animator.avatar.isValid && !animator.applyRootMotion,
            "Tanjiro Humanoid Avatar, existing locomotion, and controller-driven movement enabled");
        Check(player.GetComponentsInChildren<SkinnedMeshRenderer>().All(renderer=>renderer.name.Contains("Tanjiro")),"Akaza and other old visuals are hidden without deleting their assets");
        Check(animator.GetComponent<ThirdPersonAnimationEvents>()!=null && Mathf.Abs(player.GetComponent<CharacterController>().height-1.65f)<.001f,
            "Animation events and Tanjiro-sized collision capsule installed");
        Check(!player.GetComponentInChildren<SkinnedMeshRenderer>().bones.Any(bone=>bone.name.Contains("Sleeve")),
            "The saved Playground player inherits compressed sleeves without the broken garment anchors");
        Install(); Check(player.GetComponentsInChildren<Animator>().Count(value=>value.enabled)==1,"Repeated setup does not duplicate Tanjiro");
        player.Initialize(); player.GetComponent<StarterAssetsInputs>().MoveInput(Vector2.up);
        for (int frame=0; frame<60; frame++) { player.Simulate(1f/60f); animator.Update(1f/60f); }
        Check(animator.GetFloat("Speed")>1 && animator.GetFloat("MotionSpeed")>.5f,"Existing scene controller drives Tanjiro's locomotion parameters");
        report.WriteLine("ALL TANJIRO PLAYER SETUP CHECKS PASSED");
    }
}
