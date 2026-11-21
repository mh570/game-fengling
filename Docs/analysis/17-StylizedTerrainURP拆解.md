# Stylized-Terrain-URP 地形 shader 拆解

**来源**：[BradFitz66/Stylized-Terrain-URP](https://github.com/BradFitz66/Stylized-Terrain-URP)（MIT，⭐35）
**已复制到**：`t3ss/stylized-terrain/`
**日期**：2026-11-22

> 仓库说明："A stylized terrain system for URP, **similar to the work of t3ssel8r**"

---

# 一、文件清单

| 文件 | 行数 | 作用 |
|---|---|---|
| `Terrain.shader` | 319 | 地形主 shader（入口 `STE/Terrain`） |
| `CustomLighting.hlsl` | 405 | **卡通光照核心**（主光 + 附加光 + 量化） |
| `Lighting.hlsl` | 219 | 3D Simplex/Perlin 噪声（给 `_RockNoise` 用） |
| `Grass.shader` | 275 | 草 |
| `Lit.hlsl` | 64 | 子图公共函数 |

---

# 二、Terrain.shader 的关键特性

## 2.1 参数

| 参数 | 作用 |
|---|---|
| `_MainTex` | 反照率 |
| `_RockColor` | 岩石色 |
| `_Diffuse` / `_ShadowColor` | 亮部/暗部 |
| `_DiffuseOffset` | 亮暗偏移（0.5 默认） |
| **`_LedgeTopThickness`** | **台阶/陡坡边缘高光厚度** |
| **`_LedgeBottomThickness`** | **台阶底部暗部厚度** |
| `_RockTextureTiling` | 岩石平铺 |
| **`_SpotLightBands`** | **聚光灯量化档数**（默认 4） |
| **`_PointLightBands`** | **点光量化档数**（默认 4） |
| `_Ground1..4` | **4 层地表贴图** |
| `_Layer1..4ColorVariationMultiplier` | **每层的颜色变化倍率** |
| **`_RockNoise`** | **3D 噪声贴图**（岩石不规则） |

## 2.2 我们缺的三个特性

### ① 灯光的衰减量化（我们没有）

`CustomLighting.hlsl:275`：
```hlsl
float ToonAttenuation(int lightIndex, float3 positionWS, float pointBands, float spotBands){
    ...
    (floor(spotAtten * spotBands) / spotBands) * maskSpotToRange :
    saturate(1.0 - floor(dist * pointBands) / pointBands);
}
```

**点光/聚光灯的衰减被 floor 量化成 N 档**，和主光的卡通分档统一。
我们现在只有主光分档，附加光还是平滑衰减。

### ② Ledge 边缘（我们没有）

`_LedgeTopThickness` / `_LedgeBottomThickness`：
在地形的**台阶/陡坡上沿加亮、下沿压暗**，形成"描边"式的立体感。
这是地形"看起来有厚度"的关键。

### ③ 4 层地表 + 每层颜色倍率（我们只有 1 层 + patch）

```
_Ground1..4        → 4 张地表贴图
_LayerNColorVariationMultiplier → 每层的颜色变化强度
_RockNoise (3D)    → 岩石的不规则变化
```

比我们的"单色 + 两层噪声 patch"丰富得多。

---

# 三、光照结构

`CustomLighting.hlsl` 的三层：

| 层 | 函数 | 说明 |
|---|---|---|
| 主光 | `MainLightToon` | 分档 + 阴影 |
| 附加光 | `AdditionalLightsToon` | **衰减量化**（`_PointLightBands`） |
| GI | `Subtractive GI` | 子图 |

---

# 四、怎么用（迁移步骤）

## 4.1 前提

这个 shader 是给 **Unity Terrain** 的（需要 `_Ground1..4` 层贴图）。
我们的项目现在是**平面 + 自建网格**。

## 4.2 两条路

| 方案 | 做法 | 代价 |
|---|---|---|
| **A** | 换成 Unity Terrain + 这个 shader | 要重做地形生成（Terrain 用高度图） |
| **B** | **只偷技术**：把光照量化、Ledge、4 层混合移植到我们的 `ToonLit` | 工作量小，效果可控 |

**建议 B**：我们的地形系统已经有高度/坡度数据，只需要把 shader 里的算法拿过来。

## 4.3 具体能移植的三段

### ① 附加光量化（直接抄）
```hlsl
// 移植到 ToonLit 的 AdditionalLights
float bandAtten = saturate(1.0 - floor(dist * _PointLightBands) / _PointLightBands);
```

### ② Ledge 边缘
用**地形坡度**判断：坡度 > 阈值的地方，按局部高度梯度在上下沿加亮/压暗。

### ③ 4 层混合
我们已经有顶点色，可以扩展成 4 层权重混合。

---

# 五、注意事项

- 依赖 URP 的 `Lighting.hlsl` / `Shadows.hlsl`
- `_RockNoise` 需要 3D 纹理（`sampler3D`），WebGL 不支持
- 需要 `_Ground1..4` 和 `_LayerN*` 参数由 Terrain 组件自动填充

---

# 六、仓库里没有的东西

| 内容 | 状态 |
|---|---|
| **树 / 植被模型** | ❌ **完全没有** |
| 叶片卡片 shader | ❌ 没有 |
| `NativeTrees` | 其实是**八叉树空间索引库**（Bart van de Sande），名字误导 |
| `Ica_Normal_Tools` | Burst 法线重算库（带角度阈值），可用来重算球形法线 |
