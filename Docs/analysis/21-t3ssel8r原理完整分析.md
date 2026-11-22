# t3ssel8r 原理完整分析 · 两根本问题论

**日期**：2026-11-22
**来源**：t3ssel8r 的 YouTube 视频论文 + 复刻者教程 + 我们工程实测
**目标**：把树的渲染逻辑彻底理清，给出可执行方案

---

# 一、核心理论：两个根本问题

## 1.1 原文引用

> "This tutorial covers **the two fundamental problems** every indie game developer faces when trying to make 3D look like pixel art: **the grid problem and the color problem**.
> We'll implement **point filtering** to preserve sharp edges and use **perceptual color space (OKLab)** to **quantize colors** the way your eyes actually see them."
>
> — [Red Giraffe: The Secret to Crisp 3D Pixel Art Rendering](https://www.gamesinprogress.com/indie-game-developers/red-giraffe/the-secret-to-crisp-3d-pixel-art-rendering-or-pixel-perfect)（明确引用 t3ssel8r）

## 1.2 两个问题

| # | 问题 | 本质 | 解法 |
|---|---|---|---|
| **①** | **网格问题**（Grid Problem） | 3D 三角形的边缘是亚像素的连续曲线，放大后是「糊的斜线」，不是「方块」 | **低分辨率渲染 + 点采样放大** |
| **②** | **颜色问题**（Color Problem） | 3D 的光照是连续渐变，看起来「浑」（muddy），不像像素画的有限调色板 | **在感知色彩空间（OKLab）量化颜色** |

## 1.3 时间轴（教程结构）

```
00:00 Intro
01:31 Downsampling    ← 解决问题①
05:31 Quantization    ← 解决问题②（OKLab）
11:03 Outro
```

---

# 二、为什么必须用 OKLab 而不是 RGB

## 2.1 RGB 量化的缺陷

```hlsl
// ❌ 错误做法（简单 RGB 量化）
c = floor(c * steps + 0.5) / steps;
```

**问题**：RGB 空间**不是感知均匀的**。
- 人眼对绿色最敏感，对蓝色最不敏感
- RGB 中「等距」的两个值，人眼看出来的差异不相等
- 结果：**量化后偏色、发脏、色阶不均匀**

## 2.2 OKLab 是感知均匀色彩空间

```
RGB → LinearRGB → LMS → OKLab → 量化 → 逆变换 → RGB
```

**OKLab 的特性**：数值上等距 = 感知上等距。

**量化后的效果**：色阶之间看起来**同样程度的差异**，不会出现「有的台阶很粗、有的很细」的偏色问题。

## 2.3 OKLab 转换公式（Björn Ottosson）

```hlsl
// Linear sRGB → OKLab
float3 LinearRGBToOKLab(float3 c)
{
    float l = 0.4122214708f*c.r + 0.5363325363f*c.g + 0.0514459929f*c.b;
    float m = 0.2119034982f*c.r + 0.6806995451f*c.g + 0.1073969566f*c.b;
    float s = 0.0883024619f*c.r + 0.2817188376f*c.g + 0.6299787005f*c.b;

    float l_ = pow(max(l, 0), 1.0/3.0);
    float m_ = pow(max(m, 0), 1.0/3.0);
    float s_ = pow(max(s, 0), 1.0/3.0);

    return float3(
        0.2104542553f*l_ + 0.7936177850f*m_ - 0.0040720468f*s_,
        1.9779984951f*l_ - 2.4285922050f*m_ + 0.4505937099f*s_,
        0.0259040371f*l_ + 0.7827717662f*m_ - 0.8086757660f*s_
    );
}

// OKLab → Linear sRGB
float3 OKLabToLinearRGB(float3 c)
{
    float l_ = c.x + 0.3963377774f*c.y + 0.2158037573f*c.z;
    float m_ = c.x - 0.1055613458f*c.y - 0.0638541728f*c.z;
    float s_ = c.x - 0.0894841775f*c.y - 1.2914855480f*c.z;

    float l = l_*l_*l_;
    float m = m_*m_*m_;
    float s = s_*s_*s_;

    return float3(
         4.0767416621f*l - 3.3077115913f*m + 0.2309699292f*s,
        -1.2684380046f*l + 2.6097574011f*m - 0.3413193965f*s,
        -0.0041960863f*l - 0.7034186147f*m + 1.7076147010f*s
    );
}

// 量化
float3 QuantizeOKLab(float3 srgb, float steps)
{
    float3 lin = SRGBToLinear(srgb);
    float3 lab = LinearRGBToOKLab(lin);
    // 只在 L（亮度）上量化，保留色相
    lab.x = floor(lab.x * steps + 0.5) / steps;
    float3 lin2 = OKLabToLinearRGB(lab);
    return LinearToSRGB(lin2);
}
```

**注意**：只在 **L（亮度）通道**量化，可以保留树冠的绿色色相不变，只让明暗变成台阶。

---

# 三、完整的 5 阶段管线

```
① Scene Render（全分辨率）
        ↓
② Downsample → 640×360（Point 采样）        ← 解决网格问题
        ↓
③ NormalLine Outline（内部res，1px 描边）    ← 工程自带
        ↓
④ OKLab Quantization（亮度量化到 4~8 级）   ← 解决颜色问题 ★
        ↓
⑤ Nearest Upscale → 1920×1080              ← 保持方块锐利
        ↓
输出
```

---

# 四、我们工程的现状（完整分析）

## 4.1 工程全部资产清点

| 类别 | 数量 | 关键内容 |
|---|---|---|
| **相关 C#** | **2 个** | `DrawLeaf.cs`、`NoramlLineRenderPassFeature.cs` |
| Shader | 5 个 | Grass / Leaf / NormalLIne / NormalTex / Unlit |
| 第三方包 | 1 个 | Cyanilux Custom Lighting（Toon Steps=3） |
| 场景 | 1 个 | SampleScene.unity 58MB（2297 草，**无树**） |

## 4.2 逐环节对照

| # | 环节 | 视频（t3ssel8r） | 工程 | 我们 | 状态 |
|---|---|---|---|---|---|
| 1 | 树几何 | 3 个 blobby + 扁球 | `树叶` 网格（3019顶点，5球） | 用工程的 | ✅ |
| 2 | 叶片摆放 | 顶点撒叶 | `DrawLeaf.cs`（44.3%密度） | 用工程的 | ✅ |
| 3 | 叶片着色 | 球面法线 + 渐变 | `Leaf.shadergraph`（**6帧平滑渐变**） | HLSL 重写 | ✅ |
| 4 | 树体着色 | 纯色 | `Unlit.shadergraph` | `Custom/FlatColor` | ✅ |
| 5 | 描边 | 法线 Roberts Cross | `NoramlLineRenderPassFeature` | Unity 6 适配版 | ✅ 编译通过 |
| 6 | 后期 | 未知 | Neutral + Bloom1 + Vignette0.25 | 曾加过，已移除 | ⚠️ |
| 7 | **降采样** | **有** | **❌ 无** | 验证过 | ⚠️ 未接入管线 |
| 8 | **OKLab 量化** | **有** | **❌ 无** | **❌ 未实现** | ❌ **核心缺失** |

## 4.3 关键证据：工程的渐变是平滑的

从 `Leaf.shadergraph` JSON 提取：
```
m_NumColorKeys = 6
key0: RGB(0.283, 0.298, 0.358) @ 0.203
key1: RGB(0.402, 0.424, 0.481) @ 0.406
key2: RGB(0.440, 0.466, 0.538) @ 0.624
key3: RGB(0.603, 0.645, 0.764) @ 0.832
key4: RGB(0.678, 0.727, 0.858) @ 0.965
key5: RGB(1.000, 1.000, 1.000) @ 1.000
```

**⇒ 6 个关键帧均匀分布 = 平滑渐变**
**⇒ 工程里做不出「深浅绿方块直接拼接」**

**所以用户观察的差距 2、3 不是参数问题，是工程缺了第 7、8 环节。**

---

# 五、四个差距的最终定性

| # | 视频特征 | 差距性质 | 解决方案 |
|---|---|---|---|
| **1** | **伞状半圆**，边缘破损 | **参数错** | 树冠非均匀缩放 (1.20, 0.68, 1.20) |
| **2** | **强烈像素马赛克** | **缺环节：降采样** | 640×360 + Point 放大 |
| **3** | **深浅绿方块拼接** | **缺环节：OKLab 量化** | `QuantizeOKLab()` |
| **4** | 树干可见、树枝隐没 | **参数错** | 树冠压扁后自然露出 |

**1、4 是参数（快）；2、3 是缺环节（要写代码）。**

---

# 六、执行方案

## 6.1 文件清单

| 文件 | 状态 | 内容 |
|---|---|---|
| `Assets/Shaders/OKLabQuantize.shader` | **待建** | OKLab 转换 + 亮度量化 |
| `Assets/Editor/PixelArtRenderFeature.cs` | **待建** | 降采样 + 量化 + 放大的 RendererFeature |
| `Assets/Editor/PixelatedTreeTest.cs` | **待建** | 完整管线渲染脚本 |
| `t3ss/ImportedTreeTest.cs` | ✅ 有 | e2a6e4f 基准 |
| `t3ss/LeafImported.shader` | ✅ 有 | 叶片 HLSL |
| `t3ss/FlatColor.shader` | ✅ 有 | 纯色 |
| `t3ss/NoramlLineRenderPassFeature_Unity6.cs` | ✅ 有 | 描边（编译通过） |

## 6.2 执行顺序（每步一验证）

| 阶段 | 内容 | 验证标准 |
|---|---|---|
| **A** | 确认 e2a6e4f 基准 | 采样 冠上≈145~159 / 冠中≈107 / 冠下≈92 |
| **B** | 降采样 640×360 + Point 放大 | 出现 3×3 方块 |
| **C** | **OKLab 量化**（steps 4~8，只量化 L） | 树冠只出现 4~8 种明度 |
| **D** | 树冠非均匀缩放 (1.20, 0.68, 1.20) | 宽高比 0.78 → 1.38 |
| **E** | 树干比例 | 树干占全树高 25~30% |
| **F** | 描边 `_Edge` 0.3 → 0.85 | 描边可见 |

## 6.3 每步的验证方法

| 类型 | 方法 |
|---|---|
| 编译 | `Unity.exe -batchmode -quit -projectPath <proj> -logFile <log>`，grep `error CS` |
| 渲染 | 检查 PNG 尺寸 + 是否生成 |
| 数值 | 采样树冠上/中/下 RGB + 亮度 |
| 色阶 | 统计树冠区域出现的不同明度值的数量 |

---

# 七、禁止事项（本次已踩的坑）

| 禁止 | 原因 |
|---|---|
| **一次改多个参数** | 曾同时改 7 项，坏了分不清哪个 |
| **`git show \| Set-Content` 导文件** | 吞换行 → 单行文件 → `CS1513 } expected` |
| **PowerShell `.Replace()` 改代码** | 找不到文本时**静默无操作** |
| **`>` 重定向写 shader** | 写成 UTF-16，Unity 读不了 |
| **改 e2a6e4f 已确认的参数** | 树参数是对的 |
| **用 `LifhtOffsetDenesity=0.588`** | e2a6e4f 用的是默认 0，改成 0.588 会让每片叶子颜色不同 |

---

# 八、结论

## 8.1 可行性论证

| 技术点 | 可行性 | 依据 |
|---|---|---|
| 降采样 + Point 放大 | ✅ **已验证** | 我们工程 `LowResTree.cs` 跑通 |
| **OKLab 量化** | ✅ **有标准公式** | Björn Ottosson 的 OKLab，公开数学 |
| 法线描边 | ✅ **已编译通过** | Unity 6 适配版 |
| 树冠变形 | ✅ **已实现** | `ShapeVariants.cs` 逆转置法线 |
| 树干比例 | ✅ **物理必然** | 树冠压扁后自然露出 |

## 8.2 最终判断

**用户说得对：这不是调参数，是逻辑问题。**

**工程的逻辑链是完整的（几何→撒叶→着色→描边→后期），但缺了两个环节：**
1. **降采样**（网格问题）
2. **OKLab 量化**（颜色问题）

**补上这两个环节，逻辑链才完整。**

## 8.3 t3ssel8r 的原理总结

> **3D 渲染 → 降采样到低分辨率（解决网格）→ 在 OKLab 感知空间量化颜色（解决颜色）→ 最近邻放大**
>
> **两个问题，两个解法，缺一不可。**

---

# 九、参考来源

- [Red Giraffe: The Secret to Crisp 3D Pixel Art Rendering](https://www.gamesinprogress.com/indie-game-developers/red-giraffe/the-secret-to-crisp-3d-pixel-art-rendering-or-pixel-perfect) — **两个根本问题 + OKLab 量化**
- [Red Giraffe: Drawing the Line on 3D Pixel Art](https://www.gamesinprogress.com/indie-game-developers/red-giraffe/drawing-the-line-on-3d-pixel-art-or-pixel-perfect) — gbuffer 深度+法线描边
- [bababuyyy/unity-isometric-pixel-pipeline](https://github.com/bababuyyy/unity-isometric-pixel-pipeline) — 5 步管线，Roberts Cross 描边
- [StackOverflow: 3D world in pixel-art form](https://stackoverflow.com/revisions/ab21f22a-e0e3-43eb-b690-e016e89bd427/view-source)
- [spritekiln: t3ssel8r 相机像素吸附](https://github.com/lucasjramos/spritekiln/blob/17ae0ea8d822b9bb543e539118516ae37d25b114/docs/design/10-thin-parts-and-stability.md)
- [t3ssel8r: Making an Animation for my 3D Pixel Art Game](https://www.gamesinprogress.com/indie-game-developers/t3ssel8r/making-an-animation-for-my-3d-pixel-art-game) — 他的原视频
- [80.lv: Tips on Convincing Animations for 3D Pixel Art Games](https://80.lv/articles/tips-on-convincing-animations-for-3d-pixel-art-games) — 他的动画方法论
- [SIGGRAPH 2014: Stylized Trees and Landscapes](https://history.siggraph.org/wp-content/uploads/2022/12/2014-Poster-97-Murphy_Stylized-Trees-and-Landscapes.pdf) — 3 blobby 树冠
- [Cyanilux/URP_ShaderGraphCustomLighting](https://github.com/Cyanilux/URP_ShaderGraphCustomLighting) — 工程内含的 Toon 分阶库
- Björn Ottosson, "A perceptual color space for image processing" (OKLab)
