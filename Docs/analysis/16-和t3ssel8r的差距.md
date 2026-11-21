# 和 t3ssel8r 的差距分析

**日期**：2026-11-22
**结论**：**影子是"不像"的第一原因**，不是树形、不是颜色。

---

# 一、我们的树**完全不投影**

## 原因

`ToonFoliage.shader` 里**没有可用的 ShadowCaster pass**。

历史：
1. 第一版加了 ShadowCaster，编译报 `ApplyShadowBias` 未定义（缺 `Shadows.hlsl`）
2. 删掉 `Shadows.hlsl` 后报 `SampleBias: no matching 0 parameter intrinsic method`
3. 于是**把整个 ShadowCaster pass 删了** → 树一个影子都没有

## 现在尝试修复时遇到的错误

```
Shader error: 'SampleBias': no matching 0 parameter intrinsic method
at ToonFoliage.shader(125)
Shader error: 'SampleLevel': no matching 0 parameter intrinsic method
at ToonFoliage.shader(125)
```

第 125 行 = `clip(SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, V.uv).a - _Cutoff);`

**试过**：`SAMPLE_TEXTURE2D` → `SAMPLE_TEXTURE2D_LOD(..., 0)`，错误从 `SampleBias` 变成 `SampleLevel`，
说明**宏展开本身有问题**，不是采样方式。

## 可能的原因（下次排查方向）

| 猜测 | 验证方法 |
|---|---|
| **ShadowCaster pass 需要 `#pragma multi_compile_shadowcaster`** | 加上试试 |
| 需要在 `CBUFFER_START(UnityPerMaterial)` 里声明属性 | 参照 URP 官方 `ShadowCasterPass.hlsl` |
| 应该直接用 URP 内置的 `#include ".../ShadowCasterPass.hlsl"` | 最标准的做法 |
| `TEXTURE2D/SAMPLER` 宏在某些 pass 上下文里不展开 | 改用 `sampler2D _MainTex; tex2D(...)` 老式写法 |

---

# 二、系统性差距对照表

| # | t3ssel8r | 我们 | 严重度 |
|---|---|---|---|
| **1** | **树在地上投下明确暗影** | ❌ **完全没有** | ★★★ |
| **2** | 树冠内部有明暗层次（叶簇各自受光） | 🟡 球形法线让整冠变一个光滑球 | ★★ |
| **3** | 地面明暗 = 影子形状 | 🟡 只有噪声斑块 | ★★ |
| **4** | 冷绿、高对比 | 🟡 黄绿、低对比 | ★ |
| **5** | GodRay | ❌ | ★★ |
| **6** | 树是多棵变体、形状各异 | 🟡 30 棵同款（只有随机旋转） | ★ |

---

# 三、为什么影子这么关键

对比两张图：

| 有影子 | 没影子 |
|---|---|
| 树"坐"在地上，有重量感 | 树"浮"在背景上 |
| 地面有形状变化 | 地面是均匀色块 |
| 能看出光照方向 | 光照方向不可读 |
| 视觉上"有空间" | 视觉上"是贴纸" |

**没有影子的森林，不管树做得多好，都会像玩具。**

---

# 四、下一步（按优先级）

| 序 | 动作 | 说明 |
|---|---|---|
| **1** | **修 ShadowCaster pass** | 用 URP 官方 `ShadowCasterPass.hlsl` 的写法，不要自己手写 |
| 2 | 树冠内部层次 | 球形法线 blend 从 0.55 降到 0.35，让叶簇各自有明暗 |
| 3 | 色调转冷 | `_HighlightColor` 偏冷绿，`_ShadowColor` 更深 |
| 4 | GodRay | 视频 03 的主题 |
| 5 | 树的变体 | 每个种子生成不同形状，避免"复制粘贴"感 |

---

# 五、参考：URP 官方 ShadowCaster 写法

正确的做法是**不要手写**，而是：

```hlsl
// ShadowCaster pass
#pragma vertex ShadowPassVertex
#pragma fragment ShadowPassFragment
#pragma multi_compile_instancing
#pragma multi_compile _ LOD_FADE_CROSSFADE
#pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

#include "Packages/com.unity.render-pipelines.universal/Shaders/ShadowCasterPass.hlsl"
```

需要在 `ShadowCasterPass.hlsl` 之前定义好：
- `Attributes` / `Varyings` 结构
- `_BaseMap` / `_BaseColor` / `_Cutoff` / `_AlphaClip`
- `VertexPositionInputs` 相关

**参考参数**：
- [Unity URP ShadowCasterPass.hlsl](https://github.com/Unity-Technologies/Graphics/blob/master/Packages/com.unity.render-pipelines.universal/Shaders/ShadowCasterPass.hlsl)
- 或者直接看本仓库的 `GrassBlade.shader` —— **它有 ShadowCaster 吗？**（如果草能投影，抄它的写法）
