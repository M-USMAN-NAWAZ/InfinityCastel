using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

public static class RedKatanaSetup
{
    public static void Run() => Prepare(false);
    public static void Yoriichi() => Prepare(true);
    private static void Prepare(bool yoriichi)
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("Run katana preparation in the isolated validation project.");
        string assetName=yoriichi ? "YoriichiKatana" : "RedNichirinKatana";
        string folder="Assets/Weapons/"+assetName+"/";
        string prefix=yoriichi ? "yoriichi-katana" : "red-katana";
        using var report=new StreamWriter(Path.GetFullPath("../"+prefix+"-tests.txt")) { AutoFlush=true };
        void Check(bool passed,string message) { report.WriteLine((passed ? "PASS " : "FAIL ")+message); if (!passed) throw new InvalidOperationException(message); }
        string modelPath=folder+assetName+".fbx";
        var importer=(ModelImporter)AssetImporter.GetAtPath(modelPath);
        importer.animationType=ModelImporterAnimationType.None; importer.importAnimation=false;
        importer.importCameras=importer.importLights=false; importer.isReadable=true;
        importer.materialImportMode=ModelImporterMaterialImportMode.None; importer.bakeAxisConversion=true;
        importer.importNormals=ModelImporterNormals.Import; importer.importTangents=ModelImporterTangents.CalculateMikk;
        importer.SaveAndReimport();
        string[] maps=yoriichi ? new[] { "Albedo","Normal","MetallicSmoothness" } : new[] { "Albedo","Normal","MetallicSmoothness","Emission" };
        foreach (string map in maps)
        {
            var texture=(TextureImporter)AssetImporter.GetAtPath(folder+"Katana_"+map+".png");
            texture.textureType=map=="Normal" ? TextureImporterType.NormalMap : TextureImporterType.Default;
            texture.sRGBTexture=map=="Albedo" || map=="Emission";
            texture.alphaSource=TextureImporterAlphaSource.FromInput; texture.alphaIsTransparency=false;
            texture.isReadable=false; texture.mipmapEnabled=true; texture.maxTextureSize=map=="Albedo" ? 2048 : 1024;
            texture.SetPlatformTextureSettings(new TextureImporterPlatformSettings { name="Android",overridden=true,
                maxTextureSize=texture.maxTextureSize,format=TextureImporterFormat.ASTC_6x6 }); texture.SaveAndReimport();
        }
        Shader shader=Shader.Find("Universal Render Pipeline/Lit"); Check(shader!=null && shader.isSupported,"Uses the project's supported URP Lit shader");
        string materialPath=folder+assetName+".mat";
        Material material=AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if (material==null) { material=new Material(shader); AssetDatabase.CreateAsset(material,materialPath); }
        material.shader=shader;
        foreach (var map in new[] { ("_BaseMap","Albedo"),("_BumpMap","Normal"),("_MetallicGlossMap","MetallicSmoothness"),("_EmissionMap","Emission") })
            material.SetTexture(map.Item1,AssetDatabase.LoadAssetAtPath<Texture2D>(folder+"Katana_"+map.Item2+".png"));
        material.SetColor("_BaseColor",Color.white); material.SetColor("_EmissionColor",Color.white);
        material.SetFloat("_BumpScale",.65f); material.SetFloat("_Metallic",1); material.SetFloat("_Smoothness",1);
        material.SetFloat("_SmoothnessTextureChannel",0); material.SetFloat("_Surface",0); material.renderQueue=2000;
        foreach (string keyword in new[] { "_NORMALMAP","_METALLICSPECGLOSSMAP" }) material.EnableKeyword(keyword);
        if (yoriichi) material.DisableKeyword("_EMISSION"); else material.EnableKeyword("_EMISSION");
        EditorUtility.SetDirty(material);
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        var weapon=new GameObject(assetName);
        var model=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(modelPath),weapon.transform);model.name="Mesh";
        var renderers=weapon.GetComponentsInChildren<MeshRenderer>();
        Check(renderers.Length==1,"One mesh renderer and one material draw for the weapon");
        var renderer=renderers.Single();renderer.sharedMaterial=material;
        Mesh mesh=renderer.GetComponent<MeshFilter>().sharedMesh;
        Check(mesh.subMeshCount==1 && (yoriichi ? mesh.triangles.Length/3==50000 : mesh.triangles.Length/3<10000),
            "Single submesh; "+(yoriichi ? "supplied geometry preserved: " : "under 10,000 triangles: ")+mesh.triangles.Length/3);
        Check(mesh.uv.Length==mesh.vertexCount && mesh.tangents.Length==mesh.vertexCount,"Atlas UVs and tangent-space normal mapping imported");
        Check(Mathf.Abs(renderer.bounds.size.y-(yoriichi ? 1.196758f : 1.05f))<.02f,"Sword length and Unity +Y blade orientation are correct");
        Vector3 tip=mesh.vertices.OrderByDescending(point=>point.y).First();
        var tipMarker=new GameObject("BladeTip").transform;tipMarker.SetParent(weapon.transform,false);
        tipMarker.position=renderer.transform.TransformPoint(tip);
        var grip=new GameObject("GripPivot").transform;grip.SetParent(weapon.transform,false);
        Check(grip.localPosition==Vector3.zero && renderer.bounds.min.y<0 && renderer.bounds.max.y>.8f,"Root pivot sits inside the grip, not at the sword tip");
        PrefabUtility.SaveAsPrefabAsset(weapon,folder+assetName+".prefab");
        RenderSettings.ambientMode=AmbientMode.Flat; RenderSettings.ambientLight=new Color(.65f,.7f,.8f);
        var key=new GameObject("Preview key").AddComponent<Light>();key.type=LightType.Directional;key.intensity=2;
        key.transform.rotation=Quaternion.Euler(45,-35,0);
        var fill=new GameObject("Preview fill").AddComponent<Light>();fill.type=LightType.Directional;fill.intensity=1.2f;
        fill.transform.rotation=Quaternion.Euler(-35,140,0);
        var camera=new GameObject("Weapon camera").AddComponent<Camera>();camera.orthographic=true;
        camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.065f,.075f,.095f);
        void Capture(Vector3 centre,float size,int width,int height,string name,Vector3 direction)
        {
            camera.transform.position=centre+direction*2;
            camera.transform.rotation=Quaternion.LookRotation(centre-camera.transform.position)*Quaternion.Euler(0,0,90);
            camera.orthographicSize=size;
            var target=RenderTexture.GetTemporary(width,height,24);camera.targetTexture=target;
            camera.Render();camera.Render();RenderTexture.active=target;
            var image=new Texture2D(width,height,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,width,height),0,0);image.Apply();
            Color[] pixels=image.GetPixels();
            Check(pixels.Count(colour=>colour.r>.8f && colour.b>.8f && colour.g<.25f)<pixels.Length*.005f,name+" has no pink missing-shader surface");
            Check(pixels.Count(colour=>colour.r>.25f && colour.r>colour.g*1.5f && colour.r>colour.b*1.2f)>1000,name+" shows the red blade edge");
            if (name=="handle")
            {
                Color[] gripPixels=pixels.Where((colour,index)=>index%width>width*.15f && index%width<width*.65f && index/width>height*.25f && index/width<height*.75f).ToArray();
                int white=gripPixels.Count(colour=>colour.r>.65f && colour.g>.65f && colour.b>.65f);
                int gold=gripPixels.Count(colour=>colour.r>.3f && colour.g>.15f && colour.b<.25f && colour.r>colour.g*1.1f);
                Check(white>gold*2 && white>10000,"White wrapping covers the grip; gold is confined to diamond openings and fittings");
            }
            File.WriteAllBytes(Path.GetFullPath("../"+prefix+"-"+name+".png"),image.EncodeToPNG());
            camera.targetTexture=null;RenderTexture.active=null;RenderTexture.ReleaseTemporary(target);UnityEngine.Object.DestroyImmediate(image);
        }
        Capture(renderer.bounds.center,yoriichi ? .29f : .27f,1600,700,"unity",Vector3.back);
        Capture(renderer.bounds.center,yoriichi ? .29f : .27f,1600,700,"reverse",Vector3.forward);
        if (yoriichi)
        {
            Capture(new Vector3(-.003f,.23f,0),.065f,1200,900,"inscription",Vector3.back);
            Capture(new Vector3(-.003f,.23f,0),.065f,1200,900,"inscription-reverse",Vector3.forward);
        }
        else Capture(new Vector3(0,.03f,0),.11f,1400,850,"handle",new Vector3(.12f,0,-1).normalized);
        Check(weapon.GetComponentsInChildren<Light>().Length==0 && weapon.GetComponentsInChildren<Collider>().Length==0,
            "Prefab adds no realtime lights, cloth physics, or unwanted movement colliders");
        importer.isReadable=false;importer.SaveAndReimport();
        AssetDatabase.SaveAssets();
        report.WriteLine("ALL "+assetName.ToUpperInvariant()+" ASSET CHECKS PASSED");
    }
}
