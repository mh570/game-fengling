using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace T3ssDemo
{
    /// <summary>分析工程自带的 树叶 形状网格：到底几个球、球心在哪、法线怎么分布。</summary>
    public static class AnalyzeShapeMesh
    {
        [MenuItem("T3SS/分析树叶网格")]
        public static void Run()
        {
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>("Assets/Imported/Model/model.fbx");
            Mesh shape = null;
            // 从 fbx 的子资产里找
            var all = AssetDatabase.LoadAllAssetsAtPath("Assets/Imported/Model/model.fbx");
            foreach (var o in all)
            {
                var m = o as Mesh;
                if (m != null && m.name.Contains("树叶")) { shape = m; break; }
            }
            if (shape == null)
            {
                // 用 MeshFilter 找
                var go = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Imported/Model/model.fbx");
                if (go != null)
                    foreach (var mf in go.GetComponentsInChildren<MeshFilter>())
                        if (mf.sharedMesh != null && mf.sharedMesh.name.Contains("树叶")) { shape = mf.sharedMesh; break; }
            }
            if (shape == null) { Debug.LogError("[Ana] 找不到 树叶 网格"); EditorApplication.Exit(1); return; }

            var vs = shape.vertices;
            var ns = shape.normals;
            var b = shape.bounds;
            Debug.Log("[Ana] ===== 树叶 网格分析 =====");
            Debug.Log("[Ana] 顶点数=" + vs.Length + " 三角形=" + shape.triangles.Length / 3);
            Debug.Log("[Ana] 包围盒 size=" + b.size + " center=" + b.center);

            // 1) 按「到中心的距离」统计 → 看是不是单个球
            var dists = new List<float>();
            foreach (var v in vs) dists.Add((v - b.center).magnitude);
            dists.Sort();
            Debug.Log("[Ana] 距中心距离: min=" + dists[0].ToString("F3") +
                      " 25%=" + dists[dists.Count / 4].ToString("F3") +
                      " 50%=" + dists[dists.Count / 2].ToString("F3") +
                      " 75%=" + dists[dists.Count * 3 / 4].ToString("F3") +
                      " max=" + dists[dists.Count - 1].ToString("F3"));

            // 2) 法线 vs 「从中心发散方向」的夹角 → 判断法线是不是整球面
            float sumAng = 0; int n = 0;
            for (int i = 0; i < vs.Length; i++)
            {
                var radial = (vs[i] - b.center).normalized;
                float dot = Vector3.Dot(ns[i], radial);
                sumAng += Mathf.Acos(Mathf.Clamp(dot, -1f, 1f)) * Mathf.Rad2Deg;
                n++;
            }
            Debug.Log("[Ana] 法线 vs 径向 平均夹角 = " + (sumAng / n).ToString("F1") + " 度");
            Debug.Log("[Ana]   （0°=完全球形法线；偏离大=每球独立法线或原始法线）");

            // 3) 找球心：用顶点位置聚类（简单网格法）
            //    把空间划分成格子，统计每格里顶点密度峰值
            float cell = b.size.x / 12f;
            var grid = new Dictionary<string, int>();
            foreach (var v in vs)
            {
                string k = Mathf.FloorToInt(v.x / cell) + "," + Mathf.FloorToInt(v.y / cell) + "," + Mathf.FloorToInt(v.z / cell);
                if (!grid.ContainsKey(k)) grid[k] = 0;
                grid[k]++;
            }
            var list = new List<KeyValuePair<string, int>>(grid);
            list.Sort((p, q) => q.Value.CompareTo(p.Value));
            Debug.Log("[Ana] 顶点最密的 8 个格子（可能是球心区域）：");
            for (int i = 0; i < Mathf.Min(8, list.Count); i++)
            {
                var parts = list[i].Key.Split(',');
                var c = new Vector3(
                    (int.Parse(parts[0]) + 0.5f) * cell,
                    (int.Parse(parts[1]) + 0.5f) * cell,
                    (int.Parse(parts[2]) + 0.5f) * cell);
                Debug.Log("[Ana]   #" + i + " 中心≈" + c + "  顶点数=" + list[i].Value);
            }

            // 4) 顶点法线的 Y 分量分布（看上下渐变范围）
            int up = 0, down = 0, mid = 0;
            foreach (var nv in ns)
            {
                if (nv.y > 0.5f) up++;
                else if (nv.y < -0.3f) down++;
                else mid++;
            }
            Debug.Log("[Ana] 法线 Y: 朝上(>0.5)=" + up + " 朝下(<-0.3)=" + down + " 中间=" + mid);

            // 5) 导出前 20 个顶点 + 法线
            Debug.Log("[Ana] 前 10 个 (顶点 → 法线):");
            for (int i = 0; i < Mathf.Min(10, vs.Length); i++)
                Debug.Log("[Ana]   " + vs[i].ToString("F3") + " → " + ns[i].ToString("F3"));

            // 6) 导出 OBJ 方便外部看
            var sb = new System.Text.StringBuilder();
            foreach (var v in vs) sb.AppendLine("v " + v.x + " " + v.y + " " + v.z);
            foreach (var v in ns) sb.AppendLine("vn " + v.x + " " + v.y + " " + v.z);
            var tr = shape.triangles;
            for (int i = 0; i < tr.Length; i += 3)
                sb.AppendLine("f " + (tr[i] + 1) + "//" + (tr[i] + 1) + " " + (tr[i + 1] + 1) + "//" + (tr[i + 1] + 1) + " " + (tr[i + 2] + 1) + "//" + (tr[i + 2] + 1));
            File.WriteAllText(Path.Combine(Directory.GetCurrentDirectory(), "Shots/shuye_shape.obj"), sb.ToString());
            Debug.Log("[Ana] 已导出 Shots/shuye_shape.obj");
            EditorApplication.Exit(0);
        }
    }
}
