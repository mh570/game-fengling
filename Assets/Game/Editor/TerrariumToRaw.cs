using System.IO;
using UnityEditor;
using UnityEngine;

namespace Fengling.EditorTools
{
    /// <summary>
    /// 把 Terrarium 高程瓦片转成 Unity Terrain 可 Import Raw 的 16 位 RAW。
    ///
    /// 读的是 ffmpeg 转出的裸 RGB（<tile>.rgb），不走 Texture2D.LoadImage：
    ///   Terrarium 是数据不是颜色，LoadImage 的 sRGB→线性变换会破坏字节值，
    ///   导致高程出现 -16917 这类荒谬数字。
    ///
    /// Terrarium 编码：高程(米) = (R*256 + G + B/256) - 32768
    /// Unity RAW：16 位无符号小端，分辨率 2^n+1，0..65535 线性映射 Min..Max Height
    /// </summary>
    public static class TerrariumToRaw
    {
        private const int Zoom = 15;
        private const int X0 = 15822;
        private const int Y0 = 9951;
        private const int Cols = 5;
        private const int Rows = 5;
        private const int OutSize = 1025;

        [MenuItem("Fengling/Terrain/转换 Terrarium 为 RAW")]
        public static void Run()
        {
            string tileDir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "_terrain", "tiles"));
            if (!Directory.Exists(tileDir)) { Debug.LogError("[DEM] 无瓦片目录 " + tileDir); return; }

            int mw = Cols * 256, mh = Rows * 256;
            var heights = new float[mw * mh];
            int loaded = 0;

            for (int j = 0; j < Rows; j++)
            {
                for (int i = 0; i < Cols; i++)
                {
                    string p = Path.Combine(tileDir, Zoom + "_" + (X0 + i) + "_" + (Y0 + j) + ".rgb");
                    if (!File.Exists(p)) { Debug.LogWarning("[DEM] 缺瓦片 " + p); continue; }

                    byte[] bytes = File.ReadAllBytes(p);
                    if (bytes.Length < 256 * 256 * 3) { Debug.LogWarning("[DEM] 尺寸不对 " + p); continue; }

                    for (int y = 0; y < 256; y++)
                    {
                        for (int x = 0; x < 256; x++)
                        {
                            int si = (y * 256 + x) * 3;
                            float h = (bytes[si] * 256f + bytes[si + 1] + bytes[si + 2] / 256f) - 32768f;
                            int px = i * 256 + x;
                            int py = (Rows - 1 - j) * 256 + (255 - y);
                            heights[py * mw + px] = h;
                        }
                    }
                    loaded++;
                }
            }
            Debug.Log("[DEM] 载入瓦片 " + loaded + "/" + (Cols * Rows));

            var outH = new float[OutSize * OutSize];
            float minH = float.MaxValue, maxH = float.MinValue;

            for (int y = 0; y < OutSize; y++)
            {
                for (int x = 0; x < OutSize; x++)
                {
                    float fx = (x + 0.5f) / OutSize * mw - 0.5f;
                    float fy = (y + 0.5f) / OutSize * mh - 0.5f;
                    int x0 = Mathf.Clamp(Mathf.FloorToInt(fx), 0, mw - 1);
                    int y0 = Mathf.Clamp(Mathf.FloorToInt(fy), 0, mh - 1);
                    int x1 = Mathf.Min(x0 + 1, mw - 1);
                    int y1 = Mathf.Min(y0 + 1, mh - 1);
                    float tx = Mathf.Clamp01(fx - x0);
                    float ty = Mathf.Clamp01(fy - y0);

                    float a = Mathf.Lerp(heights[y0 * mw + x0], heights[y0 * mw + x1], tx);
                    float b = Mathf.Lerp(heights[y1 * mw + x0], heights[y1 * mw + x1], tx);
                    float h = Mathf.Lerp(a, b, ty);

                    // 钳制到物理合理范围：Terrarium 个别瓦片含哨兵值，会产生 -4000 这类离群点
                    h = Mathf.Clamp(h, -50f, 600f);
                    outH[y * OutSize + x] = h;
                    if (h < minH) minH = h;
                    if (h > maxH) maxH = h;
                }
            }

            string outDir = Path.Combine(Application.dataPath, "Resources", "Terrain");
            Directory.CreateDirectory(outDir);
            string rawPath = Path.Combine(outDir, "skye_2km6.raw");

            using (var fs = new FileStream(rawPath, FileMode.Create))
            using (var bw = new BinaryWriter(fs))
            {
                for (int i = 0; i < outH.Length; i++)
                    bw.Write((ushort)Mathf.RoundToInt(Mathf.Clamp01(Mathf.InverseLerp(minH, maxH, outH[i])) * 65535f));
            }

            int sea = 0;
            foreach (float h in outH) if (h <= 0.5f) sea++;

            Debug.Log(string.Format(
                "[DEM] 完成 {0}x{1}  高程 {2:F1} ~ {3:F1} m  海面 {4:P1}  文件 {5}",
                OutSize, OutSize, minH, maxH, (float)sea / outH.Length, rawPath));
            AssetDatabase.Refresh();
        }
    }
}
