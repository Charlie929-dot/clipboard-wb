# ClipboardWB

一个常驻托盘的 Windows 剪贴板历史工具。**按住 `Ctrl` 再按 `V` 唤出历史面板**，`↑` / `↓` 选择，**再按一次 `V` 把选中内容粘贴到当前窗口**。面板是无焦点置顶窗口，不会抢走光标，也不会打断你正在输入的应用。

界面是暗色玻璃卡片：渐入缩放动画、选中项平滑高亮、像素级平滑滚动（`↑` / `↓` / 数字直达会自动把选中行滚进视野）、亚克力质感的托盘菜单与设置窗口。

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
| `Ctrl + V` | 唤出历史面板，默认选中最新一条 |
| `↑` / `↓` | 在历史中循环移动选择 |
| `V` / `Enter` | 粘贴选中项（`Ctrl` 按住或松开都可以） |
| `1` – `9` | 直达第 N 条并粘贴 |
| `Esc` | 关闭面板，不改动剪贴板 |
| 鼠标单击 / 双击 | 选中 / 粘贴 |
| `★` | 置顶该条，不被容量淘汰 |

- 面板在松开 `Ctrl` 后仍会停留，方便用鼠标选；12 秒无操作自动淡出。
- 点击面板外部、`Esc`、或完成粘贴都会关闭面板。
- 支持 **文本 / 图片 / 文件列表**，相同内容自动去重并移到最前，历史持久化到磁盘。
- 托盘菜单可调整历史容量（10–120 条）、开关开机自启、清空历史、打开设置窗口。

## 实现要点

| 模块 | 文件 | 说明 |
| --- | --- | --- |
| 全局热键 | `Services/KeyboardHook.cs` | `WH_KEYBOARD_LL` 低层钩子；只在面板打开时吞掉方向键 / V / 数字键，普通输入不受影响；`Win+V`、`Ctrl+Shift+V` 主动放行 |
| 剪贴板监听 | `Services/ClipboardMonitor.cs` | 轮询 `GetClipboardSequenceNumber`，避开剪贴板查看器链；文本 / 图片 / 文件分别读取并做 SHA-256 去重 |
| 粘贴 | `Services/PasteService.cs` | 回写剪贴板后用 `SendInput` 模拟 `Ctrl+V`；注入的按键带 magic 标记，钩子自动放行，不会递归触发自己 |
| 历史存储 | `Services/HistoryStore.cs` | `ObservableCollection` + 防抖 JSON 持久化；图片单独存 PNG 文件，置顶项不参与淘汰 |
| 弹窗 | `Views/HistoryWindow.xaml` | `ShowActivated=False` + `WS_EX_NOACTIVATE` + `WS_EX_TOOLWINDOW`，靠光标所在显示器定位并做工作区边界收敛 |
| 键盘滚动 | `Views/HistoryWindow.xaml.cs`、`Views/SmoothScrollViewer.cs` | 按选中行的实测位置算出目标偏移，再用 `CompositionTarget.Rendering` 缓动滚动；不依赖 `ScrollIntoView`（无焦点窗口 + 物理滚动下它不生效） |
| 设置 / 托盘 | `Views/SettingsWindow.xaml`、`Services/TrayService.cs` | 暗色自绘菜单、开关、容量滑杆、注册表开机启动项 |

其他细节：单实例 Mutex、剪贴板写入重试、单例生命期内复用 HWND（首次弹出不卡顿）、DWM 暗色模式与圆角、异常兜底日志（`%TEMP%\clipboardwb-error.log`）。

## 自检与演示

```bat
dist\ClipboardWB.exe --selftest   :: 无界面自检，结果写到 selftest.log
dist\ClipboardWB.exe --demo       :: 用临时目录的示例数据展示面板，不安装钩子
```

自检覆盖：钩子安装/卸载、去重、容量淘汰、JSON 持久化与重载、两个窗口的 XAML 解析，以及**键盘导航滚动**——对 0→3→7→11→15→19→0 的跳转逐一验证算出的目标偏移确实让选中行落在视口内（含从末尾回绕到顶部）。

端到端脚本：

```bat
python scripts\hotkey_e2e.py            :: 真实模拟 Ctrl+V → ↓ → V，校验剪贴板回写
python scripts\scroll_e2e.py --seed    :: 预置 18 条编号历史（会备份原 history.json）
start dist\ClipboardWB.exe
python scripts\scroll_e2e.py           :: 连按 12 次 ↓、4 次 ↑，逐步截图
python scripts\scroll_e2e.py --restore :: 恢复原始历史
```

`scroll_e2e.py` 的截图可用来肉眼确认高亮项始终停在卡片可视区域内。

## 数据与配置

```
%APPDATA%\ClipboardWB\history.json     历史记录
%APPDATA%\ClipboardWB\images\          图片条目
%APPDATA%\ClipboardWB\settings.json    容量、开机自启
HKCU\...\CurrentVersion\Run\ClipboardWB  开机启动项
```

## 已知限制

- **接管了 `Ctrl+V`**：本工具把系统的单次粘贴改成了「唤出面板 → 再按 V」。最新一条永远在第一位，所以 `Ctrl+V`、`V` 两下就等于普通粘贴。
- 目标窗口若以**管理员权限**运行，普通权限进程无法向它注入按键（UIPI），此时会弹出托盘提示；把本工具也以管理员身份启动即可。
- 终端类应用常用 `Ctrl+Shift+V`，本工具对带 `Shift` 的组合键直接放行，可用原生快捷键。
- 如果系统里还运行着别的剪贴板工具（例如 `clipboard-dsh`），两者都会监听 `Ctrl+V`，建议只保留一个，避免同时弹窗。
- 自包含发布是「单文件 exe + 少量 WPF 原生 DLL」，不是绝对的单文件，见上文 `dist\` 说明。

## 版本

用 git 管理，提交粒度按「项目骨架 → 核心逻辑 → GUI → 构建与文档」拆分；`git log --oneline` 可查看。
远程仓库：<https://github.com/Charlie929-dot/clipboard-wb>（私有）。
