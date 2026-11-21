using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace T3ssDemo
{
    /// <summary>
    /// 在编辑器里画草：GrassSpawner 用的是 Graphics.DrawMeshInstanced，
    /// 只在 Play 模式的 Update 里跑。这里手动算矩阵 + 手动调用，然后渲染。
    /// </summary>
    public static class GrassRenderTest
    {
        static void TrySetColor(Material m, string prop, Color c)
        {
            if (m != null && m.HasProperty(prop)) m.SetColor(prop, c);
        }

        [MenuItem("T3SS/画草并渲染")]
        public static void Run()
        {
            var scene = EditorSceneManager.OpenScene("Assets/Scenes/Test/ShaderTest_Trees.unity", OpenSceneMode.Single);

            // ---- 找 GrassSpawner（场景里已有）
            var spawner = Object.FindFirstObjectByType<GrassSpawner>();
            if (spawner == null)
            {
                Debug.LogError("[Grass] 场景里没有 GrassSpawner");
                EditorApplication.Exit(1);
                return;
            }
            Debug.Log("[Grass] 找到 GrassSpawner: count=" + spawner.grassCount +
                      " mesh=" + (spawner.grassMesh != null) +
                      " mat=" + (spawner.grassMaterial != null) +
                      " area=" + spawner.spawnArea);

            if (spawner.grassMesh == null || spawner.grassMaterial == null)
            {
                Debug.LogError("[Grass] mesh 或 material 为空");
                EditorApplication.Exit(1);
                return;
            }

            // ---- 手动生成实例矩阵（表面撒点）
            int count = 35000;
            var mats = new Matrix4x4[count];
            var rng = new System.Random(2026);
            // 覆盖撒点范围：聚焦相机可见区，密度提到 ~30 株/㎡
            var b = new Bounds(new Vector3(0f, 0f, 0f), new Vector3(16f, 4f, 16f));
            int placed = 0;
            for (int i = 0; i < count * 3 && placed < count; i++)
            {
                float x = b.center.x + (float)(rng.NextDouble() - 0.5) * b.size.x;
                float z = b.center.z + (float)(rng.NextDouble() - 0.5) * b.size.z;
                float y = 0f;

                // 射线打地面：只要朝上的面，且高度接近地面（排除方块顶）
                if (!Physics.Raycast(new Vector3(x, b.center.y + 50f, z), Vector3.down, out var hit, 200f))
                    continue;
                if (hit.normal.y < 0.9f) continue;
                if (Mathf.Abs(hit.point.y) > 0.15f) continue;
                y = hit.point.y;

                float h = Mathf.Lerp(spawner.minHeight, spawner.maxHeight, (float)rng.NextDouble());
                float w = h * 0.75f;
                var rot = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
                mats[placed] = Matrix4x4.TRS(new Vector3(x, y, z), rot, new Vector3(w, h, w));
                placed++;
            }
            Debug.Log("[Grass] 生成 " + placed + " 个实例矩阵");

            // ---- 草颜色：把 patch 系统的颜色改成绿色（不继承地形的黄绿）
            var gm = spawner.grassMaterial;
            TrySetColor(gm, "_Color1", new Color(0.34f, 0.52f, 0.24f));
            TrySetColor(gm, "_Color2", new Color(0.42f, 0.60f, 0.28f));
            TrySetColor(gm, "_Color3", new Color(0.26f, 0.42f, 0.18f));
            TrySetColor(gm, "_BaseColor", new Color(0.36f, 0.54f, 0.26f));

            // ---- 手动绘制（分块，DrawMeshInstanced 上限 1023）
            var mpb = new MaterialPropertyBlock();
            const int batch = 1023;
            for (int i = 0; i < placed; i += batch)
            {
                int n = Mathf.Min(batch, placed - i);
                var slice = new Matrix4x4[n];
                System.Array.Copy(mats, i, slice, 0, n);
                Graphics.DrawMeshInstanced(spawner.grassMesh, 0, spawner.grassMaterial, slice, n, mpb,
                    UnityEngine.Rendering.ShadowCastingMode.Off, false);
            }

            // ---- 渲染
            Camera cam = null;
            foreach (var c in Object.FindObjectsByType<Camera>(FindObjectsSortMode.None))
                if (c.cameraType == CameraType.Game && c.targetTexture == null) { cam = c; break; }
            if (cam == null) cam = Object.FindFirstObjectByType<Camera>();
            if (cam == null) { Debug.LogError("[Grass] 没相机"); EditorApplication.Exit(1); return; }

            var rt = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt;
            cam.Render();
            cam.Render();
            RenderTexture.active = rt;
            var shot = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            shot.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
            shot.Apply();
            RenderTexture.active = null;
            cam.targetTexture = null;

            Directory.CreateDirectory("Shots");
            File.WriteAllBytes(Path.Combine(Directory.GetCurrentDirectory(), "Shots/grass_test.png"), shot.EncodeToPNG());
            Object.DestroyImmediate(shot);
            rt.Release();

            Debug.Log("[Grass] 已保存 Shots/grass_test.png");
            EditorApplication.Exit(0);
        }
    }
}
