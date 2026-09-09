# Desktop Tools

Windows 11 桌面工具集合，覆盖效率工具、AI 工具、开发者工具、文件处理工具和自动化工具。

目标不是做 Demo，而是尽量形成真正可运行、可测试、可发布、可持续维护的 Windows 产品。

## 当前状态

仓库处于早期阶段，尚未包含可运行源码。

规划中的第一个功能：托盘常驻的一键内存清理（清理自身 Working Set 与 System File Cache，按需 UAC）。

## 技术方向

默认目标平台为 Windows 11。具体技术栈按功能需求选择，不预先锁定框架（可考虑 WPF / WinUI 3 / .NET、Tauri、Electron、Qt、Python 等）。

## 开发约定

协作方式、修改原则、完成标准等见 [docs/dev-guidelines.md](docs/dev-guidelines.md)。

## 实施计划

当前功能的实施计划见 [docs/plans/windows-memory-cleanup-implementation-plan.md](docs/plans/windows-memory-cleanup-implementation-plan.md)。