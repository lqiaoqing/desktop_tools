# Desktop Tools

Windows 11 桌面工具集合，覆盖效率工具、AI 工具、开发者工具、文件处理工具和自动化工具。

目标不是做 Demo，而是尽量形成真正可运行、可测试、可发布、可持续维护的 Windows 产品。

## 当前状态

已实现第一个功能：Windows 11 托盘常驻一键内存清理（C# / WPF / .NET 10）。

- 主程序 `src/DesktopTools`：asInvoker，单实例，托盘，主窗口，统一内存查询与清理流程。
- 提升 Helper `src/DesktopTools.Helper`：固定 `cache-flush` 协议，按需 UAC 清理 System File Cache。
- 测试 `tests/DesktopTools.Tests`：聚合、并发、Before/After 失败、Helper 故障等单元测试。

不清理 Standby List，不结束第三方进程，不强制 Trim 第三方 Working Set。

## 技术方向

默认目标平台为 Windows 11。V1 固定为 C# / WPF、`net10.0-windows`（.NET 10 LTS），锁定 SDK 见 `global.json`。

## 开发约定

协作方式、修改原则、完成标准等见 [docs/dev-guidelines.md](docs/dev-guidelines.md)。

## 实施计划

当前功能的实施计划见 [docs/plans/windows-memory-cleanup-implementation-plan.md](docs/plans/windows-memory-cleanup-implementation-plan.md)。