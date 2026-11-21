# 地形 shader 实现规格

**日期**：2026-11-21
**问题**：Unity Terrain 用普通物体的顶点路径渲染时，**地形块会整块错位**（"悬浮方块"）
**已验证**：不是 LOD（`heightmapPixelError=1` 无变化）、不是背面剔除（`Cull Off` 无变化）

---

# 一、根因

Unity Terrain 的地表是**按 patch 实例化绘制**的：

- 顶点坐标带着 **per-instance 的 patch 偏移**
- 普通 shader 只做 `mul(unity_ObjectToWorld, v.vertex)`，**拿不到那层偏移**
- 结果：每个 patch 被画到错误位置 → 看起来像悬浮的方块、阶梯错位

**旁证**：设 `terrain.drawInstanced = true` 时地形**完全消失**——因为我们的 shader 没有 instancing 宏，顶点全废。

---

# 二、URP 的做法（照抄）

参考：
- [URP TerrainLit.shader](https://github.com/Unity-Technologies/Graphics/blob/513e413c/Packages/com.unity.render-pipelines.universal/Shaders/Terrain/TerrainLit.shader)
- [TerrainLitInput.hlsl 里的 TerrainInstancing](https://gitea.speldesign.uu.se/2025-AgileMethods/GOATtheDiver/blame/commit/e4ff5fbe54d830f1e889ee5d4b04d463720caf37/Library/PackageCache/com.unity.render-pipelines.universal/Shaders/Terrain/TerrainLitInput.hlsl)

## 2.1 必需的 pragma

```hlsl
#pragma multi_compile_instancing
#pragma instancing_options assumeuniformscaling nomatrices nolightprobe nolightmap
```

## 2.2 顶点函数结构

```hlsl
Varyings Vert(Attributes input)
{
    Varyings o;

#ifdef UNITY_INSTANCING_ENABLED
    // 地形 patch 的 per-instance 偏移必须在这里处理
    TerrainInstancing(input.positionOS, input.normalOS, input.uv);
#endif

    o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
    o.positionWS  = TransformObjectToWorld(input.positionOS.xyz);
    o.normalWS    = TransformObjectToWorldNormal(input.normalOS);
    return o;
}
```

`TerrainInstancing` 在 URP 里定义于 `TerrainLitInput.hlsl`，核心是读 `unity_InstanceParams`（patch 的 xy 偏移 + zw 缩放）并重构 `positionOS.xz`。

## 2.3 我们的简化版（如果不想依赖 URP 的 include）

可以自己写等价实现：把 `unity_ObjectToWorld` 的平移部分当 patch 偏移，或者直接从 `unity_InstanceParams` 取值。

**但最稳的做法是照抄 URP 的 `TerrainInstancing`**——那是官方验证过的。

---

# 三、我们的落地方式

## 方案 A（推荐）：新建 `Fengling/Terrain` shader

- 复制 `FenglingLit` 的光照/阴影/云影逻辑
- 顶点路径换成带 `TerrainInstancing` 的
- 保留 `_TerrainMode` 的高度/坡度分带
- 材质 `Cull Off`（地形双面）

## 方案 B（快捷）：给 `FenglingLit` 加 instancing 宏

- 加 `#pragma multi_compile_instancing` + `TerrainInstancing` 调用
- 好处：不用新建文件
- 坏处：给所有物体都加了实例化开销

**建议 A**——地形是独立的渲染路径，值得单独一个 shader。

---

# 四、验收

| 项 | 标准 |
|---|---|
| 地形块不错位 | 岛屿轮廓连续，没有悬浮方块 |
| 高度分带 | 岩/草/沙 正常 |
| 光照 | 与现有物体一致（同一套太阳/阴影/云影） |
| 性能 | 不因 instancing 明显变慢 |

---

# 五、已知的相关坑

| 坑 | 说明 |
|---|---|
| `terrain.drawInstanced = true` + 无 instancing shader | 地形**完全消失** |
| `Cull Back` + 地形裙边 | 曾怀疑导致镂空，实测**不是** |
| `heightmapPixelError` | 调到 1 也没解决错位，说明不是 LOD |
