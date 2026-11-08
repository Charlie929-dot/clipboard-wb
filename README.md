# ClipboardWB

一个常驻托盘的 Windows 剪贴板历史工具。**轻按 `Ctrl+V` 就是原生粘贴**，和平时完全一样、不弹面板；**按住 `Ctrl+V` 约 180 ms** 才唤出历史面板，`↑` / `↓` 选择，**再按一次 `V` 把选中内容粘贴到当前窗口**。面板是无焦点置顶窗口，不会抢走光标，也不会打断你正在输入的应用。

界面是暗色玻璃卡片：渐入缩放动画、选中项平滑高亮、像素级平滑滚动（`↑` / `↓` / 数字直达会自动把选中行滚进视野）、亚克力质感的托盘菜单与设置窗口。**置顶项固定在列表最前**；面板标题栏可拖动，位置会被记住（设置里可切回跟随鼠标）；设置窗口内容支持鼠标滚轮，小屏也能看全。

## 快速开始

```bat
build.bat        :: 发布自包含版到 dist\（首次会联网拉取 win-x64 运行时包）
run.bat          :: 启动；之后常驻托盘，右键托盘图标可设置 / 清空 / 退出
```

自包含版 **不需要系统安装 .NET**，但请保留 `dist\` 整个文件夹（WPF 的原生 DLL 和 `app.ico` 必须和 exe 放在一起）。

开发时也可以直接用 SDK 编译运行：

```bat
set DOTNET_ROOT=D:\Clipboard\.tools\dotnet
..\..\.tools\dotnet\dotnet.exe build src\ClipboardWB\ClipboardWB.csproj
src\ClipboardWB\bin\Debug\net10.0-windows\ClipboardWB.exe
```

`build.bat fx` 会额外产出一个 300 KB 的轻量版到 `dist-fx\`，用 `run.bat fx` 启动（依赖本机 `.tools` 里的 .NET 10 运行时）。

## 操作

| 按键 | 作用 |
| --- | --- |
| **轻按 `Ctrl+V`** | 原生粘贴，与系统默认行为一致，**不弹面板** |
| **长按 `Ctrl+V`** | 按住约 180 ms 唤出历史面板，默认选中最新一条 |
| `↑` / `↓` | 在历史中循环移动选择（自动滚动到可见区域） |
| `V` / `Enter` | 粘贴选中项（面板打开时，`Ctrl` 按住或松开都可以） |
| `1` – `9` | 直达第 N 条并粘贴 |
| `Esc` | 关闭面板，不改动剪贴板 |
| 鼠标单击 / 双击 | 选中 / 粘贴 |
| `★` | 置顶该条，固定在列表最前，且不被容量淘汰 |
| 拖动面板标题栏 | 自定义面板位置并自动记住（设置里可改回「跟随鼠标」） |

- **粘贴到「确认选择那一刻有焦点」的窗口**：面板是无焦点窗口，打开后你可以先切到另一个程序再选，内容会粘到新窗口，不会跑回打开面板的那个窗口。
- 置顶项永远排在非置顶项之前：新复制的内容插在「置顶区」之后，粘贴过的条目也只会在非置顶区内前移。
- 判定阈值是 `KeyboardHook.HoldMilliseconds`（默认 180 ms）；短按不会弹面板，长按也不会误粘贴。
- 设置 → 「面板跟随鼠标」：关闭后拖动标题栏即可固定位置；重新开启会恢复跟随鼠标并清掉记住的坐标。
- 面板在松开 `Ctrl` 后仍会停留，方便用鼠标选；12 秒无操作自动淡出。
- 点击面板外部、`Esc`、或完成粘贴都会关闭面板。
- 支持 **文本 / 图片 / 文件列表**，相同内容自动去重并移到最前，历史持久化到磁盘。
- 托盘菜单可调整历史容量（10–120 条）、开关开机自启、清空历史、打开设置窗口。

## 实现要点

| 模块 | 文件 | 说明 |
| --- | --- | --- |
| 全局热键 | `Services/KeyboardHook.cs` | `WH_KEYBOARD_LL` 低层钩子；扣住 `Ctrl+V` 的 `V` keydown，短按在松键时补发一次带 magic 标记的原生 `Ctrl+V`，长按 180 ms 用 `DispatcherTimer` 唤出面板（排队到下一轮消息循环，不阻塞钩子）；`Win+V`、`Ctrl+Shift+V`、普通字母 `v` 全部放行 |
| 剪贴板监听 | `Services/ClipboardMonitor.cs` | 轮询 `GetClipboardSequenceNumber`，避开剪贴板查看器链；文本 / 图片 / 文件分别读取并做 SHA-256 去重 |
| 粘贴 | `Services/PasteService.cs` + `App.xaml.cs` | 在用户确认选择时读取 `GetForegroundWindow()` 作为粘贴目标，回写剪贴板后用 `SendInput` 模拟 `Ctrl+V`；**不再激活打开面板时记录的窗口**（旧行为会把焦点拉回原窗口），仅当前台窗口为空或落在自己面板上时才用该锚点兜底；注入的按键带 magic 标记，钩子自动放行 |
| 历史存储 | `Services/HistoryStore.cs` | `ObservableCollection` + 防抖 JSON 持久化；图片单独存 PNG；新增 / 去重 / 粘贴 / 切换置顶 / 加载都维护「pinned 前缀」不变式，置顶项不参与容量淘汰 |
| 弹窗 | `Views/HistoryWindow.xaml` | `ShowActivated=False` + `WS_EX_NOACTIVATE` + `WS_EX_TOOLWINDOW`，按光标或记住的位置定位并做工作区收敛；标题栏用物理光标坐标拖动（header 必须 `Background=Transparent` 才能命中空白区域），松手后写入 `settings.json` |
| 键盘滚动 | `Views/HistoryWindow.xaml.cs`、`Views/SmoothScrollViewer.cs` | 按选中行的实测位置算出目标偏移，再用 `CompositionTarget.Rendering` 缓动滚动；不依赖 `ScrollIntoView`（无焦点窗口 + 物理滚动下它不生效） |
| 设置 / 托盘 | `Views/SettingsWindow.xaml`、`Services/TrayService.cs` | 暗色自绘菜单、开关、容量滑杆、注册表开机启动项 |

其他细节：单实例 Mutex、剪贴板写入重试、预创建 HWND 并离屏预热首次布局（首次长按不卡顿）、DWM 暗色模式与圆角、异常兜底日志（`%TEMP%\clipboardwb-error.log`）。

## 自检与演示

```bat
dist\ClipboardWB.exe --selftest   :: 无界面自检，结果写到 selftest.log
dist\ClipboardWB.exe --demo       :: 用临时目录的示例数据展示面板，不安装钩子
dist\ClipboardWB.exe --demo --settings :: 用隔离的临时配置展示设置窗口
```

`--demo` 会把历史与设置都重定向到 `%TEMP%\ClipboardWB.Demo`，不会碰真实配置。

自检覆盖：钩子安装/卸载、去重、容量淘汰、JSON 持久化与重载、两个窗口的 XAML 解析、**置顶排序不变式**（含切换置顶后重载、淘汰不删置顶项）、**键盘导航滚动**（0→3→7→11→15→19→0 逐一验证目标偏移让选中行落在视口内，含回绕）、**设置窗口滚动**（小屏高度下能滚到最后一行并验证其可见）、**面板位置**的配置往返与工作区收敛，以及**粘贴目标窗口解析**（A 窗口打开 / 切到 B 窗口粘贴时不激活 A）。

端到端脚本：

```bat
python scripts\hotkey_e2e.py            :: 真实模拟 长按Ctrl+V → ↓ → V，校验剪贴板回写
python scripts\scroll_e2e.py --seed    :: 预置 18 条编号历史（会备份原 history.json）
start dist\ClipboardWB.exe
python scripts\scroll_e2e.py           :: 连按 12 次 ↓、4 次 ↑，逐步截图
python scripts\scroll_e2e.py --restore :: 恢复原始历史
```

`scroll_e2e.py` 的截图可用来肉眼确认高亮项始终停在卡片可视区域内。

轻按 / 长按的判定用真实的 Win32 输入框验证（不是只看截图）：

```bat
start dist\ClipboardWB.exe
python scripts\tap_hold_e2e.py
```

它依次验证：轻按把当前剪贴板内容**原生粘贴**进输入框、长按不粘贴且弹出面板、`↓` + `V` 粘贴较旧条目，最后恢复原剪贴板。

跨窗口粘贴用两个独立的 Win32 输入框验证：

```bat
start dist\ClipboardWB.exe
python scripts\cross_window_e2e.py
```

基线在 A 窗口打开并粘贴（A 必须收到内容）；随后在 A 打开面板、**切到 B 再按 `V`**，断言焦点确实在 B、B 收到历史条目且 A 保持为空。这能防住「粘贴跑回打开面板的窗口」这类回归。

排查按键问题时可设 `CLIPBOARDWB_KEYLOG=<日志文件路径>` 后启动，钩子会把每次 `V` 的按下/松开、180 ms 判定和补发动作写入日志。

## 数据与配置

```
%APPDATA%\ClipboardWB\history.json     历史记录
%APPDATA%\ClipboardWB\images\          图片条目
%APPDATA%\ClipboardWB\settings.json    容量、开机自启、面板跟随鼠标 / 固定位置
HKCU\...\CurrentVersion\Run\ClipboardWB  开机启动项
```

## 已知限制

- **轻按会有一个按键时长的延迟**：为了区分长按，钩子先扣住 `V`，在松开瞬间才补发原生 `Ctrl+V`，所以粘贴比系统默认晚约 50–120 ms（即你按住 `V` 的时长）。这是「短按原生粘贴 + 长按唤出面板」无法避免的代价；习惯后基本无感。
- 目标窗口若以**管理员权限**运行，普通权限进程无法向它注入按键（UIPI），此时会弹出托盘提示；把本工具也以管理员身份启动即可。
- 终端类应用常用 `Ctrl+Shift+V`，本工具对带 `Shift` 的组合键直接放行，可用原生快捷键。
- 如果系统里还运行着别的剪贴板工具（例如 `clipboard-dsh`），它也会监听 `Ctrl+V`，可能和本工具的短按补发 / 长按弹窗冲突，建议只保留一个。
- 自包含发布是「单文件 exe + 少量 WPF 原生 DLL」，不是绝对的单文件，见上文 `dist\` 说明。

## 版本

用 git 管理，提交粒度按「项目骨架 → 核心逻辑 → GUI → 构建与文档」拆分；`git log --oneline` 可查看。
远程仓库：<https://github.com/Charlie929-dot/clipboard-wb>（私有）。

- `v1.0.0` 初始版本
- `v1.0.1` 修复 ↑↓ 键盘导航不自动滚动
- `v1.1.0` 交互改为「轻按原生粘贴 / 长按 180 ms 唤出历史」
- `v1.2.0` 置顶项固定最前、设置窗口可滚动、面板可拖动并记住位置
- `v1.2.1` 修复跨窗口粘贴：内容跟随确认选择时的焦点窗口，不再跑回打开面板的窗口
