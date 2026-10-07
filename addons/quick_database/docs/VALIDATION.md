# Quick Database 验证记录

验证日期：2026-10-07。以下结果来自真实 Godot / .NET 进程，所有通过项均检查进程退出码；没有把打印成功但进程崩溃视为通过。

## 环境

- 基线：main `9f0a4640d78d284cc22929f1acb407caf95b299a`
- Godot：4.7.2.stable.mono.official.ed1daf0bf
- .NET SDK：9.0.318；目标 net9.0 / Godot.NET.Sdk 4.7.2
- Linux 云端隔离项目；不使用用户电脑
- 图形界面：X11 / OpenGL Compatibility / Mesa llvmpipe
- 自动 CI：仅 C# Debug/Release；完整 Godot 测试仅 workflow_dispatch 手动运行

## 已执行结果

| 检查 | 结果 |
| --- | --- |
| 全仓库 Debug / Release 构建 | 通过，0 warnings / 0 errors |
| 全仓库编辑器导入 | 通过，无 SCRIPT ERROR / ERROR |
| 仅 Database 的全新项目 | 先无程序集冷导入，再 Debug/Release 构建，均退出 0 |
| 冷导入编辑器 | 3 项断言通过；首次构建前 C# 插件保持关闭 |
| 构建后的真实编辑器 | 18 项断言通过，包括 3 次开关、两种占用方向、Inspector 字段 |
| 编辑器持久化与重新打开 | 分别 19 / 20 项断言通过；已启用状态、UID Autoload 保留并可移除 |
| 原 GDScript 测试 | document / store / repository / service / package 五组全部通过 |
| GDScript ↔ C# 互读 | 12 项断言通过，含嵌套字典/数组/Vector2、Unicode、Int64 count/schema |
| C# 原生回归 | 75 个命名案例通过，真实退出 0 |
| C# 单语言独立项目 | 不包含任何 GDScript 插件源文件，重新构建/导入，75 案例通过、退出 0 |
| 跨进程持久化 | 第一个进程退出场景树保存，第二个进程读取并验证数值/Unicode/嵌套 Variant，分别退出 0 |
| 生命周期稳定性复测 | 额外 4 次独立进程各 75/75，均退出 0，无 unsafe-reference 泄漏或脚本错误 |
| 图形交互示例 | 实际点击 +1、Flush、Reload、Commit title、Erase example，并关闭/重开验证持久化；两次图形进程退出 0 |
| 差异审查 | 原 GDScript 运行时语义保持，13 个原 UID 保留；其他插件源文件不变；git diff --check 通过 |

### 75 案例的重点

- 文档默认值、类型校验、运行时子类复制和嵌套隔离
- 工厂/store/path 保留、schema 门禁、全部诊断代理
- 临时写入/验证/旧文件保留/新文件提升/回滚/清理各失败阶段
- 损坏与未知 schema 备份、备份受阻保留原字节、重试恢复、断电恢复
- 多路径复用 store 不丢失另一路径的待恢复状态；擦除不会使旧 `.previous` 复活
- 缺失槽、重复槽、dirty 状态、Commit 成功/失败交换规则、Flush 失败、Close 失败保留
- 9 个信号的参数与顺序；同步回调修改槽或共用 store 的重入保护
- 真实退出场景树 Flush、多服务隔离、反复树附着
- 示例本地服务与 Autoload 两种模式、5 个按钮、3 次 detach/reattach、释放后信号不再访问旧 UI、其他槽不覆盖当前 UI

故障注入测试有意触发 ConfigFile 解析错误和存储错误日志；断言验证其错误码、备份及原数据保全。脚本要求明确的通过标记和退出 0，并拒绝 SCRIPT ERROR、未处理异常、unsafe-reference 泄漏及失败标记。

## 验证中发现并修复

1. Godot 4.7 会把 Autoload 路径转为 uid://。两版插件现在先解析 UID 再判断归属，避免禁用后残留或误判冲突
2. 原 GDScript 整数是 Int64。C# schema 和示例 count 使用 long，避免旧存档中大整数被归零
3. 编辑器/服务重入保护：共享 store 的载入诊断先快照；CloseSlot 不移除回调重新弄脏或替换的槽
4. 示例使用具名事件处理并成对订阅/退订，支持移出/重新加入同一场景实例；临时文档快照及时 Dispose
5. 原生测试最初通过 GetSignalConnectionList 统计 C# 事件订阅，这是不正确的：Godot 的事件桥接连接即使无托管订阅者也存在。现在检查实际托管 delegate 数量并结合 UI 行为验证
6. 大量同步测试在同一帧后立即 Quit 曾触发 Godot 托管终结器的退出时序问题。测试现在在调用栈退出后让引擎处理 3 帧，并执行 2 轮 GC/终结器回收，再打印最终结果并退出；复测均真实退出 0。交互式示例正常关闭无需此测试清理流程

## 复现

```bash
dotnet build QuickVfxDemo.csproj -c Debug
dotnet build QuickVfxDemo.csproj -c Release
GODOT=/path/to/Godot_mono bash tests/quick_database/run.sh
```

脚本创建只含 Database 的临时项目。设置 `QUICK_DATABASE_TEST_WORK` 为全新空目录可保留每阶段日志；离线官方 Godot NuGet 源可通过 `NUGET_CONFIG` 指定。不要跨 Debug/Release 盲用 `--no-restore`：Godot 编辑器程序集依赖与构建配置有关。

单语言验证：在空 Godot .NET 项目只复制 `addons/quick_database/csharp`，Build、编辑器导入后运行 `res://addons/quick_database/csharp/tests/DatabaseTests.tscn`。

## 发布与边界

代码仅发布到 `feat/quick-database-csharp`；不创建 PR，不合并 main。[此分支的自动编译](https://github.com/RedisTKey/Godot-Quick-Dev-Kit/actions/workflows/quick-database.yml?query=branch%3Afeat%2Fquick-database-csharp) 与交付消息提供远端提交核验结果。

- 首次启用 C# EditorPlugin 前必须 Build；使用 Godot .NET 版
- 未执行 Windows/macOS/移动设备或导出包上的真实运行测试，本次运行平台为 Linux
- 不承诺自动 schema 升级、跨进程并发写入、强制断电 fsync 或磁盘介质损坏恢复
- 默认文档复制要求公开无参构造，可覆写；Dictionary 深复制不克隆 GodotObject/Resource 引用
- mutator 同步执行，内部不要关闭/重载同一个槽；异常由调用者处理
- GDScript 运行时保留原行为；C# 额外安全修复详见 README
