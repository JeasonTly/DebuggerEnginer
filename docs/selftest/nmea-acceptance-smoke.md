# NMEA 验收冒烟（2026-11-12）

## 真实数据

- 路径：`C:\Users\Tuly\Desktop\工作文件\工作工具\Nmea2Kmz`（**只读**，未删除/改写）
- 冒烟时文件数仍为 76（含 bat/py）

## 自动化

- `NmeaAcceptanceSmokeTests.Scan_RealNmea2Kmz_TxtDefault_AndKmlToTemp`：**通过**
- 默认仅 `.txt` 扫描 ≥50 文件；打开 `.log/.nmea` 后文件数不少于默认
- KML 写到 `%TEMP%\TestEngineer-NmeaSmoke\...`，含 `LineString`/`coordinates`，未写回真实目录

## 单元/集成

- `FullyQualifiedName~Nmea|FullyQualifiedName~Kml`：**21 通过**
- Desktop 构建：0 警告 0 错误

## 明早手工验收清单

1. 侧栏「定位日志」→ 拖入整个 `Nmea2Kmz` 目录
2. 看汇总固定/浮点/差分/单点/未定位比例；表格与图表切换
3. 打开「包含 .log/.nmea」应多出 2 个 `.log`
4. 导出 CSV；导出 KML 时弹出颜色对话框，为不同文件选色后生成（建议输出到临时目录，避免写满采集目录）
5. 浅色/深色主题各看一眼汇总条与状态标签
