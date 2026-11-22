using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace T3ssDemo
{
    /// <summary>
    /// 完全照抄工程文件（SampleScene.unity + SampleSceneProfile.asset）：
    ///   Camera: orthographic, size 6.02, near 0.3, far 100
    ///   Light:  Directional, white, intensity 2
    ///   Volume: Tonemapping=Neutral, Bloom=1, Vignette=0.25/0.4
    ///   + 低分辨率渲染 640x360（t3ssel8r 风格）
    /// </summary>
    public static class CopyProjectScene
    {
        [MenuItem("T3SS/照抄工程场景")]
        public static void Run()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // ===== Global Volume（照抄工程）=====
            var volGo = new GameObject("Global Volume");
            var vol = volGo.AddComponent<Volume>();
            vol.isGlobal = true;
            vol.priority = 0;
            vol.weight = 1f;
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            var tonemap = profile.Add<Tonemapping>(true);
            tonemap.mode.Override(TonemappingMode.Neutral);
            var bloom = profile.Add<Bloom>(true);
            bloom.intensity.Override(1f);
            var vig = profile.Add<Vignette>(true);
            vig.intensity.Override(0.25f);
            vig.smoothness.Override(0.4f);
            vol.sharedProfile = profile;
            Debug.Log("[CP] Volume: Tonemapping=Neutral, Bloom=1, Vignette=0.25/0.4");

            // ===== 树（照抄 e2a6e4f）=====
            var model = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Imported/Model/model.fbx");
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(model);
            inst.transform.position = Vector3.zero;

            Mesh shapeMesh = null;
            foreach (var mf in inst.GetComponentsInChildren<MeshFilter>())
                if (mf.sharedMesh != null && mf.name.Contains("树叶")) { shapeMesh = mf.sharedMesh; break; }

            foreach (var mr in inst.GetComponentsInChildren<MeshRenderer>())
            {
                bool keep = mr.name.Contains("树叶") || mr.name.Contains("树干");
                mr.enabled = keep;
                if (mr.name.Contains("树叶"))
                {
                    var sm = new Material(Shader.Find("Custom/FlatColor"));
                    sm.SetColor("_Color", new Color(0.250f, 0.320f, 0.195f));
                    mr.sharedMaterial = sm;
                }
                if (mr.name.Contains("树干"))
                {
                    var tm = new Material(Shader.Find("Custom/FlatColor"));
                    tm.SetColor("_Color", new Color(0.29f, 0.21f, 0.25f));
                    mr.sharedMaterial = tm;
                }
            }

            var leafShader = Shader.Find("Custom/LeafImported");
            var leafMesh = AssetDatabase.LoadAssetAtPath<Mesh>("Assets/Imported/Model/Leaf.mesh");
            var leafTex = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Imported/Tex/树叶.png");

            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.transform.localScale = new Vector3(30, 1, 30);
            var gmat = new Material(Shader.Find("Custom/ToonLit"));
            gmat.SetColor("_BaseColor", new Color(0.42f, 0.55f, 0.32f));
            gmat.SetColor("_Color2", new Color(0.40f, 0.52f, 0.30f));
            gmat.SetColor("_Color3", new Color(0.36f, 0.48f, 0.27f));
            ground.GetComponent<MeshRenderer>().sharedMaterial = gmat;

            // ===== Light（照抄工程：白色 intensity 2）=====
            var lg = new GameObject("Directional Light").AddComponent<Light>();
            lg.type = LightType.Directional;
            lg.color = Color.white;          // 工程 m_Color: (1,1,1)
            lg.intensity = 2f;               // 工程 m_Intensity: 2
            lg.shadows = LightShadows.Hard;   // 工程 m_SoftShadowsSupported: 0
            lg.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            lg.useColorTemperature = true;
            lg.colorTemperature = 5000f;     // 工程 5000K
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.55f, 0.62f, 0.55f);

            // DrawLeaf（照抄 e2a6e4f）
            var dl = inst.AddComponent<DrawLeaf>();
            dl.ShapeMesh = shapeMesh;
            dl.LeafMesh = leafMesh;
            var lm = new Material(leafShader);
            lm.enableInstancing = true;
            if (leafTex != null) lm.SetTexture("_MainTex", leafTex);
            lm.SetColor("_Color", Color.white);
            lm.SetVector("_SunDir", new Vector4(0.35f, 0.85f, 0.40f, 0f));
            lm.SetFloat("_LightOffsetScale", 0.396f);
            lm.SetColor("_GradDark",  new Color(0.333f, 0.384f, 0.299f));
            lm.SetColor("_GradMid1",  new Color(0.429f, 0.494f, 0.374f));
            lm.SetColor("_GradMid2",  new Color(0.559f, 0.644f, 0.487f));
            lm.SetColor("_GradLight", new Color(0.686f, 0.790f, 0.660f));
            dl.mats = new List<Material> { lm, lm };
            dl.LeafDenesity = 0.443f;
            dl.LeafSize = 1.449f;
            dl.LeafOffset = 0.356f;
            dl.LightOffset = 0.396f;
            dl.LifhtOffsetDenesity = 0.588f;

            // ===== Camera（照抄工程：ortho 6.02, near 0.3, far 100）=====
            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = 6.02f;    // 工程 m_Size: 6.02
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 100f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.70f, 0.78f, 0.80f);
            var data = camGo.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = true;   // ★ 启用后期（Tonemapping/Bloom/Vignette）

            var tb = shapeMesh.bounds;
            Vector3 lookAt = tb.center;
            cam.transform.position = lookAt + new Vector3(0f, 0f, -20f);   // 正交，距离不影响大小
            cam.transform.LookAt(lookAt);

            Directory.CreateDirectory("Shots/CopyProject");

            var resList = new (int w, int h, string name)[]
            {
                (640, 360, "low640"),
                (1920, 1080, "high1080"),
            };
            foreach (var r in resList)
            {
                var rt = new RenderTexture(r.w, r.h, 24, RenderTextureFormat.ARGB32);
                rt.filterMode = FilterMode.Point;
                cam.targetTexture = rt;
                dl.InitLeaf();
                var mi = typeof(DrawLeaf).GetMethod("DrawLeafs", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                for (int f = 0; f < 3; f++) { if (mi != null) mi.Invoke(dl, null); cam.Render(); }
                RenderTexture.active = rt;
                var shot = new Texture2D(r.w, r.h, TextureFormat.RGB24, false);
                shot.ReadPixels(new Rect(0, 0, r.w, r.h), 0, 0);
                shot.Apply();
                RenderTexture.active = null;
                cam.targetTexture = null;
                File.WriteAllBytes(Path.Combine(Directory.GetCurrentDirectory(), "Shots/CopyProject/" + r.name + ".png"), shot.EncodeToPNG());
                Object.DestroyImmediate(shot);
                rt.Release();
                Debug.Log("[CP] 渲染 " + r.name + " (" + r.w + "x" + r.h + ")");
            }

            Object.DestroyImmediate(dl);
            EditorApplication.Exit(0);
        }
    }
}
