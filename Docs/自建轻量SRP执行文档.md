# 自建轻量 SRP 执行文档

> 目标：离开 HDRP，自建一条只为"俯视/第一人称水域场景"服务的最小渲染管线，
> 以便水面能做平面反射、LOD、水下这些 HDRP 里塞不进去的东西。

---

## 0. 为什么离开 HDRP

现在的状态是「在 HDRP 里塞一个自包含 shader」。它有三个死结：

| 死结 | 后果 |
|---|---|
| 走 `SRPDefaultUnlit` 通道 | 拿不到 HDRP 的光照/阴影/后处理，一切自己算 |
| 没有管线层钩子 | 平面反射、水下套、深度预pass 都没地方插 |
| HDRP 材质依赖重 | 地形/石头/鱼都用 HDRP/Lit，离开就得换 |

而自建 SRP 后：**水面就是管线的一等公民**，反射 pass、水下 pass 都能直接排进队列。

代价：天空、体积云、Bloom、色彩分级这些 HDRP 白送的东西，要自己写或者砍掉。

---

## 1. 保留 / 丢弃清单

### 保留（与渲染管线无关）

```
Assets/Game/Water/Runtime/     全部逻辑（WaterModule / IWaterSurface / 模拟器）
Assets/Game/Water/Shaders/     水面 shader（HLSL 本来就是自包含的，几乎不用改）
Assets/Game/Environment/       CoastTerrain / FishSchool
Assets/Game/Player/            相机与移动
Assets/Game/Editor/            ScenePresets（改材质创建方式即可）
```

### 丢弃（HDRP 专属）

| 丢什么 | 替代 |
|---|---|
| `HDRP/Lit` 材质 | 自己写一个极简 `Fengling/Lit`（漫反射 + 主光 + 环境） |
| `Volume` + `PhysicallyBasedSky` / `HDRISky` | 天空盒直接由 camera clear 用 cubemap；环境光用 SH |
| `Exposure` / `Tonemapping` / `Bloom` | 后处理 pass（先只做 tonemap + bloom） |
| 反射探针 | 平面反射 pass 取代 |
| HDRP 阴影 | 第一版不做阴影，或只做主光的方向阴影 |

---

## 2. 分阶段

### 阶段 A：最小可跑 SRP（3–5 天）

```
RenderPipelineAsset (FenglingPipelineAsset)
└─ RenderPipeline (FenglingPipeline)
   └─ ForwardRenderer
      ├─ Cull
      ├─ DrawOpaque（主光 + 环境光）
      └─ DrawTransparent（水面）
```

**验收**
- [ ] 新建场景能渲染一个 Cube，光照方向正确
- [ ] 水面 shader 直接可用（不透明与透明两个队列都对）
- [ ] 帧率不低于当前 HDRP 版本

**这一阶段不改水面逻辑，只换渲染底座。**

### 阶段 B：平面反射（2–3 天）

```
水面 pass 之前：
  1. 用主相机关于 y = waterLevel 镜像得到 mirrorCamera
  2. mirrorCamera 渲染到 reflectionRT（半分辨率）
  3. 水面 shader 用屏幕坐标 + 反射矩阵采样 reflectionRT
```

**验收**
- [ ] 湖面能看到天空与远岸倒影
- [ ] 相机移动时倒影不抖、不闪
- [ ] 关掉反射 pass 后帧率明显上升（说明真的在花性能）

### 阶段 C：LOD clipmap 网格（2 天）

```
以相机为中心生成 3–4 级同心网格（每级边长翻倍，密度减半）
相邻层之间做缝合（避免裂缝）
```

**验收**
- [ ] 相机移动时网格跟随，看不到边界接缝
- [ ] 远处波形的密度变化不突兀
- [ ] 顶点数比现在的固定 128×128 少 30% 以上

### 阶段 D：泡沫重做（1–2 天）

```
foam = f(浪高, 波形梯度, 深度) × 噪声 × 生命周期
```

**验收**
- [ ] 岸线泡沫宽度不均匀、边缘破碎
- [ ] 浪尖有零星泡沫
- [ ] 泡沫会随时间生成与消散

### 阶段 E：后处理（2 天）

- Tonemap（ACES 近似）
- Bloom（可选，低分辨率降采样）
- 水下雾（相机在水下时全屏 tint + 距离雾）

**验收**
- [ ] 白天场景不过曝、不灰
- [ ] 入水后画面明显变青、变糊

### 阶段 F：水下渲染（3–5 天，可选）

- 折射（复制颜色缓冲 + 法线扰动采样）
- 焦散（预计算纹理投射）

---

## 3. 里程碑与提交

| 里程碑 | 内容 | 提交点 |
|---|---|---|
| M1 | 最小 SRP 能画东西 | `feat(srp): minimal forward pipeline` |
| M2 | 水面在新管线下正常 | `feat(srp): water surface on custom pipeline` |
| M3 | 平面反射 | `feat(srp): planar reflection` |
| M4 | LOD 网格 | `feat(srp): ocean clipmap LOD` |
| M5 | 泡沫重做 | `feat(water): procedural foam` |
| M6 | 后处理 | `feat(srp): tonemap + bloom + underwater fog` |

**每个里程碑必须：编译通过 + 场景能 Play + 提交推送。** 不达标不进入下一阶段。

---

## 4. 风险

| 风险 | 概率 | 对策 |
|---|---|---|
| 自建 SRP 功能缺失比想象中多（阴影、雾、透明排序） | **高** | 阶段 A 就只做"能看见东西"，其余后补 |
| 水面 shader 依赖了 HDRP 的宏（`unity_SpecCube0` 等） | 中 | 这些是 core 库的，SRP 下仍可用；有问题就改成自己的 uniform |
| 场景资产全部要重做材质 | 中 | 写一个 `HDRP/Lit → Fengling/Lit` 的批量替换工具 |
| 时间成本 | 高 | 若两周内没到 M3，考虑退回 HDRP + 手搓反射 |

---

## 5. 决策（已确认）

| 决策 | 结论 | 影响 |
|---|---|---|
| 阴影 | **完整阴影**（主光 + 点光/聚光） | 阶段 A 工作量 +30%，需要阴影贴图与阴影收集 pass |
| 天空 | **程序化天空盒**（渐变 + 云） | 多一个天空 pass，可调但需要自己写 |
| 旧场景 | **直接覆盖** | HDRP 六场景不再维护，仓库保持单一管线 |

### 完整阴影对阶段 A 的影响

原本阶段 A 只做「Cull → DrawOpaque → DrawTransparent」，现在必须加上：

```
1. 主光方向阴影图（ShadowCaster pass + 采样 + PCF）
2. 点光/聚光阴影（视锥体贴图或 cubemap，每个光源一张）
3. 阴影接收（shader 里采样阴影，需要额外的 uniform 与 pass tag）
```

阶段 A 从 3–5 天变成 **5–7 天**，且这是最容易出 bug 的部分
（视锥体拟合、pancaking、阴影 acne）。

### 程序化天空对阶段 A 的影响

天空盒需要一个独立的全屏 pass（在先不透明之前绘制，depth = far）。
好处是不依赖外部 cubemap 资产，且能随时调太阳位置与云量。

### 覆盖旧场景的代价

HDRP 版本的六个场景会失去渲染能力（材质全部是 HDRP/Lit）。
**建议在开始阶段 A 之前打一个 tag**（例如 `hdrp-final`），方便回溯。

---

## 6. 当前进度

| 阶段 | 状态 |
|---|---|
| A 最小 SRP | 未开始 |
| B 平面反射 | 未开始 |
| C LOD | 未开始 |
| D 泡沫 | 未开始 |
| E 后处理 | 未开始 |
| F 水下 | 未开始 |
