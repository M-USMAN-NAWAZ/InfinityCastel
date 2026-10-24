using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

public static class CastleSkyboxBake
{
    public const string Image = "Assets/Images/CastleFromGameModels.png";
    public const string Sky = "Assets/Mat/MyMat/InfintyCastelSkyBox.mat";

    public static void Bake()
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("Run the isolated Skybox validation so the open scene is not disturbed.");
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        ShaderUtil.allowAsyncCompilation = false;
        var runtime = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/prefabs/InfinityCastleRuntime.prefab").GetComponent<DynamicInfinityCastle>();
        GameObject[] prefabs = new[] { runtime.roomPrefab, runtime.corridorPrefab, runtime.junctionPrefab,
            runtime.doubleStairPrefab, runtime.roofAccessStairPrefab }.Concat(runtime.pathPrefabs).ToArray();
        var draws = new List<(Mesh mesh, Material material, Matrix4x4 local, List<Matrix4x4> instances)>[prefabs.Length];
        var counts = new int[prefabs.Length];
        for (int i = 0; i < prefabs.Length; i++)
        {
            draws[i] = new();
            LODGroup lod = prefabs[i].GetComponent<LODGroup>();
            Renderer[] renderers = lod == null ? prefabs[i].GetComponentsInChildren<Renderer>() : lod.GetLODs()[0].renderers;
            foreach (Renderer renderer in renderers)
            {
                var material = new Material(renderer.sharedMaterial) { enableInstancing = true };
                material.SetVector("_CastleBuild", Vector4.zero); material.SetVector("_CastleAssembly", new Vector4(1,0,0,0));
                draws[i].Add((renderer.GetComponent<MeshFilter>().sharedMesh, material,
                    prefabs[i].transform.worldToLocalMatrix * renderer.transform.localToWorldMatrix, new()));
            }
        }
        void Add(int type, Vector3 position, Quaternion rotation, Vector3 scale)
        {
            counts[type]++;
            Matrix4x4 world = Matrix4x4.TRS(position, rotation, scale);
            foreach (var draw in draws[type]) draw.instances.Add(world * draw.local);
        }
        var random = new System.Random(271828);
        float Range(float a, float b) => Mathf.Lerp(a,b,(float)random.NextDouble());
        Vector3[] ups = { Vector3.up, Vector3.down, Vector3.left, Vector3.right, Vector3.forward, Vector3.back };
        int clusters = 0;
        for (int layer = 0; layer < 3; layer++)
        {
            int count = 96 + layer * 42;
            for (int index = 0; index < count; index++)
            {
                float y = 1 - 2 * (index + .5f) / count, angle = index * 2.39996323f + layer * 1.7f;
                Vector3 direction = new(Mathf.Sqrt(1-y*y)*Mathf.Cos(angle), y, Mathf.Sqrt(1-y*y)*Mathf.Sin(angle));
                Vector3 centre = direction * (240 + layer * 220 + Range(-20,20));
                Quaternion rotation = Quaternion.FromToRotation(Vector3.up, ups[(index+layer)%6]) * Quaternion.Euler(0,random.Next(4)*90,0);
                float size = (3 + layer * 1.45f) * Range(.85f,1.15f);
                int columns = random.Next(2,5), rows = random.Next(1,3), floors = random.Next(3,8);
                void Place(int type, Vector3 point, Quaternion turn, Vector3 stretch) =>
                    Add(type, centre + rotation * (point * size), rotation * turn, stretch * size);
                for (int floor = 0; floor < floors; floor++)
                {
                    float height = (floor - floors * .5f) * 6;
                    for (int x = 0; x < columns; x++) for (int z = 0; z < rows; z++)
                    {
                        int type = random.NextDouble() < .7 ? 0 : random.Next(1,3);
                        Place(type, new Vector3((x-(columns-1)*.5f)*7.6f,height,(z-(rows-1)*.5f)*6.6f), Quaternion.identity, Vector3.one);
                    }
                    float edge = columns * 3.8f;
                    int bridge = 5 + (index + floor) % runtime.pathPrefabs.Count;
                    Place(bridge, new Vector3(edge+7,height,0), Quaternion.Euler(0,90,0), new Vector3(1,1,1.2f));
                    Place(0, new Vector3(edge+17,height,0), Quaternion.identity, Vector3.one);
                    // Use the actual authored stair_2 as a whole, never a fabricated ramp.
                    int switchback = Array.FindIndex(prefabs, prefab => prefab.name == "PackSwitchbackStair");
                    Place(switchback, new Vector3(-edge-2,height,0), Quaternion.identity, Vector3.one);
                    if (floor%2==0) Place(3+index%2, new Vector3(0,height,rows*3.3f+4), Quaternion.identity, Vector3.one);
                }
                clusters++;
            }
        }
        var camera = new GameObject("Offline castle backdrop camera").AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.10f,.045f,.02f);
        camera.fieldOfView = 90; camera.aspect = 1; camera.nearClipPlane = 1; camera.farClipPlane = 1400;
        RenderSettings.fog = true; RenderSettings.fogMode = FogMode.Exponential; RenderSettings.fogDensity = .0011f;
        RenderSettings.fogColor = new Color(.14f,.075f,.035f);
        RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = new Color(.28f,.22f,.17f);
        Shader.SetGlobalVector("_CastleStreamCenter", Vector4.zero);
        var matrices = new Matrix4x4[64];
        void Draw(ScriptableRenderContext context, Camera current)
        {
            if (current != camera) return;
            foreach (var source in draws) foreach (var draw in source)
            {
                var parameters = new RenderParams(draw.material) { camera = camera, worldBounds = new Bounds(Vector3.zero,Vector3.one*3000),
                    shadowCastingMode = ShadowCastingMode.Off, receiveShadows = false, lightProbeUsage = LightProbeUsage.Off };
                for (int offset = 0; offset < draw.instances.Count; offset += matrices.Length)
                {
                    int count = Mathf.Min(matrices.Length, draw.instances.Count-offset);
                    draw.instances.CopyTo(offset, matrices, 0, count);
                    Graphics.RenderMeshInstanced(parameters, draw.mesh, 0, matrices, count);
                }
            }
        }
        const int resolution = 1024, width = 4096, heightPixels = 2048;
        Vector3[] forward = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
        Vector3[] up = { Vector3.up, Vector3.up, Vector3.back, Vector3.forward, Vector3.up, Vector3.up };
        var faces = new Texture2D[6];
        RenderPipelineManager.beginCameraRendering += Draw;
        try
        {
            for (int face = 0; face < faces.Length; face++)
            {
                camera.transform.rotation = Quaternion.LookRotation(forward[face],up[face]);
                RenderTexture target = RenderTexture.GetTemporary(resolution,resolution,24);
                camera.targetTexture = target; camera.Render(); camera.Render();
                RenderTexture previous = RenderTexture.active; RenderTexture.active = target;
                faces[face] = new Texture2D(resolution,resolution,TextureFormat.RGB24,false);
                faces[face].ReadPixels(new Rect(0,0,resolution,resolution),0,0); faces[face].Apply();
                RenderTexture.active = previous; camera.targetTexture = null; RenderTexture.ReleaseTemporary(target);
            }
        }
        finally { RenderPipelineManager.beginCameraRendering -= Draw; }
        // Reproject the actual scene views into one wrap-safe panorama. No AI
        // replacement models, artificial enclosing geometry, or runtime capture.
        var panorama = new Texture2D(width,heightPixels,TextureFormat.RGB24,false);
        var pixels = new Color32[width*heightPixels];
        var sine = new float[width]; var cosine = new float[width];
        for (int x=0; x<width; x++) { float a=((x+.5f)/width-.5f)*Mathf.PI*2; sine[x]=Mathf.Sin(a); cosine[x]=Mathf.Cos(a); }
        for (int row=0; row<heightPixels; row++)
        {
            float latitude=((row+.5f)/heightPixels-.5f)*Mathf.PI, vertical=Mathf.Sin(latitude), radius=Mathf.Cos(latitude);
            for (int x=0; x<width; x++)
            {
                Vector3 direction = new(radius*sine[x],vertical,radius*cosine[x]);
                Vector3 a = new(Mathf.Abs(direction.x),Mathf.Abs(direction.y),Mathf.Abs(direction.z));
                int face = a.x>=a.y && a.x>=a.z ? direction.x>0 ? 0 : 1 : a.y>=a.z ? direction.y>0 ? 2 : 3 : direction.z>0 ? 4 : 5;
                float distance = Vector3.Dot(direction,forward[face]);
                pixels[row*width+x] = faces[face].GetPixelBilinear(.5f+.5f*Vector3.Dot(direction,Vector3.Cross(up[face],forward[face]))/distance,
                    .5f+.5f*Vector3.Dot(direction,up[face])/distance);
            }
        }
        panorama.SetPixels32(pixels); panorama.Apply(); File.WriteAllBytes(Image,panorama.EncodeToPNG());
        foreach (Texture2D face in faces) UnityEngine.Object.DestroyImmediate(face);
        UnityEngine.Object.DestroyImmediate(panorama); UnityEngine.Object.DestroyImmediate(camera.gameObject);
        foreach (var source in draws) foreach (var draw in source) UnityEngine.Object.DestroyImmediate(draw.material);
        AssetDatabase.ImportAsset(Image,ImportAssetOptions.ForceSynchronousImport);
        var importer = (TextureImporter)AssetImporter.GetAtPath(Image);
        importer.sRGBTexture=true; importer.mipmapEnabled=true; importer.isReadable=false;
        importer.wrapModeU=TextureWrapMode.Repeat; importer.wrapModeV=TextureWrapMode.Clamp;
        importer.maxTextureSize=4096; importer.textureCompression=TextureImporterCompression.Compressed;
        importer.SetPlatformTextureSettings(new TextureImporterPlatformSettings { name="Android", overridden=true,
            maxTextureSize=4096, format=TextureImporterFormat.ASTC_6x6 });
        importer.SaveAndReimport();
        Apply();
        string sources = string.Join("\n",prefabs.Select((prefab,i)=>prefab.name+": "+counts[i]+" instances"));
        File.WriteAllText(Path.GetFullPath("../castle-skybox-bake.txt"),$"Offline capture: {clusters} varied clusters; {counts.Sum()} source models.\n{sources}\n4096x2048 panorama; Android ASTC 6x6; no runtime capture or geometry.\n");
    }

    public static void Apply()
    {
        Material sky = AssetDatabase.LoadAssetAtPath<Material>(Sky);
        sky.shader=Shader.Find("Skybox/Panoramic"); sky.SetTexture("_MainTex",AssetDatabase.LoadAssetAtPath<Texture2D>(Image));
        sky.SetFloat("_Mapping",1); sky.SetFloat("_ImageType",0); sky.SetFloat("_Exposure",.85f); sky.SetFloat("_Rotation",0);
        sky.SetColor("_Tint",Color.gray); sky.shaderKeywords=Array.Empty<string>();
        EditorUtility.SetDirty(sky); AssetDatabase.SaveAssets();
    }
}
