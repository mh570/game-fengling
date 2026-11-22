using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace T3ssDemo
{
    /// <summary>
    /// 完全按工程原样：他们的模型 + 他们的材质 + 他们的 DrawLeaf，不做任何替换。
    /// 目的是看 Unity 6 下工程的真实输出（预期：坏掉的 shader graph → 粉红/不可见）。
    /// </summary>
    public static class ProjectOriginalTest
    {
        [MenuItem("T3SS/工程原样")]
        public static void Run()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var model = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Imported/Model/model.fbx");
            if (model == null) { Debug.LogError("[Orig] model.fbx 缺"); EditorApplication.Exit(1); return; }
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(model);
            inst.transform.position = Vector3.zero;

            // 原样：只显示 树叶 + 树干（其他是地砖/墙/石头）
            Mesh shapeMesh = null;
            foreach (var mr in inst.GetComponentsInChildren<MeshRenderer>())
            {
                bool keep = mr.name.Contains("树叶") || mr.name.Contains("树干");
                mr.enabled = keep;
                if (mr.name.Contains("树叶") && mr.GetComponent<MeshFilter>() != null)
                    shapeMesh = mr.GetComponent<MeshFilter>().sharedMesh;
                if (keep)
                {
                    var s = mr.sharedMaterial != null ? mr.sharedMaterial.shader : null;
                    Debug.Log("[Orig] " + mr.name + " 材质=" + (mr.sharedMaterial != null ? mr.sharedMaterial.name : "null") +
                              " shader=" + (s != null ? s.name : "null") + " supported=" + (s != null && s.isSupported));
                }
            }

            var leafMesh = AssetDatabase.LoadAssetAtPath<Mesh>("Assets/Imported/Model/Leaf.mesh");
            var leafMat  = AssetDatabase.LoadAssetAtPath<Material>("Assets/Imported/Material/树叶.mat");
            var leafMat2 = AssetDatabase.LoadAssetAtPath<Material>("Assets/Imported/Material/树叶 2.mat");

            // ★ 原样参数，一个都不改
            var dl = inst.AddComponent<DrawLeaf>();
            dl.ShapeMesh = shapeMesh;
            dl.LeafMesh = leafMesh;
            dl.mats = new System.Collections.Generic.List<Material>();
            if (leafMat != null)  dl.mats.Add(leafMat);
            if (leafMat2 != null) dl.mats.Add(leafMat2);
            dl.LeafDenesity = 0.443f;
            dl.LeafSize = 1.449f;
            dl.LeafOffset = 0.356f;
            dl.LightOffset = 0.396f;
            dl.LifhtOffsetDenesity = 0.588f;
            Debug.Log("[Orig] DrawLeaf: 材质 " + dl.mats.Count + " 个, 密度 " + dl.LeafDenesity + ", 尺寸 " + dl.LeafSize);

            // 地面 + 光 + 相机（只为了让画面能看）
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.transform.localScale = new Vector3(8, 1, 8);
            ground.GetComponent<MeshRenderer>().sharedMaterial = new Material(Shader.Find("Custom/ToonLit"));

            var lg = new GameObject("Sun").AddComponent<Light>();
            lg.type = LightType.Directional;
            lg.intensity = 1.3f;
            lg.shadows = LightShadows.Soft;
            lg.transform.rotation = Quaternion.Euler(55f, -35f, 0f);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.55f, 0.62f, 0.55f);

            // 取景
            Bounds tb = new Bounds(Vector3.zero, Vector3.one);
            bool hasB = false;
            foreach (var mf in inst.GetComponentsInChildren<MeshFilter>())
                if (mf.name.Contains("树叶") && mf.sharedMesh != null)
                {
                    tb = new Bounds(mf.transform.TransformPoint(mf.sharedMesh.bounds.center), mf.sharedMesh.bounds.size);
                    hasB = true; break;
                }

            var camGo = new GameObject("Cam");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            float ext = hasB ? Mathf.Max(tb.extents.x, tb.extents.y) * 1.4f : 5f;
            cam.orthographicSize = ext;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.70f, 0.78f, 0.80f);
            cam.transform.position = (hasB ? tb.center : Vector3.zero) + new Vector3(0, ext * 0.15f, -ext * 3.2f);
            cam.transform.LookAt(hasB ? tb.center : Vector3.zero);
            cam.farClipPlane = 200f;

            EditorSceneManager.SaveScene(scene, "Assets/Scenes/Test/ProjectOriginal.unity");

            // 渲染
            var rt = new RenderTexture(512, 512, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt;
            dl.InitLeaf();
            var mi = typeof(DrawLeaf).GetMethod("DrawLeafs", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            for (int i = 0; i < 3; i++) { if (mi != null) mi.Invoke(dl, null); cam.Render(); }
            RenderTexture.active = rt;
            var shot = new Texture2D(512, 512, TextureFormat.RGB24, false);
            shot.ReadPixels(new Rect(0, 0, 512, 512), 0, 0);
            shot.Apply();
            RenderTexture.active = null;
            cam.targetTexture = null;
            Directory.CreateDirectory("Shots");
            File.WriteAllBytes(Path.Combine(Directory.GetCurrentDirectory(), "Shots/project_original.png"), shot.EncodeToPNG());
            Object.DestroyImmediate(shot);
            rt.Release();
            Debug.Log("[Orig] 已保存 Shots/project_original.png");
            EditorApplication.Exit(0);
        }
    }
}
