# Quick Input 验证记录

源基线：`0bee37011d7b38e44661a28cbe79fda448501952`（RedisTKey/Godot-Quick-Dev-Kit）。

## 静态检查

- 原 Quick Input 除主 README 外全部 32 个文件迁入 `gds/`，15 个原 `.gd.uid` 内容保持不变
- 旧 GDScript `class_name` API 保留；C# 全局注册仅使用三个 `CSharpQuickInput*` 名称，无同名冲突
- 插件、场景与测试路径已更新；插件启用互斥、外部 Autoload 保护与重启后的清理均有只读校验测试
- `git diff --check` 通过；测试助手的 Python 语法检查通过

## 测试设计

`tests/run_quick_input.py` 创建临时项目，仅复制 Quick Input；隔离缓存、编辑器配置、NuGet 目录与 `user://`。测试后删除临时项目，不启用其他工具箱插件。

- GDScript：包完整性、脚本/示例加载、路径/UID、插件所有权、运行时绑定和保存恢复
- C#：真实 Godot.NET.Sdk 编译、配置和非法存档拒绝、键/鼠标/手柄 codec、所有冲突策略、事务快照、存储失败回滚、UI 捕获/取消/确认、Resource 保存加载、双语言 ConfigFile 互读
- 针对原实现发现的问题，两个版本同时增加 Swap 目标类型校验和无效按键编码校验，避免保存无法重新加载的绑定或把非法按键静默变成空槽

## 执行状态

2026-10-02，在 Linux 云端隔离项目中使用官方 Godot **4.6.3 stable Mono**、官方 .NET SDK **8.0.425**：

- C# 构建成功，**0 warnings / 0 errors**
- GDScript 包测试、运行时测试通过；普通版 Godot 4.6.3 也通过
- C# **14 组、490 项断言、0 失败**，包含双语言 codec / 存档互操作与 C# `.tres` 资源保存加载
- 仅安装 C# 的独立项目：466 项断言通过，明确跳过 GDScript 互操作；仅安装 GDScript 的普通版项目也通过
- 编辑器真实启用/停用、两个方向互斥、外部 Autoload 保护：**13 项检查、0 失败**
- GDScript 和 C# 示例场景均真实启动并退出，无脚本错误
- 官方 .NET 与 Godot 下载包分别核对 SHA512 / SHA256；使用 Godot 官方包内附 NuGet 包进行可离线复现的构建

编辑器测试发现并修复了另一项旧版兼容性问题：Godot 4.6 的 Autoload 设置可能保存为 `*uid://...`。两套插件现在同时识别 UID 与 `res://` 路径，避免停用后残留服务。对无法解析或属于其他脚本的 UID 不会执行删除。

仓库包含 GitHub Actions 工作流 `.github/workflows/quick-input.yml`，在 Quick Input 相关变更上重新执行整个隔离测试助手。

## 仍需项目侧验证

- 在目标平台真实键盘布局与实体手柄上确认按键显示、取消键和设备输入
- 在目标项目 UI 路由/暂停策略下确认捕获按钮的事件阻断
- C# 资源需显式创建对应类型，原 GDScript Resource 不会自动转换
