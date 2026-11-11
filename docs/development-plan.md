# TestEngineer 开发计划

版本：1.0（2026-11-11）
依据：`PRODUCT.md`、`DESIGN.md`、`windows-desktop-development-standard.md`、`docs/ui-ux-v1-prototype.html`（UI/UX v1.5 确认稿）

本计划中标注「需真机核对」的条目是按 Android 通用行为写的预期，尚未在设备上验证，落地时以实测为准。工期是单人开发的粗略估算，不是承诺。

---

## 1. 范围

七个页卡 + 常驻投屏 + 多设备切换，功能以原型 v1.5 为准：

| 模块 | 交付内容 |
|---|---|
| 外壳 | 标题栏多设备切换、可折叠导航（顶部左对齐图标）、工作区、可拖分隔条、右侧常驻投屏、状态栏 |
| 设备信息 | 电量、存储、内存、CPU、分辨率、内核、基带、安全补丁、构建类型、网络类型（Wi‑Fi 含 SSID/MAC）、IP、运行时间每秒刷新 |
| 应用管理 | 系统与第三方列表、有图标筛选、Launcher3 置顶、拖拽安装、启动、清数据、卸载、第三方一键授权 |
| 文件管理 | 内部存储与根目录、面包屑、按节点权限判定读写、跨目录累积多选、右键复制/剪切/删除/导出到目录、拖入上传、拖出导出、导入、证据包、系统信息导出 |
| 调试指令 | 栈顶 Activity、用户账户、截屏、焦点窗口、日志入口（含 OEM）、日志导出压缩、bugreport、Settings 开关、Monkey（系统/应用，可指定时长）、am/pm |
| 控制台 | 任意 adb 指令、流式输出、可中止、历史（↑↓、可清除）、快捷芯片（SELinux、logcat 关键字等） |
| 远程投屏 | 实时画面、触摸与按键转发、截屏、录屏、旋转、导航键、拖拽装包/传文件、参数配置 |
| 关于 | 浅色/深色/跟随系统、版本信息 |

不在本期：自动更新、多机同时投屏、群控、Layout Inspector、性能曲线。

---

## 2. 当前环境（已在本机核对）

| 项 | 状态 |
|---|---|
| .NET SDK | 10.0.403，已安装 |
| adb | 37.0.0，位于 `F:\AndroidSdk\platform-tools` |
| Inno Setup | `C:\Program Files (x86)\Inno Setup 6` 目录存在，`iscc` 不在 PATH，构建脚本需用绝对路径或配置项 |
| scrcpy | PATH 中未找到，需下载并固定版本 |
| Git | 2.44 已安装，但项目目录还不是仓库，M0 第一步 `git init` |
| 源码 | 尚无，仅有规范、PRODUCT、DESIGN 和 HTML 原型 |

---

## 3. 开工前需要确认的决策

每项都给了默认方案，不回复就按默认执行。D1 影响最大，建议优先看。

| 编号 | 决策 | 默认方案 | 备选 |
|---|---|---|---|
| D1 | 投屏集成方式 | 先做方案 A（内嵌 scrcpy 窗口），M1 用 2 天 spike 验证，不过关再转方案 B | 方案 B：自研客户端（见 5.3） |
| D2 | adb / scrcpy 来源 | 随包提供，设置里可改成自定义路径 | 仅用用户本机已装的 |
| D3 | 应用名称与图标获取 | 设备端辅助 jar（`app_process` 运行，不安装 APK），失败时回退为包名 + 首字占位 | PC 端拉 APK 解析（自适应图标还原差，且慢） |
| D4 | 系统与架构 | Windows 10 22H2 / Windows 11，仅 `win-x64` | 加 `win-arm64`（scrcpy 与 FFmpeg 原生依赖需单独验证） |
| D5 | 安装权限 | 当前用户安装，不要管理员权限 | 全用户安装 |
| D6 | 安装包格式 | `.apk` 与多个 split APK（`install-multiple`）；`.apks` / `.xapk` 后置 | 首版就支持 `.apks` / `.xapk` |
| D7 | 高危操作确认 | 系统 Monkey、`setenforce 0`、删除、根目录写操作一律二次确认 | 提供「不再提示」开关 |
| D8 | 数据目录的厂商名 | 需要你给一个名称，用于 `%LocalAppData%/<Vendor>/TestEngineer/` | — |

---

## 4. 技术栈与工程结构

技术栈按规范第 2 节：C#、.NET 10、WPF、WPF-UI、CommunityToolkit.Mvvm、Microsoft.Extensions.DependencyInjection、System.Text.Json、Inno Setup。NuGet 版本在 M0 核对与 `net10.0-windows` 的兼容性后固定，并提交锁文件。

```text
src/
  TestEngineer.Desktop/         # App、主窗口、7 个页面、ViewModel、DI 装配
  TestEngineer.UI/              # DESIGN.md 令牌（浅/深）、样式、通用控件、行为
  TestEngineer.Core/            # 模型、服务接口、任务状态、解析器（纯逻辑，无 WPF）
  TestEngineer.Infrastructure/  # 进程执行器、配置存储、日志、数据目录、压缩
  TestEngineer.Adb/             # adb 参数构造、设备跟踪、各功能的 adb 实现
  TestEngineer.Mirror/          # 投屏会话（scrcpy 宿主或自研客户端）
tests/
  TestEngineer.Core.Tests/
  TestEngineer.Adb.Tests/
tools/
  device-helper/                # 设备端辅助 jar 源码（D3 采用时）
  third-party/                  # adb、scrcpy 固定版本及许可证
installer/                      # Inno Setup 脚本
scripts/                        # build / publish / verify
docs/
```

依赖方向：`Desktop → UI / Core`，`Infrastructure / Adb / Mirror → Core`，`Core` 不引用 WPF。

---

## 5. 关键技术方案

### 5.1 ADB 访问层

- 统一通过 `adb.exe` 子进程，参数以列表传入，不拼 shell 字符串。控制台需要支持任意 adb 指令，因此进程方式是必选项，其余功能复用同一个执行器。
- 所有调用带 `-s <serial>`。任务在创建时绑定序列号，之后切换当前设备不影响已在跑的任务。
- 通用执行器能力：异步读 stdout/stderr、超时、取消、进程树回收、退出码、UTF-8 解码。
- 设备发现：常驻 `adb track-devices` 流，掉线后回退到轮询 `adb devices -l`。
- 每条发出的 adb 指令写入应用日志，便于排查。

### 5.2 多设备上下文

- `IDeviceContext` 提供当前设备及变更事件，各页 ViewModel 订阅后重载。
- 按设备缓存：文件当前路径、跨目录选中集合、应用列表、设备静态属性。选中集合不跨设备共用。
- 当前设备掉线：页面进入「设备已断开」状态，保留缓存，重连后恢复；投屏区显示重连入口。
- 切换设备时投屏会话切到新设备（单会话）。

### 5.3 投屏（最高风险）

| | 方案 A：内嵌 scrcpy 窗口 | 方案 B：自研客户端 |
|---|---|---|
| 做法 | 启动 `scrcpy.exe`，把其窗口挂进 WPF 的 `HwndHost`，随布局调整大小 | 推送 scrcpy-server 到设备，自己实现协议，FFmpeg 解码，WPF 渲染，控制消息走 socket |
| 触摸 / 按键 / 剪贴板 / 拖拽装包 | scrcpy 自带 | 全部自己实现 |
| 截屏 | `adb exec-out screencap -p` | 直接取解码帧 |
| 录屏 | 启动参数 `--record`，开关录制需重启会话；或另起无窗口录制进程 | 码流直接封装，可随时开关 |
| 主要风险 | 子窗口焦点、DPI、缩放闪烁；WPF 元素无法盖在画面之上 | 工作量大；协议随 scrcpy 版本变化，必须锁定 server 版本；FFmpeg 许可证 |
| 估算 | 5–8 人日 | 15–25 人日 |

M1 的 spike 通过标准（方案 A）：

1. 画面嵌入右侧面板，拖分隔条和缩放窗口时保持纵横比，无残影。
2. 鼠标点击、滑动、键盘输入到达设备。
3. 切换页卡画面不中断。
4. 在 100% 与 150% DPI 下尺寸正确。
5. 主窗口关闭后 scrcpy 进程与设备端 server 都退出。

任何一条不满足且两天内修不好，转方案 B 并重排计划。

### 5.4 应用名称与图标

`pm list packages` 只给包名，拿不到应用名和图标。默认方案（D3）：

- 写一个小的设备端 jar，用 `app_process` 以 shell 身份运行，通过 PackageManager 输出包名、应用名、版本、是否系统应用、是否有 Launcher 入口，并把图标渲染成 PNG。
- PC 端按「包名 + versionCode」缓存图标，列表先出文字再补图标。
- jar 运行失败时回退：`pm list packages -f -s/-3` + 首字占位图标，功能不受阻。

「有图标」定义为存在 `MAIN` + `LAUNCHER` 入口的应用。Launcher3 按包名匹配并置顶，OEM 桌面包名不同，另外按「默认桌面」识别（需真机核对）。

### 5.5 文件权限判定

- 列目录用一次 shell 调用拿到名称、类型、模式、大小、时间，再批量 `test -r` / `test -w` 得到 adb shell 身份的实际读写能力，结果映射为「可读写 / 可读 / 不可读」。
- 写类操作（上传、新建、粘贴、删除、剪切）只在可写节点启用，禁用时用 tooltip 说明原因。
- 文件名含空格、中文、特殊字符必须正确转义，作为解析器单元测试的重点。
- `/sdcard/Android/data` 等目录在不同 Android 版本上 shell 的可见性不同（需真机核对），界面按实际判定结果展示即可。

### 5.6 配置与数据

- 安装版：`%LocalAppData%/<Vendor>/TestEngineer/`；便携版：`<程序目录>/Data/`，以 `portable.flag` 判定。
- `settings.json` 带版本字段，临时文件替换写入。内容：主题、导航折叠状态、投屏宽度与参数、工具路径、导出默认目录、控制台历史上限。
- 控制台历史单独存一个文件，可一键清除。
- 应用不保存任何凭据。

---

## 6. 里程碑

每个里程碑结束时主分支可运行、可演示。

### M0 工程基建（2–3 人日）

- `git init`、`.gitignore`、解决方案与 6 个项目、Nullable、代码风格、NuGet 锁文件。
- DI 容器、日志、数据目录与便携模式判定、设置读写与版本迁移。
- 把 `DESIGN.md` 令牌落成浅色/深色资源字典，全部通过语义资源名访问。
- `scripts/build.ps1`（还原、编译、测试，失败返回非零）。

验收：空窗口能启动并切换主题；`build.ps1` 通过；设置能保存和读回。

### M1 外壳、设备层、投屏 spike（5–7 人日）

- 主窗口：标题栏、设备切换下拉、可折叠导航、工作区、分隔条、投屏宿主区、状态栏。
- 页面导航框架与 7 个空页面。
- adb 定位（随包 → 设置覆盖 → PATH）、通用进程执行器、设备跟踪、`IDeviceContext`。
- 投屏 spike，按 5.3 的通过标准出结论。

验收：插拔设备列表即时变化；切换设备后标题栏和状态栏同步；导航折叠状态重启后保留；spike 结论写入 `docs/`。

### M2 设备信息（2–3 人日）

- 采集与解析：`getprop`、`dumpsys battery`、`df`、`/proc/meminfo`、CPU、`wm size` / `wm density`、内核版本、基带、安全补丁、网络类型、Wi‑Fi SSID/MAC、IP。
- 运行时间：定期读取 `/proc/uptime` 校准，本地每秒递增显示。
- 刷新、复制摘要；离线与读取失败状态。

验收：user 与 userdebug 设备各一台字段齐全；拿不到的字段显示「—」并说明原因，不留空白。

### M3 投屏正式化（方案 A 5–8 人日）

- 会话生命周期：启动、停止、重连、设备切换、异常退出提示。
- 工具条：返回、Home、多任务、电源、截屏、录屏、旋转。
- 参数页：最大边、码率、帧率、编码器、输出目录，改动后提示重启会话生效。
- 拖拽 APK 到画面安装，拖拽文件推送到 `/sdcard/Download`。
- 关闭主窗口时回收 scrcpy 进程与设备端 server。

验收：5.3 的 5 条标准全部满足；录屏文件可播放；连续切换设备 10 次无残留进程。

### M4 应用管理（6–9 人日）

- 设备端辅助 jar 及构建脚本（D3）、PC 端调用与图标缓存。
- 列表：36px 图标行、搜索、有图标/第三方/系统/全部、Launcher 置顶、虚拟化滚动。
- 行操作：启动、清数据、卸载，系统应用禁用授权与卸载。
- 一键授权：读取应用声明的权限，运行时权限逐个 `pm grant`，AppOps 类按固定清单 `appops set`，结果逐项列出成功与失败。
- 拖拽安装：单 APK、多 split APK，显示进度与失败原因。

验收：500 个包以上的设备列表滚动流畅；授权结果与 `dumpsys package` 一致；安装失败时显示 adb 返回的原因码。

### M5 文件管理（7–10 人日）

- 双入口（内部存储、根目录）、面包屑、目录缓存与刷新。
- 权限判定与标签（5.5）。
- 跨目录选中集合：勾选、Ctrl+点击、已选条、查看列表、清空，按设备隔离。
- 右键菜单：复制、剪切、删除、导出到目录；粘贴；选中集合内右键时批量生效。
- 上传（按钮与拖入）、导出（选目录）、新建文件夹。
- 拖出到资源管理器。
- 导入、证据包（截图 + 录屏 + logcat + 设备摘要 → ZIP）、系统信息导出。

验收：中文与带空格文件名全流程可用；在 A 目录选文件、进 B 目录再选、一次导出全部成功；只读目录写操作禁用且有说明；500MB 文件传输期间界面不卡，可取消。

### M6 调试指令（5–7 人日）

- 指令清单数据化（JSON：分类、标题、命令模板、适用条件、是否需参数、是否高危），新增指令不改代码。
- 结果弹窗：等宽输出、复制、长输出虚拟化。
- 带参数项：Monkey（应用 / 系统、时长、seed、throttle）、日志导出压缩、bugreport（进度）。
- OEM 日志入口：按机型和包是否存在决定显示。
- 长任务可取消，状态栏显示进行中任务。

验收：清单中每条指令在对应条件的设备上有预期输出；Monkey 到设定时长自动停止，中途可手动停止；bugreport 生成到指定目录。

### M7 控制台（3–4 人日）

- 输入解析（支持引号）、流式输出、中止当前命令、输出行数上限。
- 历史：持久化、↑↓、历史面板、清除。
- 快捷芯片数据化；logcat 关键字用应用内过滤实现。
- 需要 root 的芯片在 user 设备上禁用并说明。

验收：`logcat` 持续输出 10 分钟内存稳定；中止后无残留 adb 子进程；重启应用后历史仍在。

### M8 关于、设置与打磨（3–4 人日）

- 关于页：主题三态、版本、第三方组件与许可证。
- 全页面状态补齐：空、加载、失败、设备断开。
- 键盘导航与焦点可见、浅深主题逐页检查、100% / 125% / 150% / 200% DPI、最小窗口 1100×700。
- 退出时有任务在跑的处理。

验收：规范第 7 节「UI 与功能」清单逐项通过。

### M9 发布（3–5 人日）

- `scripts/publish.ps1` 一键生成：自包含发布 → Portable ZIP（含 `portable.flag`）→ Inno Setup 安装包 → SHA-256 清单。
- 安装脚本：固定 `AppId`、开始菜单、卸载入口、升级覆盖、默认保留用户数据。
- 干净系统（未装 .NET）上验证安装、首次启动、升级、卸载，以及便携版在中文带空格路径下运行。

验收：规范第 7 节「安装与免安装」清单逐项通过。

### 汇总

| 里程碑 | 人日 |
|---|---|
| M0 基建 | 2–3 |
| M1 外壳 + 设备层 + spike | 5–7 |
| M2 设备信息 | 2–3 |
| M3 投屏（方案 A） | 5–8 |
| M4 应用管理 | 6–9 |
| M5 文件管理 | 7–10 |
| M6 调试指令 | 5–7 |
| M7 控制台 | 3–4 |
| M8 打磨 | 3–4 |
| M9 发布 | 3–5 |
| 合计 | 41–60 |

投屏若转方案 B，M3 改为 15–25 人日，合计约 51–77 人日。

顺序上把投屏 spike 放在 M1，是为了尽早知道最大的不确定项。M4–M7 之间没有强依赖，可以按你的优先级调换。

---

## 7. 功能与 adb 实现对照

| 功能 | 实现 | 限制与备注 |
|---|---|---|
| 构建类型 / 是否 debug | `getprop ro.build.type`、`ro.debuggable` | — |
| 安全补丁 / 基带 / 内核 | `ro.build.version.security_patch`、`gsm.version.baseband`、`uname -r` | 基带属性名个别厂商不同，需真机核对 |
| 网络类型 | `dumpsys connectivity` 解析活动网络 | 输出格式随版本变化，解析器要容错 |
| Wi‑Fi MAC | `ip addr show wlan0` 或 `dumpsys wifi` | Android 10 起默认随机 MAC，显示的是当前连接所用地址，界面需注明；user 版本可读性需真机核对 |
| 栈顶 Activity | `dumpsys activity activities`，应用内筛选 `ResumedActivity` | 字段名随版本变化 |
| 用户账户 ID | `pm list users` | — |
| 截屏 | `adb exec-out screencap -p` 直接写本机文件 | — |
| 飞行模式 | 优先 `cmd connectivity airplane-mode enable/disable` | 旧版本回退到 `settings put` + 广播，user 版本上广播可能被拒，需真机核对 |
| Wi‑Fi 开关 | `svc wifi enable/disable` | — |
| 蓝牙开关 | `svc bluetooth enable/disable` 或 `cmd bluetooth_manager` | 可用命令随版本不同，按版本选择 |
| 显示设置等页面 | `am start -a android.settings.DISPLAY_SETTINGS` | — |
| OEM 日志入口 | 按包是否存在匹配，如 MTK DebugLoggerUI | 各厂商包名与 Activity 需逐个真机核对后写入清单 |
| 日志导出 | `logcat -d -b all` + OEM 日志目录拉取 → ZIP | OEM 目录路径各家不同 |
| bugreport | `adb bugreport <目录>` | 耗时数分钟，解析进度输出 |
| Monkey | `monkey [-p pkg] -s seed --throttle ms -v N` | Monkey 按事件数运行，不支持时长：用大事件数启动，到时结束设备端 monkey 进程；系统 Monkey 可能改动系统设置，必须二次确认 |
| 一键授权 | `dumpsys package` 取声明权限 → `pm grant`；AppOps 用 `appops set` | `pm grant` 只对运行时权限有效，其余会失败，逐项回报 |
| 清数据 / 卸载 | `pm clear`、`adb uninstall` | — |
| 安装 | `adb install -r`，split 用 `install-multiple` | `.apks` / `.xapk` 见 D6 |
| 关闭 SELinux | `setenforce 0` | 需要 root，仅 userdebug / eng 可用，user 设备上禁用 |
| `adb root` / `remount` | 直接转发 | 仅 userdebug / eng |
| logcat 关键字 | 应用内过滤 | 控制台不经过 cmd，不支持 `| findstr` 这类管道 |
| 文件复制 / 剪切 / 删除 | `cp -r`、`mv`、`rm -rf` | 删除要拦截对根级路径的误操作 |

---

## 8. 风险

| 风险 | 影响 | 应对 |
|---|---|---|
| scrcpy 窗口内嵌的焦点、DPI、缩放问题 | 投屏体验，可能转方案 B，增加 10–17 人日 | M1 spike 提前验证并设通过标准 |
| 设备端辅助 jar 在个别 ROM 上跑不起来 | 应用名与图标缺失 | 回退到包名 + 占位图标 |
| 拖出文件到资源管理器需要延迟渲染的虚拟文件 | 实现复杂，大文件体验差 | 先保证「导出到目录」；拖出做不稳就限制为小文件或后置 |
| `dumpsys` 输出随 Android 版本和厂商变化 | 设备信息、栈顶、网络类型解析失败 | 解析器容错并配测试样本，失败显示原始输出 |
| user 版本设备权限受限 | 部分指令不可用 | 按构建类型和探测结果禁用并说明，不静默失败 |
| 大文件传输没有进度 | `adb push/pull` 在非终端下不输出进度 | 首版显示文件级进度与不确定进度条；后续可改用 sync 协议 |
| 第三方组件再分发许可 | adb、scrcpy 及其附带的 FFmpeg/SDL | M0 核对许可条款，随包附许可证；不允许再分发则改为引导用户自备 |
| 系统 Monkey、删除、`setenforce` 误操作 | 设备状态被破坏 | 高危标记 + 二次确认，命令写入日志 |

---

## 9. 测试策略

- 单元测试（`Core.Tests`、`Adb.Tests`）：各解析器（设备列表、目录列表、`getprop`、`dumpsys battery/connectivity/package`、Monkey 输出）、命令行参数拆分、路径转义、配置迁移、跨目录选中集合。解析器用真机抓取的输出样本做用例。
- ViewModel 测试：用假的 adb 执行器覆盖任务状态机（可执行、执行中、取消中、成功、失败、已取消）和设备切换。
- 真机矩阵：至少覆盖 user 与 userdebug 各一台、MTK 与高通各一台、Android 11 至最新版本中的三个版本、USB 与 Wi‑Fi 两种连接。
- 每个里程碑的验收条目进入回归清单，M8 全量跑一遍。
- 发布验证按规范第 7 节清单执行。

---

## 10. 下一步

1. 确认第 3 节的决策，至少 D1、D3、D8。
2. 执行 M0。
3. M1 完成后给出投屏 spike 结论，据此确定 M3 的方案和总工期。
