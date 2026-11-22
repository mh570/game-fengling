using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace T3ssDemo
{
    /// <summary>
    /// 从工程的 树叶 网格反解出球心/半径。
    /// 原理：球面上 v = c + n*r  =>  c = v - n*r
    ///       任取两点：r = |v1-v2| / |n1-n2|
    ///       对大量点对算 r，出现最多的值就是真实半径。
    /// </summary>
    public static class FitSpheres
    {
        [MenuItem("T3SS/反解球心半径")]
        public static void Run()
        {
            Mesh shape = null;
            var all = AssetDatabase.LoadAllAssetsAtPath("Assets/Imported/Model/model.fbx");
            foreach (var o in all)
            {
                var m = o as Mesh;
                if (m != null && m.name.Contains("树叶")) { shape = m; break; }
            }
            if (shape == null)
            {
                var go = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Imported/Model/model.fbx");
                if (go != null)
                    foreach (var mf in go.GetComponentsInChildren<MeshFilter>())
                        if (mf.sharedMesh != null && mf.sharedMesh.name.Contains("树叶")) { shape = mf.sharedMesh; break; }
            }
            if (shape == null) { Debug.LogError("[Fit] 找不到网格"); EditorApplication.Exit(1); return; }

            var vs = shape.vertices;
            var ns = shape.normals;
            Debug.Log("[Fit] 顶点=" + vs.Length);

            // ---- 1) 采样点对，算半径，做直方图
            var rng = new System.Random(7);
            var samples = new List<float>();
            int trials = 60000;
            for (int t = 0; t < trials; t++)
            {
                int i = rng.Next(vs.Length);
                int j = rng.Next(vs.Length);
                if (i == j) continue;
                var dn = ns[i] - ns[j];
                float dnLen = dn.magnitude;
                if (dnLen < 0.15f) continue;          // 法线太接近，数值不稳
                float dv = (vs[i] - vs[j]).magnitude;
                float r = dv / dnLen;
                if (r > 0.05f && r < 20f) samples.Add(r);
            }
            samples.Sort();
            Debug.Log("[Fit] 采样 " + samples.Count + " 个半径值");
            Debug.Log("[Fit] 分位数 5%=" + samples[(int)(samples.Count * 0.05f)].ToString("F3") +
                      " 25%=" + samples[(int)(samples.Count * 0.25f)].ToString("F3") +
                      " 50%=" + samples[(int)(samples.Count * 0.50f)].ToString("F3") +
                      " 75%=" + samples[(int)(samples.Count * 0.75f)].ToString("F3") +
                      " 95%=" + samples[(int)(samples.Count * 0.95f)].ToString("F3"));

            // ---- 2) 直方图找峰值（真实半径）
            float lo = samples[(int)(samples.Count * 0.10f)];
            float hi = samples[(int)(samples.Count * 0.90f)];
            int bins = 60;
            var hist = new int[bins];
            foreach (var s in samples)
            {
                int b = (int)((s - lo) / (hi - lo) * bins);
                if (b >= 0 && b < bins) hist[b]++;
            }
            var peaks = new List<(float r, int cnt)>();
            for (int b = 1; b < bins - 1; b++)
                if (hist[b] > hist[b - 1] && hist[b] >= hist[b + 1] && hist[b] > samples.Count / bins)
                    peaks.Add((lo + (b + 0.5f) / bins * (hi - lo), hist[b]));
            peaks.Sort((a, b) => b.cnt.CompareTo(a.cnt));
            Debug.Log("[Fit] ===== 半径峰值（前 8） =====");
            for (int i = 0; i < Mathf.Min(8, peaks.Count); i++)
                Debug.Log("[Fit]   r=" + peaks[i].r.ToString("F4") + "  出现 " + peaks[i].cnt + " 次");

            // ---- 3) 对最强的 3 个半径，反解球心并聚类
            for (int pi = 0; pi < Mathf.Min(3, peaks.Count); pi++)
            {
                float r = peaks[pi].r;
                var centers = new List<Vector3>();
                // 对每个顶点，若法线确实"属于"这个半径的球（用邻居一致性不好判），
                // 就全部算一遍 c = v - n*r，然后聚类
                for (int i = 0; i < vs.Length; i += 3)
                    centers.Add(vs[i] - ns[i] * r);

                // 简单聚类：网格化统计
                float cell = Mathf.Max(0.25f, r * 0.5f);
                var grid = new Dictionary<string, (List<Vector3> pts, int cnt)>();
                foreach (var c in centers)
                {
                    string k = Mathf.FloorToInt(c.x / cell) + "," + Mathf.FloorToInt(c.y / cell) + "," + Mathf.FloorToInt(c.z / cell);
                    if (!grid.ContainsKey(k)) grid[k] = (new List<Vector3>(), 0);
                    var g = grid[k];
                    g.pts.Add(c); g.cnt++;
                    grid[k] = g;
                }
                var top = grid.OrderByDescending(p => p.Value.cnt).Take(6).ToList();
                Debug.Log("[Fit] === 半径 r=" + r.ToString("F4") + " 反解的球心（前 6 簇）===");
                foreach (var kv in top)
                {
                    Vector3 avg = Vector3.zero;
                    foreach (var p in kv.Value.pts) avg += p;
                    avg /= kv.Value.pts.Count;
                    Debug.Log("[Fit]   球心≈" + avg.ToString("F3") + "  顶点数=" + kv.Value.cnt);
                }
            }

            EditorApplication.Exit(0);
        }
    }
}
