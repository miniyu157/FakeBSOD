# FakeBSOD

终极高仿真 Windows 蓝屏恶作剧。

**毫不妥协的硬件级欺骗。**  
多屏幕、全局钩子、强制静音、伪二维码、自适应语言、弹出设备提示音。  
源码约 500 行，C#4.0，不依赖第三方库。  
适合炫技、教学、减压。

---

## 特性

- **定格**：等待程序触发后，先截获当前屏幕并全屏定格，鼠标变为等待状态。延迟数秒后画面切换到 BSOD。

- **多显示器控制**：遍历所有显示器，主屏幕绘制完整 BSOD，副屏幕覆盖纯黑。

- **你硬件或驱动炸了**：蓝屏显示前同步播放 `%SystemRoot%\Media\Windows Hardware Remove.wav`。

- **关闭声音**：通过 Core Audio API (`IAudioEndpointVolume`) 直接操作系统主音量静音。

- **全局低层键盘钩子**：封锁以下快捷键：  
  <kbd>Win</kbd>、<kbd>Alt</kbd>+<kbd>Tab</kbd>、<kbd>Alt</kbd>+<kbd>Esc</kbd>、<kbd>Ctrl</kbd>+<kbd>Esc</kbd>、<kbd>Alt</kbd>+<kbd>F4</kbd>。

- **随机递增进度条**：从 0% 开始，每 3 秒递增 4%–15%，至 100% 后再延迟数秒自动退出。

- **伪二维码**：程序在内存中生成看起来很像二维码的二维码，但实质是随机数据。手机扫码时只会持续对焦而永远无法识别。

- **自适应语言与字体**：自动检测系统语言与合适的字体，也可以修改 `TargetLanguage` 字段指定语言进行编译。

- **DPI 自适应**：调用 `SetProcessDPIAware()`，所有视觉元素都以屏幕高度为基准等比缩放。

- **干净退出零残留**：按 <kbd>ESC</kbd> 或进度到达 100% 后自动退出：恢复鼠标显示、卸载键盘钩子、还原声音状态。不修改注册表，无后台线程驻留。

---

## 快速开始

### 编译（单文件，零依赖）

在 Windows 命令提示符中，使用 .NET Framework 内置编译器：

```cmd
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /target:winexe /out:FakeBSOD.exe FakeBSOD.cs
```

无需 Visual Studio，无需 NuGet 还原。

### 运行

双击 `FakeBSOD.exe` 或命令行启动。  

---

## 退出方式

- 按 <kbd>ESC</kbd>，立刻中止所有计时器，恢复鼠标、恢复音量，进程干净退出
- 等待进度 100%，进度走完后自动延迟退出。
- <kbd>Ctrl</kbd>+<kbd>Alt</kbd>+<kbd>Del</kbd>，安全界面仍可调出。

**请勿用电源键强制关机**，除非你真的以为蓝屏了。

---

## 自定义与调试

### 切换语言

修改 `MainForm.TargetLanguage` 静态字段即可强制指定语言：

```csharp
public static LanguageOption TargetLanguage = LanguageOption.Zh; // 中文
// 或 LanguageOption.En, LanguageOption.Auto
```

### 修改配色

`MainForm` 中的两个公共颜色常量可以直接替换：

```csharp
public static readonly Color BackgroundColor = Color.FromArgb(0, 120, 215); // BSOD 蓝色
public static readonly Color TextColor = Color.White;
```

使用任何 .NET `Color` 值即可。

### 调整时序

在 `MainForm` 顶部，可直接修改并重新编译：

- `InitialDelayMs`：启动到蓝屏的等待时间
- `ProgressIntervalMs`：进度刷新间隔
- `ProgressMinIncrement`, `ProgressMaxIncrement`：每次进度的随机增量范围
- `ExitDelayMs`：进度 100% 后延迟退出时间

### 更换声音

声音文件路径为 `%SystemRoot%\Media\Windows Hardware Remove.wav`。如需替换，修改 `Timer_Tick` 方法中的 `wavPath` 变量。播放使用 `SoundPlayer.PlaySync()`，可换为任何 Windows 系统支持的标准 WAV。

### 添加新语言

1. 实现 `ILanguageProvider` 接口，提供 `FontName`、`MainText`、`SupportText`、`FormatProgress`。
2. 在 `LanguageOption` 枚举中添加新条目。
3. 在 `LanguageFactory.GetProvider()` 中添加对应分支。

系统已有中英文实现，可作为模板直接复制。

---

## 免责声明

本项目**仅供安全研究、技术学习及合法恶作剧**。严禁用于：

- 诱导他人支付“解锁费”或拨打诈骗电话；
- 运行于包含未保存工作或关键进程的计算机。

作者不对因滥用导致的系统损坏、数据丢失或法律纠纷负任何责任。  
运行前请确认目标已保存所有数据。

---

## 许可证

MIT License。可自由 fork、修改、署名，但需保留原作者信息及上述免责声明。
