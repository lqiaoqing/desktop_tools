# Desktop Tools

Windows 11 桌面工具集合，覆盖效率工具、AI 工具、开发者工具、文件处理工具和自动化工具。

目标不是做 Demo，而是尽量形成真正可运行、可测试、可发布、可持续维护的 Windows 产品。

## 当前状态

已实现第一个功能：Windows 11 托盘常驻一键内存清理（C# / WPF / .NET 10）。

- 主程序 `src/DesktopTools`：asInvoker，单实例，托盘，主窗口，统一内存查询与清理流程。
- 提升 Helper `src/DesktopTools.Helper`：固定 `cache-flush` 协议，按需 UAC 清理 System File Cache。
- 测试 `tests/DesktopTools.Tests`：聚合、并发、Before/After 失败、Helper 故障等单元测试。

不清理 Standby List，不结束第三方进程，不强制 Trim 第三方 Working Set。

## 环境要求

| 项 | 要求 |
| --- | --- |
| 操作系统 | Windows 11 x64（开发目标；Win10 一般也可编译） |
| SDK | .NET 10 SDK（含 Windows Desktop / WPF / WinForms） |
| SDK 版本锁定 | `global.json` 要求 `10.0.110` 起，`rollForward: latestFeature`（同系列更新版本可用） |
| IDE（可选） | Visual Studio 2022 17.12+，或直接用命令行 `dotnet` |
| 网络 | 首次 `restore` 需访问 NuGet（仅测试工程有第三方包） |

安装 SDK 后校验：

```powershell
dotnet --list-sdks
# 应能看到 10.0.x，例如 10.0.110
```

## 快速开始（新机器）

```powershell
git clone https://github.com/lqiaoqing/desktop_tools.git
cd desktop_tools

# 还原依赖（主程序/Helper 无第三方包；测试包来自 NuGet）
dotnet restore DesktopTools.sln

# 建议整包编译，确保 Helper 一并产出并复制到主程序 helper\ 目录
dotnet build DesktopTools.sln -c Debug

# 运行单元测试
dotnet test tests\DesktopTools.Tests\DesktopTools.Tests.csproj -c Debug --no-build
```

启动主程序：

```powershell
src\DesktopTools\bin\Debug\net10.0-windows\DesktopTools.exe
```

首次启动会显示主窗口并创建托盘图标；关闭窗口仅隐藏，从托盘「退出」才结束进程。重复启动同一会话内只会有一个实例。

## 工程结构

```text
desktop_tools/
├── DesktopTools.sln              # 解决方案入口
├── global.json                   # 锁定 .NET SDK
├── src/
│   ├── DesktopTools/             # WPF 主程序（托盘 + 主窗口 + 清理流程）
│   │   ├── App.xaml / App.xaml.cs
│   │   ├── MainWindow.xaml / MainWindow.xaml.cs
│   │   ├── TrayController.cs
│   │   ├── MemoryCleanupService.cs   # 唯一业务状态与清理门闩
│   │   ├── MemoryCleanupModels.cs
│   │   ├── WindowsMemory.cs          # GlobalMemoryStatusEx / EmptyWorkingSet
│   │   ├── HelperLauncher.cs         # 按次 runas 启动 Helper 并解析退出码
│   │   └── SimpleLog.cs              # %LocalAppData%\DesktopTools\logs\app.log
│   └── DesktopTools.Helper/      # 提升 Helper（固定 cache-flush 协议）
│       └── Program.cs            # 日志：%LocalAppData%\DesktopTools\logs\helper.log
└── tests/
    └── DesktopTools.Tests/       # xunit 单元测试
```

主程序构建后会把 Helper 复制到：

```text
src\DesktopTools\bin\<Configuration>\net10.0-windows\helper\DesktopTools.Helper.exe
```

`dotnet run` / F5 / 发布目录使用同一布局。主程序始终从该固定相对路径启动 Helper。

## 日常开发

| 场景 | 命令 |
| --- | --- |
| 整包编译 | `dotnet build DesktopTools.sln` |
| 只编主程序（需已存在 helper 产物） | `dotnet build src\DesktopTools\DesktopTools.csproj` |
| 只编 Helper | `dotnet build src\DesktopTools.Helper\DesktopTools.Helper.csproj` |
| 跑测试 | `dotnet test tests\DesktopTools.Tests\DesktopTools.Tests.csproj` |
| Release 编译 | `dotnet build DesktopTools.sln -c Release` |
| 用 VS 开发 | 打开 `DesktopTools.sln`，把 `DesktopTools` 设为启动项目，F5 |

**注意：** 不要只 `dotnet build src\DesktopTools` 而跳过 Helper。主工程没有显式 `ProjectReference` 到 Helper；若 `helper\` 尚不存在，复制目标会静默跳过，运行时清理会报 Helper not found。整包 `dotnet build DesktopTools.sln` 一般会把三个工程都编出来。

## 发布

```powershell
dotnet publish src\DesktopTools.Helper\DesktopTools.Helper.csproj -c Release -r win-x64 --self-contained true -o artifacts/win-x64/helper
dotnet publish src\DesktopTools\DesktopTools.csproj -c Release -r win-x64 --self-contained true -o artifacts/win-x64
```

发布为目录产物，Helper 位于主程序目录下的 `helper` 子目录。V1 不引入安装器或单文件提取。

## 日志位置

| 进程 | 路径 |
| --- | --- |
| 主程序 | `%LocalAppData%\DesktopTools\logs\app.log` |
| Helper | 其所在用户的 `%LocalAppData%\DesktopTools\logs\helper.log` |

两进程写不同文件；日志写入失败不会改变清理结果。

## 常见问题

### restore 一直卡住 / 超时

`api.nuget.org` 在部分网络环境下 v3 索引会超时。可在用户配置里改用 v2 源：

编辑或创建 `%AppData%\NuGet\NuGet.Config`：

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="nuget.org" value="https://www.nuget.org/api/v2/" />
  </packageSources>
</configuration>
```

然后重试：

```powershell
dotnet nuget locals all --clear
dotnet restore DesktopTools.sln --disable-parallel
```

### `Value cannot be null. (Parameter 'path1')`

NuGet 解析机器级路径失败。检查环境变量是否缺失：

```powershell
[Environment]::GetEnvironmentVariable('ProgramFiles')
```

若为空，当前会话临时补上后重试：

```powershell
$env:ProgramFiles = 'C:\Program Files'
$env:ProgramW6432 = 'C:\Program Files'
dotnet restore DesktopTools.sln
```

### SDK 版本不满足

若只装了 .NET 8/9，`global.json` 会拒绝构建。安装 .NET 10 SDK 后再执行。

### 一键清理提示 Helper not found

先整包编译一次，确认：

```text
...\net10.0-windows\helper\DesktopTools.Helper.exe
```

存在。

### 清理时弹 UAC / 取消后部分成功

System File Cache 清理需要提升权限，按次请求 UAC。用户取消时该项记为 Skipped(UserCanceled)，若自身 Working Set 已成功则整体为 PartialSuccess，属预期行为。

### 单元测试覆盖不到真实 UAC

单元测试用可替换依赖，不触发真实提升。UAC 同意/拒绝、父进程崩溃、看门狗超时等需在 Windows 11 x64 实机手工验证。

## 技术方向

默认目标平台为 Windows 11。V1 固定为 C# / WPF、`net10.0-windows`（.NET 10 LTS），锁定 SDK 见 `global.json`。

清理范围（V1 固定，不得默默扩大）：

- 仅主程序自身 Working Set（`EmptyWorkingSet`）
- System File Cache（`SetSystemFileCacheSize((SIZE_T)-1, (SIZE_T)-1, 0)`，按次 UAC）

明确不做：Standby List、Modified Page List、第三方进程 Working Set、TerminateProcess、自动/定时/阈值清理。

## 开发约定

协作方式、修改原则、完成标准等见 [docs/dev-guidelines.md](docs/dev-guidelines.md)。

## 实施计划

当前功能的实施计划见 [docs/plans/windows-memory-cleanup-implementation-plan.md](docs/plans/windows-memory-cleanup-implementation-plan.md)。
