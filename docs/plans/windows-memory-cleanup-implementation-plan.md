# Windows Memory Cleanup Implementation Plan

> 本文为待实施计划；修订本文不代表授权创建工程、运行清理、构建或提交代码。后续实施须由用户另行授权。

**Goal:** Windows 11 普通权限托盘应用，手动清理自身 Working Set 与 System File Cache。

**Architecture:** 主程序持有唯一业务状态及清理入口；短生命周期 Helper 仅执行固定的高权限缓存操作，通过退出码返回结果。

**Tech Stack:** 空项目默认采用 C# / WPF、Windows Forms NotifyIcon、Windows 官方 Win32 API 和独立控制台 Helper。实施前选定当时受支持的 .NET LTS SDK 并固定版本；V1 发布目标为 Windows 11 x64。

## 1. Goal

在保持现有仓库行为和结构的前提下，实现 Windows 11 手动一键内存清理功能。

本次已确认的核心范围：

- 应用长期驻留系统托盘。
- 用户手动触发“一键清理”。
- 同一时间只允许执行一个清理任务。
- 允许对主程序自身执行 Working Set Trim。
- 允许清理 **System File Cache**。
- **不清理 Standby List**。
- 不结束第三方进程。
- 不关闭用户应用。
- 不强制 Trim 第三方进程 Working Set。
- 不做自动、定时或阈值触发清理。
- 需要高权限时按需申请 UAC。
- 主程序不长期以管理员权限运行。
- 清理结果必须区分 Success / PartialSuccess / Failed。
- 系统状态和数据只有一个权威来源。
- Memory Cleanup 是当前第一个 Feature，但不得借此提前建设插件系统或通用扩展框架。
- 不进行与本需求无关的重构。

---

## 2. Implementation Constraints

实施时必须遵守：

1. 先阅读真实仓库，再修改代码。
2. 优先复用现有实现。
3. 优先最小修改。
4. 不建立与现有代码平行的第二套机制。
5. UI、Tray、Helper 不分别维护独立的内存业务状态。
6. 不因为“未来可能扩展”引入没有当前收益的抽象层。
7. 不默默扩大内存清理范围。
8. 不把系统可用内存变化全部归因于本工具。
9. 所有失败路径必须有明确结果，不能将失败伪装为成功。
10. 与本需求无关的现有行为保持不变。
11. 未经用户明确授权，不执行 git add、git commit、git reset、git restore --staged；不得把阶段完成视为提交授权。
12. 新代码只增加精准、必要的中文注释，不添加重复代码含义的注释。

---

## 3. Phase 0 — Read and Map the Real Repository

开始修改前必须先完成只读检查。

定位以下真实实现：

- Solution / Project 文件。
- 应用入口。
- 主窗口。
- Tray / NotifyIcon。
- 单实例机制。
- 当前状态管理。
- Windows Native / Interop / PInvoke。
- 当前内存信息读取逻辑。
- 当前内存清理逻辑。
- UAC / privilege / elevation 逻辑。
- 日志。
- 测试项目。

形成内部映射：

```text
Application entry   -> <真实文件>
Tray lifecycle      -> <真实文件>
Single instance     -> <真实文件>
Feature/App state   -> <真实文件>
Memory query        -> <真实文件或不存在>
Memory cleanup      -> <真实文件或不存在>
Privilege/elevation -> <真实文件或不存在>
Native API          -> <真实文件或不存在>
Logging             -> <真实文件或不存在>
Tests               -> <真实文件或不存在>
```

规则：

- 已有能力可以承载需求时，直接扩展现有实现。
- 不得仅因为本 Plan 中出现某个模块名，就创建同名文件。
- 不得先重构仓库再实现功能。
- 如果现有代码与本 Plan 冲突，只修改与本需求直接相关的冲突部分。

---

### 3.1 当前基线与空项目分支

2026-09-09 只读检查：当前目录只有 `readme.txt` 和本计划，没有源码、Solution、Tray、日志或测试，也不是 Git 仓库。上述能力均应标记为不存在，不得报告已复用。实施前重新检查，若出现真实工程，优先复用并更新映射。

仍为空项目时，按以下最小方案实施，无需先建立通用 Feature 框架：

| 计划路径 | 职责 |
| --- | --- |
| `DesktopTools.sln`、`global.json` | 工程入口及固定 SDK |
| `src/DesktopTools/DesktopTools.csproj` | WPF 主程序，asInvoker，引用 Windows Forms 托盘能力 |
| `src/DesktopTools/App.xaml.cs`、`MainWindow.xaml`、`MainWindow.xaml.cs` | 显式退出生命周期、单实例、主窗口 |
| `src/DesktopTools/TrayController.cs` | 托盘菜单、打开窗口、清理及退出转发 |
| `src/DesktopTools/MemoryCleanupService.cs`、`MemoryCleanupModels.cs` | 唯一状态、执行门闩、流程和结果模型 |
| `src/DesktopTools/WindowsMemory.cs`、`HelperLauncher.cs` | 统一查询、自身 Trim、提升启动及退出码解析 |
| `src/DesktopTools.Helper/DesktopTools.Helper.csproj`、`Program.cs` | 固定缓存操作、权限处理、退出码返回；不引用 UI 工程 |
| `tests/DesktopTools.Tests/` | 流程、聚合、并发及故障注入测试 |

Native 声明可以放在实际使用的模块内；不为对应表格强行拆出更多层。主程序和 Helper 各自使用进程内轻量日志，禁止引入新的日志框架或并发写同一个无锁日志文件。

实施步骤：先固定 SDK 和测试依赖版本，再建立可启动/关闭的托盘骨架及单实例，随后按下列阶段加入查询、清理和 Helper。默认每用户、每会话一个主程序，使用会话内命名互斥量；第二次启动直接退出，不增加激活 IPC。多个登录会话不属于此单实例范围。

关闭窗口采用隐藏窗口；只有托盘明确退出才结束应用。内存信息在启动、窗口显示和手动刷新时读取，V1 不新增后台轮询定时器。

计划验证命令（仅在获得实施授权后执行）：

```powershell
dotnet restore DesktopTools.sln
dotnet build DesktopTools.sln -c Release --no-restore
dotnet test tests/DesktopTools.Tests/DesktopTools.Tests.csproj -c Release
dotnet publish src/DesktopTools/DesktopTools.csproj -c Release -r win-x64 --self-contained true -o artifacts/win-x64
dotnet publish src/DesktopTools.Helper/DesktopTools.Helper.csproj -c Release -r win-x64 --self-contained true -o artifacts/win-x64/helper
```

发布为目录产物，Helper 位于主程序目录下固定的 `helper` 子目录；V1 不引入安装器、自动更新或单文件提取机制。在无开发 SDK 的 Windows 11 x64 环境验证发布目录。ARM64 原生版本不在本轮验收范围。

## 4. Phase 1 — Establish the Single Authoritative Memory Cleanup State

找到或建立 Memory Cleanup Feature 的唯一业务状态来源。

至少需要表达：

```text
CurrentSnapshot
IsCleaning
LastCleanupResult
```

CurrentSnapshot 允许 unavailable，并附查询错误及是否过期。清理结果中的 Before/After 是不可变的历史证据，不是第二份实时状态。状态发布统一在 UI Dispatcher 上完成；查询请求使用递增序号，较旧请求不得覆盖较新采样。清理期间普通刷新不启动新采样。

约束：

- Tray 不维护第二份内存业务状态。
- Main Window 不维护第二份内存业务状态。
- Elevated Helper 不维护业务状态。
- Memory query 层只负责读取，不成为 UI 状态仓库。
- 所有 UI 展示从同一份最新 Feature State 获取数据。

`MemorySnapshot` 至少支持当前功能真正需要的数据：

```text
Timestamp
TotalPhysicalMemory
AvailablePhysicalMemory
MemoryLoad
```

只有现有 UI 或本功能实际需要时，才增加其他指标。

---

## 5. Phase 2 — Unify System Memory Query

系统内存读取必须集中到现有最合适的 Platform / System / Memory 模块中。

对外提供统一读取入口，语义类似：

```text
QueryMemory() -> MemorySnapshot / Result<MemorySnapshot>
```

要求：

- Tray 不直接读取 Windows 内存 API。
- Main Window 不直接读取 Windows 内存 API。
- Cleanup 流程不能维护独立的长期 Snapshot 副本。
- Before 核心查询失败时中止清理；After 查询失败不改变已完成操作的真实结果，但必须显示数据不可用及查询错误，不得显示完整测量成功。
- 非核心扩展指标读取失败时，可以降级，但必须明确为 unavailable。
- 不生成假数据或占位值冒充真实状态。

---

## 6. Phase 3 — Implement the Single Cleanup Execution Flow

所有入口必须调用同一个清理流程。

标准流程：

```text
User clicks Clean
        ↓
Atomically acquire cleanup gate and check exit flag
        ↓
If already Cleaning -> reject/ignore duplicate request
        ↓
IsCleaning = true
        ↓
Read BeforeSnapshot
        ↓
Execute current-process Working Set cleanup
        ↓
Execute System File Cache cleanup
        ↓
Read AfterSnapshot
        ↓
Build CleanupResult
        ↓
Update authoritative Feature State
        ↓
IsCleaning = false
```

要求：

- `IsCleaning` 必须在所有异常路径恢复。
- 使用 `finally` 或现有项目等价机制保证不会永久卡在 Cleaning。
- 不允许并发运行两个清理任务。
- Tray 和 Main Window 必须复用这个入口，不得复制清理代码。

执行门闩必须在第一次 await 前原子取得（例如非阻塞 SemaphoreSlim.Wait(0)）；失败立即拒绝，不排队。IsCleaning 仅是门闩状态的 UI 投影，不能用普通布尔变量的先检查后赋值代替门闩。退出标志与入口准入由同一串行机制处理。

Native 操作和提升等待不得同步阻塞 UI 线程。异常进入结果构造流程，保留已经取得的操作结果；仅在本次执行以及可能仍执行的 Helper 都已结束后释放门闩。结果发布和通知分别隔离异常，通知失败不能破坏释放流程。

---

## 7. Phase 4 — Current Process Working Set Cleanup

允许对 **主程序自身进程** 调用 EmptyWorkingSet。由主程序执行，不能在 Helper 中把 Helper 自身误当成清理目标。Trim 不等于释放已分配的堆内存，不保证持续降低占用或无性能影响；不增加强制 GC。

范围：

```text
Current Process only
```

禁止：

```text
EnumProcesses
EmptyWorkingSet(otherProcess)
Process-wide mass trimming
```

要求：

- 当前进程 Working Set 操作结果单独记录。
- 成功依据来自实际 API 返回结果。
- 失败时保留系统错误信息。
- 不根据 Before / After 的 Available Memory 差值判断该操作是否成功。

---

## 8. Phase 5 — System File Cache Cleanup

V1 只允许清理：

```text
System File Cache
```

明确禁止：

```text
Standby List
Modified Page List
第三方进程 Working Set
TerminateProcess
未确认的 Memory List 清理
未文档化 Nt* 内存清理路径
```

System File Cache 清理应使用已经确认的 Windows 官方能力。

固定调用 `SetSystemFileCacheSize((SIZE_T)-1, (SIZE_T)-1, 0)`，仅调用一次。C# 互操作的 SIZE_T 使用匹配进程指针宽度的无符号类型，全位为 1；不得以 32 位常量截断 64 位参数。不得设置低缓存上限、启用/禁用硬限制或失败后退回 Nt* 路径。

Helper 打开自身令牌并启用 SeIncreaseQuotaPrivilege；逐项检查令牌操作、LookupPrivilegeValue、AdjustTokenPrivileges 及缓存 API。AdjustTokenPrivileges 非零仍须检查 ERROR_NOT_ALL_ASSIGNED，不能把提升成功当作特权启用成功。及时捕获 Win32 错误，释放令牌句柄；不请求 SeDebugPrivilege 等无关权限。

验收时使用 GetSystemFileCacheSize 比较调用前后的限制设置及 Flags，确认没有留下策略修改；这项诊断不用于计算释放量。若目标系统上不能满足一次性操作且不改变策略的约束，报告阻断，不改用其他清理手段。

官方依据：[SetSystemFileCacheSize](https://learn.microsoft.com/en-us/windows/win32/api/memoryapi/nf-memoryapi-setsystemfilecachesize)、[AdjustTokenPrivileges](https://learn.microsoft.com/en-us/windows/win32/api/securitybaseapi/nf-securitybaseapi-adjusttokenprivileges)、[EmptyWorkingSet](https://learn.microsoft.com/en-us/windows/win32/api/psapi/nf-psapi-emptyworkingset)。

关键约束：

- 只做一次性清理。
- 不长期修改 Windows 缓存策略。
- 不人为维持极低 System File Cache 上限。
- 不将 System File Cache 扩展解释为 Standby List。

如果实际调用需要高权限：

```text
Main Process
    ↓
Request elevated execution
    ↓
Elevated Helper
    ↓
Execute System File Cache operation
    ↓
Return explicit operation result
    ↓
Helper exits
```

主程序不得因为此功能永久以管理员权限运行。

---

## 9. Phase 6 — Privilege and Elevated Helper

优先检查仓库是否已有可复用的 UAC / elevation 机制。

只有以下条件同时成立时才新增 Elevated Helper：

1. 现有机制无法满足本操作；
2. System File Cache 清理确实需要提升权限；
3. 新增 Helper 是完成需求的最小必要修改。

Helper 职责必须保持非常窄：

```text
Receive one supported operation request
        ↓
Execute
        ↓
Return Success / Failed + error
        ↓
Exit
```

Helper 不得：

- 保存用户配置。
- 保存 MemoryCleanupState。
- 长期驻留。
- 运行定时任务。
- 承担普通 UI。
- 演变成通用后台服务。
- 提前设计插件执行框架。

必须处理：

```text
UAC canceled
Elevation launch failed
Helper failed to start
Helper abnormal exit
System File Cache API failed
Invalid/missing helper response
```

Helper 的实际执行结果是该高权限操作成功与否的权威依据。

不能通过：

```text
AfterAvailable > BeforeAvailable
```

来推断 Helper 执行成功。

---

### 9.1 Helper 启动、返回及信任边界（Phase 6 补充）

- 本空项目使用独立 Helper EXE；主程序 asInvoker，Helper 按次 runas 提升，不安装服务。
- 主程序从 AppContext.BaseDirectory 下固定绝对路径启动已发布 Helper，不通过 PATH、当前工作目录或用户配置寻找程序。Helper 不动态加载用户指定 DLL、脚本或插件。
- Helper 只接受固定版本的缓存清理模式与用于生命周期监测的父进程标识；拒绝任意命令、目标进程、输出路径、缓存大小参数。父进程标识只能用于等待/查询，不能用于操作该进程。
- 父进程 PID 配合创建时间校验，避免 PID 重用；无法建立父进程监测时不执行缓存操作。以另一管理员账号提升也必须覆盖此行为。
- 不使用临时结果文件、标准输出重定向或命名管道。主程序持有本次提升产生的真实进程句柄，通过该句柄等待并读取退出码；不能以 PID 搜索到的其他进程或日志内容判定成功。
- 固定退出码协议：0 表示缓存 API 成功；10 参数/协议不支持；11 父进程身份或存活验证失败；12 特权准备失败；13 缓存 API 失败；14 Helper 内部异常。其他退出码均视为异常终止；没有有效进程句柄不能报告成功。
- Helper 日志保留具体 Win32 错误与失败阶段，主程序结果保留退出码及明确原因。跨账号提升时日志可能在管理员账号目录，不承诺主程序能读取。日志写入失败不得覆盖缓存 API 已取得的结果。
- 标准用户使用其他管理员凭据必须实测；不得为打通路径增加 Everyone 可写共享目录或放宽文件 ACL。

这套协议仅返回一个固定操作的结果，不承载任意任务分派。绝对路径不能防御同权限进程篡改可写发布目录；部署时不得向不可信用户开放目录写权限，不宣称已实现对主程序自身账号恶意代码的隔离。

## 10. Phase 7 — Cleanup Result Model

优先复用仓库已有 Result / Error 模型。

如果现有模型不足，最小补充能够表达以下语义：

```text
OverallStatus:
    Success
    PartialSuccess
    Failed

BeforeSnapshot
AfterSnapshot

OperationResults:
    CurrentProcessWorkingSet
    SystemFileCache
```

每个 Operation 至少能够表达：

```text
Success
Failed
Skipped
```

并携带必要错误原因。

结果另含 BeforeQueryError / AfterQueryError，及失败阶段。通信或等待失效用 Failed(ResultUnavailable) 表示“不能确认结果”，不得描述成“确定没有执行”。尚未启动的操作才可标记 Skipped；不自动重试结果未知的操作。

### OverallStatus 判定

```text
所有要求执行的操作成功
    -> Success

至少一个操作成功，且至少一个操作失败或被跳过
    -> PartialSuccess

没有任何要求执行的操作成功
    -> Failed
```

固定要求执行的操作就是上述两项，不能因某项被跳过而将其移出聚合集合。Before 查询失败时两项均为 Skipped(BeforeQueryFailed)，OverallStatus 为 Failed。

After 查询失败时 OverallStatus 仍仅按两项操作聚合；两项成功时显示“清理操作成功，清理后内存读取失败”。AfterSnapshot unavailable，同时附查询错误，不能把旧 CurrentSnapshot 当作本次 AfterSnapshot。

### UAC 取消

如果：

```text
CurrentProcessWorkingSet = Success
SystemFileCache = Skipped(UserCanceled)
```

则：

```text
OverallStatus = PartialSuccess
```

不得把前面已经完成的普通清理抹掉。

---

## 11. Phase 8 — Before / After Data Semantics

清理前后必须重新通过统一 Memory Query 获取 Snapshot。

允许展示：

```text
清理前可用内存
清理后可用内存
系统可用内存变化
```

禁止直接声称：

```text
AfterAvailable - BeforeAvailable
=
本工具精确释放内存
```

原因不是本 Plan 需要解释给用户，而是实现层面必须避免错误归因。

操作成功与否必须依据：

```text
API / Helper execution result
```

而不是依据内存数字是否下降或上升。

以下均属于合法结果：

```text
AfterAvailable > BeforeAvailable
AfterAvailable == BeforeAvailable
AfterAvailable < BeforeAvailable
```

第三种情况不能显示“释放 -X MB”。

---

## 12. Phase 9 — Tray and Main UI Integration

现有 Tray 和 Main Window 必须调用同一个 Cleanup 入口。

Cleaning 期间：

- 禁止第二次清理。
- UI / Tray 应反映“正在清理”。
- 不创建第二个后台清理任务。

完成后：

- 展示真实 OverallStatus。
- 必要时展示具体 Operation 状态。
- Success / PartialSuccess / Failed 不得混淆。

关闭主窗口：

```text
Window closed
    ↓
Application remains in Tray
```

从 Tray 明确退出：

```text
Tray Exit
    ↓
Application exits completely
```

如果这些行为仓库已经实现：

- 保持原行为。
- 不重写生命周期。

---

## 13. Phase 10 — Single Instance

如果仓库已有单实例实现：

- 原样复用。
- 不引入第二套机制。

必须保证：

```text
First instance running
        +
Second launch
        ↓
No second independent tray process
No second MemoryCleanupState
No parallel cleanup owner
```

第二实例如何通知或激活第一实例，保持仓库现有行为。

只有现有仓库完全没有单实例机制时，才实现满足当前需求的最简单可靠方案。

---

## 14. Phase 11 — Exit During Cleanup

必须明确处理：

```text
Cleaning
    ↓
User requests Exit
```

要求：

- 退出过程中不再接受新的 Cleanup。
- 不遗留无必要的 Elevated Helper。
- 不让主程序退出后留下长期高权限后台进程。
- 已经取得的 Operation Result 不应被伪造。
- 不为了此路径建立复杂的新状态机。

具体策略：退出请求先关闭清理准入、隐藏窗口并保留必要进程资源，等待正在执行的清理收尾后退出。正常退出期间保留托盘“正在退出”反馈，不能直接丢弃提升任务。

- 尚在等待 UAC 用户选择时不把超时当作取消，不重复弹窗；退出等待该次选择完成。只有系统返回用户取消才记录 UserCanceled。不得声称可以强制关闭系统 UAC 对话框。
- Helper 启动后等待预算为 30 秒；超时显示 ResultUnavailable 并进入收尾，不释放门闩供再次清理，也不自动重试。
- Helper 自身设置独立 30 秒看门狗并监测父进程；父进程退出、监测失败或预算到期时终止自身，避免普通权限主程序依赖终止高权限进程。看门狗只管理本次 Helper 寿命，不是自动清理定时器。
- 看门狗终止的退出码必须非零且区别于成功。若调用已执行但未成功回传，保留结果未知语义，不尝试回滚一次性 Trim。
- 主程序必须观察 Helper 实际退出后才清理句柄、释放门闩并完成退出；若异常环境下 Helper 仍未退出，显示无法完成退出的原因，不能声称已完整退出或重新开放清理。
- 主程序崩溃后，后来才获 UAC 批准的 Helper 也必须先验证父进程存活再执行；不匹配则立即退出。

---

## 15. Phase 12 — Error and Boundary Paths

以下路径必须有明确行为：

### Memory Query Before Failure

```text
BeforeSnapshot read failed
    ↓
Do not continue normal cleanup
    ↓
CleanupResult = Failed
    ↓
IsCleaning returns to false
```

### Current Process Working Set Failure

- 记录该 Operation 为 Failed。
- 根据后续 System File Cache 是否成功决定 OverallStatus。

### UAC Canceled

- SystemFileCache = Skipped(UserCanceled)。
- 不自动重复弹出 UAC。
- 如果前一个普通操作成功，OverallStatus = PartialSuccess。

### Elevation Launch Failure

- SystemFileCache = Failed。
- 主程序继续正常运行。

### Helper Execution Failure

- SystemFileCache = Failed。
- 保留错误原因。
- 不根据内存变化推断成功。

### AfterSnapshot Read Failure

- 已经完成的 Operation Result 必须保留。
- AfterSnapshot 标记 unavailable。
- 不将已执行成功的操作改成失败。
- UI 不显示伪造的清理后数据。

### Notification Failure

- 不改变 CleanupResult。
- 只记录通知层失败。

### Repeated Cleanup Request

```text
IsCleaning == true
    ↓
Do not start another cleanup
```

### No Visible Memory Gain

- 清理可以仍然是 Success。
- 不伪造释放数据。

### Available Memory Decreased

- 不显示负数“释放量”。
- 不把这种变化当作 Cleanup API 失败的依据。

---

## 16. Phase 13 — Logging

复用现有日志体系。

至少应能区分：

```text
Cleanup started
Before memory query failed
Current-process trim succeeded
Current-process trim failed
Elevation requested
Elevation canceled
Helper launch failed
System File Cache cleanup succeeded
System File Cache cleanup failed
After memory query failed
Cleanup completed: Success
Cleanup completed: PartialSuccess
Cleanup completed: Failed
```

不要为了本功能引入新的日志框架。

日志中应保留必要的系统错误代码或错误原因，便于定位问题。

---

## 17. Expected Modification Scope

具体文件名必须由阅读真实仓库后确定。

预计只涉及与以下职责直接相关的现有文件或模块：

- Application entry / lifecycle。
- Tray。
- Single instance。
- Memory Cleanup Feature state。
- Memory query。
- Cleanup execution。
- Windows Native / PInvoke。
- UAC / privilege / elevation。
- Elevated Helper（仅在确有必要且现有机制不能复用时）。
- Cleanup result model。
- 与清理状态直接相关的 UI。
- Logging。
- Tests。

明确不应该修改：

- 与 Memory Cleanup 无关的现有功能。
- 与本需求无关的公共基础设施。
- 已稳定工作的模块，仅为了统一代码风格进行的重构。
- 插件系统。
- 通用 Feature Framework。
- File Cleanup。
- Registry Cleanup。
- Startup Manager。
- CPU / Network / Driver 优化。
- 自动清理。
- 定时清理。
- 阈值清理。
- Standby List 清理。
- 第三方 Working Set 清理。

---

## 18. Tests

### 18.1 Build

必须：

- 项目能够正常编译。
- 不新增明显编译警告。
- 原有相关测试通过。
- 新增必要测试通过。

---

### 18.2 Normal Flow

验证：

```text
Start application
    ↓
Tray exists
    ↓
Read memory
    ↓
Manual Clean
    ↓
Current-process cleanup
    ↓
System File Cache cleanup
    ↓
Result shown
    ↓
Can clean again
```

---

### 18.3 Tray Lifecycle

验证：

```text
Close main window
    -> Tray remains

Open window again
    -> Uses same application state

Tray Exit
    -> Application fully exits
```

---

### 18.4 Single Instance

连续启动多次：

PASS 条件：

- 只有一个长期运行实例。
- 只有一个 Tray。
- 只有一份 Memory Cleanup 权威状态。

---

### 18.5 Duplicate Click

快速连续点击 Clean 多次。

PASS 条件：

```text
Only one cleanup execution
```

不得：

- 并发启动 Helper。
- 同时生成多份 CleanupResult。
- 出现状态错乱。

---

### 18.6 UAC Approved

普通权限启动主程序。

触发需要管理员权限的 System File Cache 清理。

用户允许 UAC。

PASS 条件：

- 主程序本身仍保持普通权限运行。
- System File Cache 操作实际执行。
- Helper 正常退出。
- 结果正确返回主程序。

---

### 18.7 UAC Rejected

触发清理后拒绝 UAC。

PASS 条件：

```text
Current-process operation:
    keep its real result

SystemFileCache:
    Skipped(UserCanceled)

Overall:
    PartialSuccess if current-process cleanup succeeded
```

并且：

- 不重复弹 UAC。
- 应用继续正常驻留。

---

### 18.8 Elevated Helper Failure

模拟或覆盖：

```text
Helper launch failure
Helper abnormal exit
Helper operation failure
Invalid response
```

PASS 条件：

- 不崩溃。
- 不显示完全成功。
- 不遗留长期 Helper。
- IsCleaning 能恢复。

---

### 18.9 Memory Query Failure

覆盖：

```text
BeforeSnapshot failure
AfterSnapshot failure
```

PASS 条件：

- Before 失败时不继续正常清理。
- After 失败时保留真实 Operation Result。
- 不生成假 Snapshot。
- 不永久停留在 Cleaning。

---

### 18.10 No Visible Memory Difference

清理后 Available Memory 几乎不变。

PASS 条件：

- 实际 API 成功时仍可报告操作成功。
- 不伪造较大的释放数字。
- 不把“变化小”当作 API 失败。

---

### 18.11 Available Memory Decreases

测试过程中系统发生其他内存活动，导致：

```text
AfterAvailable < BeforeAvailable
```

PASS 条件：

- 不显示负数“释放量”。
- 不因此将成功的 API 操作判定为 Failed。

---

### 18.12 Safety Boundary

检查实现不存在未经批准的行为。

PASS 条件：

```text
No TerminateProcess for cleanup
No third-party EmptyWorkingSet
No Standby List cleanup
No Modified Page List cleanup
No automatic cleanup timer
No memory-threshold cleanup
No undocumented memory-list cleanup added by this feature
```

---

### 18.13 Regression

验证与本需求直接相邻的原有行为：

- 应用启动。
- 主窗口打开/关闭。
- Tray。
- 单实例。
- 配置读取和保存（如果原有）。
- 日志。
- 应用退出。

PASS 条件：

- 原有行为没有因为本功能产生明显回归。
- 没有与本需求无关的重构导致测试变化。

### 18.14 补充故障矩阵与验证边界

自动测试使用可替换的查询、Trim、Helper 启动/等待依赖，不在单元测试中触发 UAC 或真实缓存清理：

- 覆盖两项 Operation 的 Success / Failed / Skipped 聚合组合，以及 Before 失败、After 失败和结果未知。
- 同时从两个任务调用入口，只执行一次、不排队；退出与清理竞争时不接受退出后的新任务。
- 旧查询晚到不能覆盖新快照，通知和日志异常不能导致永久 Cleaning。
- Helper 启动失败、未知退出码、超时、收尾未结束：不报成功、不自动重试、不开放第二个 Helper。

Windows 11 x64 实机验证并记录操作、真实退出码/错误、实际现象及 PASS / FAIL / MANUAL_REQUIRED：

- 普通管理员账号同意/拒绝 UAC，标准用户输入另一管理员凭据；特权缺失或启用失败。
- UAC 等待中退出、Helper 运行中退出、父进程崩溃、Helper 超时自退，以及父进程退出后才批准 UAC。
- 缓存调用前后限制与 Flags 不发生本功能造成的持久修改；API 成功不代表有可观测内存收益。
- 发布目录包含 Helper 及全部依赖；无 SDK 环境可启动，中文/空格路径可提升，Helper 缺失时明确失败。
- 多次启动只有同一用户同一会话的一份托盘；关闭/重开窗口使用同一状态；退出后进程和托盘结束。

构建、模拟测试不能替代 UAC、真实 Win32 API 和进程生命周期验证。未实测的项目保留 MANUAL_REQUIRED，不得勾选为完成。当前没有原有测试，“原有测试通过”应记录为不适用，不能虚报。

---

## 19. Acceptance Criteria

以下全部满足才算完成：

- [ ] 已根据空项目基线建立并记录最小工程、固定 SDK 及发布方式。
- [ ] 特权启用检查、固定退出码协议及未知结果语义已经验证。
- [ ] 并发准入、Helper 超时、退出中清理和父进程崩溃均有验证证据。
- [ ] After 查询失败仍保留操作结果，UI 同时明确数据不可用。
- [ ] 发布目录在无 SDK 的 Windows 11 x64 环境验证通过。

- [ ] Codex 在修改前已阅读并理解真实仓库相关实现。
- [ ] 优先复用了现有架构和机制。
- [ ] Windows 11 下应用正常启动。
- [ ] 应用能够长期驻留系统托盘。
- [ ] 重复启动不会产生多个独立托盘实例。
- [ ] 系统内存状态来自统一权威读取入口。
- [ ] Memory Cleanup 只有一份权威业务状态。
- [ ] 用户可以手动执行一键清理。
- [ ] 同一时间只能执行一个清理任务。
- [ ] 能处理当前工具自身 Working Set。
- [ ] 能执行 System File Cache 清理。
- [ ] **没有实现 Standby List 清理。**
- [ ] 没有强制 Trim 第三方进程 Working Set。
- [ ] 没有结束第三方进程。
- [ ] 没有自动、定时或阈值清理。
- [ ] 需要高权限的操作按需申请 UAC。
- [ ] 主程序不会因本功能长期管理员运行。
- [ ] Elevated Helper 如存在，只承担最小必要高权限操作并及时退出。
- [ ] UAC 取消能够正确处理。
- [ ] Success / PartialSuccess / Failed 判定明确。
- [ ] 单个 Operation 能区分 Success / Failed / Skipped。
- [ ] 清理结果以实际 API / Helper 结果为依据。
- [ ] 不将 Before / After Available Memory 差值作为精确释放归因。
- [ ] 清理无明显内存变化时不会伪造效果。
- [ ] 核心异常路径不会导致应用崩溃或永久停留 Cleaning。
- [ ] 关闭主窗口后 Tray 仍正常。
- [ ] Tray Exit 后应用和不必要的 Helper 完整退出。
- [ ] 项目正常编译。
- [ ] 原有相关测试通过。
- [ ] 新增必要测试通过。
- [ ] 与本功能无关的代码没有被重构。
- [ ] 与本功能无关的现有行为保持兼容。

---

## 20. Codex Completion Report

实施完成后必须输出：

1. 实际修改文件列表。
2. 每个文件的修改原因。
3. 修改前实际仓库机制摘要。
4. 最终 Memory Cleanup 数据流。
5. 最终权限提升数据流。
6. 实际执行的清理操作列表。
7. 明确确认：
   - Current Process Working Set：是否实现。
   - System File Cache：是否实现。
   - Standby List：未实现。
   - Third-party Working Set：未实现。
   - Process termination：未实现。
8. Success / PartialSuccess / Failed 的实际判定方式。
9. UAC 拒绝时的实际行为。
10. Build 结果。
11. Test 结果。
12. 已知限制。
13. 是否存在任何偏离本 Plan 的实现；如有，必须明确说明原因，不得默默改变设计。
