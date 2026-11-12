# NMEA 定位日志分析 — 开发计划（定稿）

## 目标

将 `Nmea2Kmz` 工具中 `nmea_to_kml.py` 与 `analyze_nmea_gga.bat` 的能力移植进 TestEngineer：

- 拖入/选择目录（或文件），递归扫描日志
- 校验合法 NMEA，分析固定/浮点/差分/单点/未定位比例
- 可选导出 CSV；生成 KML/KMZ 轨迹
- **多文件导出 KML 时弹出颜色对话框**，为每个测试数据指定轨迹颜色

## 验收数据（只读）

路径：`C:\Users\Tuly\Desktop\工作文件\工作工具\Nmea2Kmz`

- **禁止删除、移动、改写**该目录下任何采集数据
- 仅排除 `analyze_nmea_gga.bat`、`nmea_to_kml.py` 本身不作为验收输入
- 测试夹具只复制**摘录片段**到 `tests/Fixtures/Nmea/`，不碰原文件

## 已确认决策

| 项 | 决定 |
| --- | --- |
| 入口 | 左侧独立页「定位日志」，开发者选项之后、关于之前 |
| 扩展名 | 默认 `.txt`；开关可含 `.log` / `.nmea` |
| 差分 quality=2 | 单独成列，有数据才显示列 |
| 非法标准语句 | **不跳过文件**；剔除非法行并标识「含 N 条异常行」 |
| 厂商私有行（`$AGC`/`$NACK`/`$QX…`） | 不算非法，仅计私有语句数 |
| 末行截断 | 不记为异常 |
| 图表 | 汇总横向条；文件横向 100% 堆叠条；按目录竖向分组柱；表格/图表切换；无第三方图表库 |
| KML 颜色 | 多文件时模态对话框逐文件选色（预置色板 + 随机不重复）；单文件可用默认/随机 |
| 版本 | 完成后 `0.4.0` → `0.4.1`，改 props + iss，跑 `scripts/publish.ps1` |

## 文件判定

| 判定 | 条件 | 计入汇总 |
| --- | --- | --- |
| 有效 | ≥1 合法 GGA，无非法标准语句 | 是 |
| 有效·含异常行 | ≥1 合法 GGA，且有非法标准语句 | 是（非法行不入分母） |
| 跳过·无 GGA | 有标准 NMEA，无合法 GGA | 否 |
| 跳过·非 NMEA | 二进制 / 无合法标准语句 | 否 |
| 读取失败 | IO 错误 | 否 |

## 质量分类（GGA field 6）

4 固定 · 5 浮点 · 2 差分 · 1 单点 · 0/空 未定位 · 其余 其他

## 代码落点

- `TestEngineer.Core/Gnss/` — 解析与统计（纯逻辑）
- `TestEngineer.Infrastructure/Gnss/` — `NmeaLogAnalyzer`、`KmlTrackWriter`
- `TestEngineer.Desktop` — `NmeaPage`、`NmeaPageViewModel`、`NmeaColorPickerWindow`
- `TestEngineer.UI` — `Icon.Nav.Nmea`

## 阶段

| 阶段 | 内容 | 负责 |
| --- | --- | --- |
| P0 | 计划文档、夹具摘录 | 主代理 |
| P1 | Core 解析 + 测试 | subagent-core |
| P2 | 扫描 + KML 写入 | subagent-infra |
| P3 | 页面 + 图表 + 颜色对话框 | subagent-ui |
| P4 | 导航/DI 联调、真实目录验收 | 主代理 |
| P5 | 文档、版本、打包 | 主代理 |

## KML 颜色对话框行为

1. 用户勾选「生成轨迹」或点「导出 KML」且有效文件数 ≥ 2
2. 弹出模态窗：表格列出相对路径；每行颜色下拉（色板名 + 色块预览）+「随机」
3. 确认后按所选颜色生成；取消则不写文件
4. 色板与 `nmea_to_kml.py` 的 `COLOR_PALETTE` 对齐；随机时尽量互不重复

## 进度约定

- 真实数据只读
- 路上可看对话与 todo 进度；阻塞点直接提问确认
