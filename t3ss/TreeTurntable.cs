using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace T3ssDemo
{
    /// <summary>
    /// 单棵树多角度渲染（转台），用于分析树的质量。
    /// 渲染 6 个角度：0/60/120/180/240/300 度。
    /// </summary>
    public static class TreeTurntable
    {
        static readonly int[] Angles = { 0, 60, 120, 180, 240, 300 };

        [MenuItem("T3SS/单树转台")]
        public static void Run()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // ---- 叶片贴图
            Directory.CreateDirectory("Assets/Textures");
            var leafTex = TreeGenerator.MakeLeafTexture(32, 11);
            File.WriteAllBytes("Assets/Textures/LeafCluster2.png", leafTex.EncodeToPNG());
            AssetDatabase.ImportAsset("Assets/Textures/LeafCluster2.png", ImportAssetOptions.ForceSynchronousImport);
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Textures/LeafCluster2.png");
            if (tex != null)
            {
                var imp = (TextureImporter)AssetImporter.GetAtPath("Assets/Textures/LeafCluster2.png");
                if (imp != null)
                {
                    imp.textureType = TextureImporterType.Default;
                    imp.alphaIsTransparency = true;
                    imp.filterMode = FilterMode.Point;
                    imp.mipmapEnabled = false;
                    imp.SaveAndReimport();
                }
                tex = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Textures/LeafCluster2.png");
            }

            // ---- 材质
            var shader = Shader.Find("Custom/ToonFoliage");
            if (shader == null) { Debug.LogError("[TT] 缺 ToonFoliage"); EditorApplication.Exit(1); return; }
            var mat = new Material(shader) { name = "MAT_TT" };
            if (tex != null) mat.SetTexture("_MainTex", tex);
            mat.SetFloat("_Cutoff", 0.4f);
            mat.SetFloat("_Cuts", 16f);
            mat.SetFloat("_DitherStrength", 0f);
            mat.SetColor("_BaseColor", Color.white);
            mat.SetColor("_HighlightColor", new Color(0.74f, 0.82f, 0.64f));   // 发白
            mat.SetColor("_ShadowColor", new Color(0.46f, 0.58f, 0.46f));   // 暗部也提亮（低对比）

            // ---- 生成一棵树
            var opt = new TreeGenerator.Options
            {
                Height = 5.0f,
                LeafCount = 7000,
                LeafSize = 0.16f,
                Seed = 4242,
            };
            var mesh = TreeGenerator.Build(opt, out int sv, out int lv);
            Directory.CreateDirectory("Assets/Generated");
            string path = "Assets/Generated/TurntableTree.asset";
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(mesh, path);
            var saved = AssetDatabase.LoadAssetAtPath<Mesh>(path);

            var tree = new GameObject("Tree");
            tree.AddComponent<MeshFilter>().sharedMesh = saved;
            var mr = tree.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;

            // 地面
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.transform.localScale = new Vector3(3, 1, 3);
            var toon = Shader.Find("Custom/ToonLit");
            if (toon != null)
            {
                var gm = new Material(toon);
                gm.SetColor("_BaseColor", new Color(0.70f, 0.78f, 0.50f));
                gm.SetColor("_Color2", new Color(0.56f, 0.68f, 0.38f));
                gm.SetColor("_Color3", new Color(0.50f, 0.62f, 0.34f));
                ground.GetComponent<MeshRenderer>().sharedMaterial = gm;
            }

            // 光
            var lg = new GameObject("Sun").AddComponent<Light>();
            lg.type = LightType.Directional;
            lg.intensity = 1.5f;
            lg.color = new Color(1f, 0.96f, 0.85f);
            lg.shadows = LightShadows.Soft;
            lg.shadowStrength = 0.5f;
            lg.transform.rotation = Quaternion.Euler(55f, -35f, 0f);
            // 大气透视：雾让远处发白
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogDensity = 0.055f;
            RenderSettings.fogColor = new Color(0.80f, 0.86f, 0.88f);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.50f, 0.56f, 0.54f);

            // 相机（环绕）
            var camGo = new GameObject("Cam");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = 4.2f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.72f, 0.80f, 0.86f);
            cam.farClipPlane = 100f;

            var rt = new RenderTexture(512, 512, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt;
            Directory.CreateDirectory("Shots/Turntable");

            for (int i = 0; i < Angles.Length; i++)
            {
                float a = Angles[i];
                // 相机放在树前方，绕 Y 轴旋转
                var dir = Quaternion.Euler(0f, a, 0f) * new Vector3(0f, 0.42f, -1f);
                cam.transform.position = new Vector3(0f, 2.0f, 0f) + dir.normalized * 14f;
                cam.transform.LookAt(new Vector3(0f, 2.3f, 0f));

                cam.Render();
                cam.Render();
                RenderTexture.active = rt;
                var shot = new Texture2D(512, 512, TextureFormat.RGB24, false);
                shot.ReadPixels(new Rect(0, 0, 512, 512), 0, 0);
                shot.Apply();
                RenderTexture.active = null;

                File.WriteAllBytes(
                    Path.Combine(Directory.GetCurrentDirectory(), $"Shots/Turntable/angle_{a:000}.png"),
                    shot.EncodeToPNG());
                Object.DestroyImmediate(shot);
                Debug.Log("[TT] 角度 " + a + " 完成");
            }

            cam.targetTexture = null;
            rt.Release();
            Debug.Log("[TT] 壳顶点=" + saved.vertexCount + "  叶片=" + lv);
            EditorApplication.Exit(0);
        }
    }
}
