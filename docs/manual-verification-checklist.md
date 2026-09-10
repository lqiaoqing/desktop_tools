# Windows 11 x64 实机验证清单

单元测试使用可替换依赖，**不能**替代真实 UAC、Win32 API 与进程生命周期验证。
本清单按计划 `docs/plans/windows-memory-cleanup-implementation-plan.md` 第 18.14 / 19 节整理，用于发布前手工验收。

每项记录：**操作 / 期望 / 实际现象 / 结论（PASS / FAIL / MANUAL_REQUIRED）**。

日志位置：

- 主程序：`%LocalAppData%\DesktopTools\logs\app.log`
- Helper：执行提升时所在用户的 `%LocalAppData%\DesktopTools\logs\helper.log`（跨账号提升时可能不在当前用户目录）

---

## 0. 环境准备

| 项 | 记录 |
| --- | --- |
| OS 版本 | Windows 11 x64 ______ |
| 账号类型 | 管理员 / 标准用户 ______ |
| SDK | 无 SDK 发布目录 / 有 SDK ______ |
| 构建方式 | `.\build.ps1` 或 `.\build.ps1 -Publish` |

无 SDK 环境请使用：

```powershell
.\build.ps1 -Publish
# 运行 artifacts\win-x64\DesktopTools.exe
```

---

## 1. 启动 / 托盘 / 单实例

### 1.1 冷启动

1. 运行 `DesktopTools.exe`
2. **期望**：主窗口出现；托盘图标出现；显示总内存/可用内存/内存负载
3. **期望**：未弹出 UAC

### 1.2 关窗驻留

1. 关闭主窗口（X）
2. **期望**：托盘仍在；进程未退出
3. 从托盘「打开主窗口」
4. **期望**：同一窗口重新显示，内存数据为**刚刷新**的值（非半小时前旧值）

### 1.3 单实例

1. 再次启动 `DesktopTools.exe`
2. **期望**：不出现第二个托盘、第二个主窗口、第二份清理状态
3. **期望**：第二次进程迅速退出

### 1.4 托盘退出

1. 托盘「退出」
2. **期望**：进程与托盘均消失（任务管理器无 DesktopTools.exe）

**结果**：□ PASS　□ FAIL　备注：________

---

## 2. 正常一键清理（UAC 同意）

1. 普通权限启动主程序（任务管理器确认**不是**管理员运行）
2. 点击「一键清理」
3. **期望**：出现 UAC；选择「是」
4. **期望**：
   - 状态为「成功」或明确展示两项操作结果
   - 自身 Working Set = 成功
   - System File Cache = 成功
   - 主程序仍为普通权限
   - Helper 进程结束后消失
5. 查看 helper.log，应类似：
   - `Cache size before: min=... max=... flags=...`
   - `SetSystemFileCacheSize succeeded`
   - `Cache size after: min=... max=... flags=...`
6. **验收（策略未残留）**：比较 before/after 的 min/max/flags  
   - 若一致或与系统默认一致 → PASS  
   - 若本功能导致持久修改 → FAIL（阻断）

> 不要求 After 可用内存必须上升。可用内存不变甚至下降都属合法。

**结果**：□ PASS　□ FAIL　备注：________

---

## 3. UAC 拒绝

1. 触发一键清理，UAC 选「否」
2. **期望**：
   - 自身 Working Set 仍为成功（若已执行）
   - System File Cache = 已跳过（UserCanceled）
   - 整体 = 部分成功
   - **不**自动再弹一次 UAC
   - 应用继续驻留，可再次清理

**结果**：□ PASS　□ FAIL　备注：________

---

## 4. 并发与门闩

1. 快速连续点击「一键清理」多次
2. **期望**：只出现一次 UAC/一次清理，无第二个 Helper
3. 清理进行中：
   - **期望**：按钮不可用 / 托盘显示「正在清理…」
4. 清理完成后再点一次
5. **期望**：可再次正常清理

**结果**：□ PASS　□ FAIL　备注：________

---

## 5. 退出与 Helper 生命周期

### 5.1 清理结束后退出

1. 清理完成后托盘退出
2. **期望**：立即退出，无残留 Helper

### 5.2 UAC 等待中退出

1. 触发清理，**不要**点 UAC
2. 尽快从托盘点「退出」
3. **期望**：
   - 应用等待本次 UAC 选择，不把等待当作取消
   - 你完成 UAC（同意或拒绝）后清理收尾，应用再退出
   - 无长期残留高权限进程

### 5.3 Helper 仍运行时退出（若可复现）

1. 在 Helper 运行期间触发退出
2. **期望**：主程序观察 Helper 实际退出后再结束；若超时仍未结束，提示「无法完成退出」，**不**假装已退出

**结果**：□ PASS　□ FAIL　备注：________

---

## 6. 父进程崩溃 / Helper 迟到批准

### 6.1 父进程被强制结束

1. 触发清理，弹出 UAC 后
2. 在任务管理器结束 `DesktopTools.exe`（父进程）
3. 再批准 UAC
4. **期望**：Helper 校验父进程失败（exit 11）或监测到父进程退出后自终止；**不**继续执行缓存清理

### 6.2 Helper 超时自退

1. 观察 helper.log 是否出现 `Watchdog self-exit`
2. **期望**：退出码非 0 且不是成功（15 或 11）
3. 主程序侧结果为 **ResultUnavailable**，不报告成功

**结果**：□ PASS　□ FAIL　备注：________

---

## 7. 标准用户 + 另一管理员凭据

1. 使用标准用户账号登录
2. 运行主程序，触发清理
3. UAC 输入**另一管理员**账号密码
4. **期望**：
   - 提升成功时缓存清理执行，结果返回主程序
   - 取消时整体部分成功
   - 未把 Everyone 可写目录或过宽 ACL 当作依赖

**结果**：□ PASS　□ FAIL / 不适用　备注：________

---

## 8. 发布目录 / 路径

### 8.1 无 SDK 启动

1. 将 `artifacts\win-x64` 拷到无 .NET SDK 的 Windows 11 x64
2. 运行 `DesktopTools.exe`
3. **期望**：可启动，`helper\DesktopTools.Helper.exe` 存在

### 8.2 缺失 Helper

1. 临时改名 `helper\DesktopTools.Helper.exe`
2. 触发清理
3. **期望**：明确失败（Helper not found），不崩溃，可恢复后重试

### 8.3 中文 / 空格路径

1. 将发布目录放到例如 `D:\测试 目录\app\`
2. 触发清理并同意 UAC
3. **期望**：提升与清理成功

**结果**：□ PASS　□ FAIL　备注：________

---

## 9. 安全边界（代码 + 运行时）

运行时/日志中**不应**出现：

- Standby List / Modified Page List 清理
- 第三方进程 EmptyWorkingSet / TerminateProcess
- 自动、定时、阈值清理
- 未文档化 Nt* 内存列表清理

代码抽查：

```powershell
Get-ChildItem -Recurse -Include *.cs src | Select-String -Pattern 'TerminateProcess|NtSetSystemInformation|StandbyList|EnumProcesses'
```

**期望**：无命中（或仅文档/注释中的禁止说明）

**结果**：□ PASS　□ FAIL　备注：________

---

## 10. 可观测内存变化（非阻断）

1. 记录清理前后「可用内存」
2. **期望**：UI 展示差值时文案含「非本工具精确释放量」
3. 可用内存减少时不显示负数“释放量”，不因此判定 API 失败

**结果**：□ PASS　□ FAIL　备注：________

---

## 汇总

| ID | 项 | 结论 | 备注 |
| --- | --- | --- | --- |
| 1 | 启动/托盘/单实例 |  |  |
| 2 | UAC 同意清理 + Cache 策略 |  |  |
| 3 | UAC 拒绝 |  |  |
| 4 | 并发门闩 |  |  |
| 5 | 退出与 Helper 生命周期 |  |  |
| 6 | 父进程崩溃 / 超时 |  |  |
| 7 | 标准用户凭据 |  |  |
| 8 | 发布目录/路径 |  |  |
| 9 | 安全边界 |  |  |
| 10 | 内存变化文案 |  |  |

**验收人**：________　**日期**：________

全部关键项 PASS 后，方可将 V1 标记为实机验收完成。
