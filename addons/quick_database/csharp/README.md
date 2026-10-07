# Quick Database C#

独立原生 C# 实现，命名空间 `QuickDev.Database`。Godot 4.7.2 .NET / .NET SDK 9。

1. 保留路径 `addons/quick_database/csharp/`，先 Build，再启用 Quick Database (C#)
2. 与 GDScript 插件二选一，共用 Autoload 名字 `/root/QuickDatabase`
3. F6 运行 `examples/BasicUsage.tscn`；Inspector 配置槽名、存储路径和初始标题
4. 使用 `QuickDatabaseDocument` 定义 schema 和字典转换；`OpenSlot<T>`、`UpdateSlot`、`CommitSlot`、`FlushSlot` 操作数据
5. 返回快照与内部文档独立；同步 mutator 内不要关闭/重载同一个槽

[完整中文用法和安全边界](../README.md) · [功能对应表](../docs/PLAN.md) · [验证记录](../docs/VALIDATION.md)

本目录不调用 GDScript 核心，不引入数据库或原生依赖。文档 schema 是 Int64，保持 Godot Variant 整数范围。默认存储字节格式与 GDScript 版兼容。
