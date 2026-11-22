# 图1 → 图2 执行基线目录

**用途**：存放 S0–S7 的基线包、指标、对照图、验收表。
**规则**：每个 Bx 目录在对应门禁通过后只读，不覆盖；新改动生成 `Bx_v2/`。

## 目录

```
B0/  当前树 baseline + 目标裁切
B1/  图2 取景
B2/  宏观形体 clay
B3/  叶簇卡片
B4/  法线/AO/光影
B5/  场景/雾/调色
B6/  640×360 + OKLab 量化
B7/  最终验收 + 性能
```

## 每个 Bx 至少包含

| 文件 | 内容 |
|---|---|
| `baseline.json` | commit、Unity 版本、seed、相机、分辨率、后处理、目标 hash |
| `Bx_640.png` | 640×360 原始渲染 |
| `Bx_1080p.png` | Point 放大后的 1080p（S6 起） |
| `metrics.csv` | edge density、L 分位、hue、palette、覆盖率、树干比例 |
| `compare_target.png` | 与图2 同裁切并排 |
| `notes.md` | 本阶段结论、失败项、CR 编号 |

## 命名

`B<阶段>_<视图>_<分辨率>_v<版本>.png`

示例：`B3_front_640_v1.png`、`B6_1080p_v2.png`。

## 目标图

- 目标裁切：`B0/target_620.png`
- 目标 hash：写入 `B0/baseline.json`
- 后续对比只用这张图，不换参考帧、不换裁切、不换树占比。
