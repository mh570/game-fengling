# t3ssel8r 风格管线（Unity 6 URP）

**日期**：2026-11-22
**来源**：**[bababuyyy/unity-isometric-pixel-pipeline](https://github.com/bababuyyy/unity-isometric-pixel-pipeline)**（MIT）
**状态**：✅ **已跑通**（demo + 树测试）

> 这个仓库是**研究 t3ssel8r 后自己实现的完整管线**，不是 t3ssel8r 本人代码。

---

# 一、为什么用它

我们在自建 SRP 上折腾两天，反复卡在：
- glTF 导入器不认自建 SRP
- Terrain 与 SRP 冲突
- shader 全是自己写，没有参考

**这套是 URP + 现成实现 + 457 行文档。** 换成它等于站在别人肩膀上。

---

# 二、5 步管线（核心设计）

```
┌─────────┐   ┌──────────┐   ┌────────────┐   ┌─────────┐   ┌────────────┐
│ Scene   │──▶│Downsample│──▶│ Outline 1px│──▶│Composite│──▶│SharpUpscale│
│ (全分辨)│   │ (640×360)│   │  (640×360) │   │(640×360)│   │  (屏幕)    │
└─────────┘   └──────────┘   └────────────┘   └─────────┘   └────────────┘
```

**最关键的设计**：**描边在 640×360 上做**，所以不管屏幕多大，线永远是 **1 像素**。
这是"看起来像像素画"的根本原因。

| Pass | 作用 |
|---|---|
| 0 CopyColor | 拷全分辨率（Render Graph 不能同时读写同一 target） |
| 1 Downsample | 降到内部分辨率（默认 640×360，双线性） |
| 2 **Outline** | 深度 + 法线检测，**在内部分辨率上采样**，输出 RGBA 蒙版（A=强度） |
| 3 Composite | 蒙版叠加到低分辨率画面上 |
| 4 SharpUpscale | `fwidth` + `smoothstep` 放大，不模糊也不生硬 |

---

# 三、描边的两种边

| 类型 | 检测方式 |
|---|---|
| **Silhouette（轮廓）** | 深度不连续，8 邻域，**自适应阈值**（按表面相对相机的角度缩放） |
| **Crease（折边）** | **方向性对比**：`max(abs(d_top-d_bottom), abs(d_left-d_right))`，不是 `sum(1-dot(N,N))` |

**Crease 的方向性对比很关键**：曲面上对称邻居变化相同 → 对比≈0（不误判）；
真边缘一侧变化大 → 对比高（判为边）。

---

# 四、ToonLit

| 功能 | 说明 |
|---|---|
| **Toon Stepping** | `_Cuts` 参数，3 = 3 个色带 |
| **Patch System** | 世界空间噪声驱动的颜色变化（`_Color2`/`_Color3`）——**地形色斑就靠它** |
| **Cloud Shadows** | 全局滚动的噪波贴图调制光照 |
| **Bayer Dithering** | 4×4 有序抖动，打散色带边界 |
| **Palette System** | `albedo × lerp(ShadowColor, HighlightColor, lit)`，可选 |
| 4 passes | ForwardLit / ShadowCaster / DepthOnly / DepthNormals（SRP Batcher 兼容） |

---

# 五、GrassBlade（草）

| 功能 | 说明 |
|---|---|
| GPU Instancing | 每株一个 billboard quad，demo 里 **~35k 株** |
| **颜色继承地形** | 草片只有 alpha/形状，**颜色来自和地形同一套 patch 系统** ← 我们之前做错的 |
| 双层风 | 两道噪波，每株按世界位置采样 |
| 云影 | 按实例根位置采样 |
| Accent Sprites | 随机替换成花/装饰 |
| **只有 2 passes** | 故意不做 DepthOnly/DepthNormals，否则每株草都会描边（噪点） |

---

# 六、像素完美平移

低分辨率下移动相机会"像素游动"：

1. **Snap**：相机位置吸附到视空间的 texel 网格
2. **Compensate**：把吸附误差作为 UV 偏移传给 upscale shader

```hlsl
float2 uv = input.texcoord + _PixelPanOffset.xy;
```

---

# 七、为什么不 PBR

> 640×360 下 PBR 的平滑渐变会变成色带之间的噪点；
> 提高分辨率又失去像素感。**没有两全的甜蜜点。**

正确做法：**每个材质用手挑的纯色**，靠调色板而不是贴图。

---

# 八、我们的实测结果

## demo 管线（`ShaderTest.unity`）

![demo](images/t3ss/demo_pipeline.png)

✅ Toon 分档、黑色 1px 描边、地面色斑、像素化但边缘锐利

## 树测试（我们自己摆的）

![tree](images/t3ss/tree_test.png)

✅ 球形法线生效（冠部成平滑圆顶）、叶簇细节保留（blend 0.55）、阴影柔化
❌ **描边没出现** —— 因为用 `cam.Render()` 手动渲染**绕过了 URP 的 Renderer Feature**

**这验证了**：球形法线重映射 + ToonLit 的组合是对的，问题只在渲染路径。

---

# 九、踩过的坑

| 坑 | 解决 |
|---|---|
| 项目没有 gltfast，GLB 导入失败 | 往 `manifest.json` 加 `com.unity.cloud.gltfast` |
| `cam.Render()` 绕过 URP Renderer Feature | 必须用 URP 正常渲染路径（Play 模式 / `UniversalRenderPipeline.Render`） |
| 阴影死黑 | 加 `RenderSettings.ambientLight` + `light.shadowStrength = 0.55` |
| 球形法线 blend=1 太光滑 | 降到 **0.55**，保留叶簇凸起 |

---

# 十、下一步

| 序 | 动作 |
|---|---|
| **1** | 修渲染路径，让描边生效（用 URP 正常渲染） |
| 2 | 地形换成 `ToonLit` + patch 系统（替掉我们的顶点色） |
| 3 | 草用 `GrassBlade` + `GrassSpawner`，颜色继承地形 |
| 4 | 加云影 |
| 5 | GodRay（参考 David Holland 的 shell texturing） |

---

# 十一、注意事项

- **SSAO 必须关闭**（和 GPU 草冲突）
- Unity 6（6000.x）+ URP 17.x
- MIT 协议，商用无限制
