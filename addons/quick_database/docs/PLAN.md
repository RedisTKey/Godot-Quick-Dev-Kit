# Quick Database C# 全量翻译计划与覆盖清单

基线：`main` / `9f0a4640d78d284cc22929f1acb407caf95b299a`。工作分支：`feat/quick-database-csharp`。仅新增 Database 双语言结构、文档/示例/测试及编译入口；保留其他插件和默认 VFX 场景。

## 阶段

1. [x] 盘点全部源文件、原测试、Godot/.NET 版本和许可证情况；未发现数据库原生库或独立许可证文件
2. [x] 保留 GDScript 与 UID 到 `gds/`，更新内部路径和插件名称
3. [x] 翻译全部运行时、EditorPlugin、示例及错误恢复路径
4. [x] 增加中文用法、覆盖表、可复现测试与轻量自动编译 CI
5. [x] 实际执行冷构建、双语言、故障、跨进程、编辑器和图形界面验证，填写验证报告
6. [x] 复核差异与远端主分支，创建独立远端分支；仅上传此分支，不创建 PR、不合并 main。发布后核验见分支 Actions 与交付说明

## 功能对应

| GDScript | C# | 验证重点 |
| --- | --- | --- |
| quick_database_document.gd | QuickDatabaseDocument.cs | schema、双向字典转换、具体类型复制、嵌套隔离 |
| quick_database_store.gd | QuickDatabaseStore.cs | 可覆盖 API、8 种状态与默认行为 |
| quick_database_config_file_store.gd | QuickDatabaseConfigFileStore.cs | ConfigFile 格式、事务写入、验证、回滚、断电恢复、损坏/未知 schema 备份、受阻重试、擦除 |
| quick_database_repository.gd | QuickDatabaseRepository.cs | 工厂+store+path、载入/保存、全部状态代理 |
| quick_database_service.gd | QuickDatabaseService.cs | 14 个公共槽操作、9 个信号、保存失败回滚、退出 Flush、多实例 |
| quick_database_plugin.gd | QuickDatabasePlugin.cs | Autoload 注册/移除、占用保护、语言互斥、重复开关 |
| example_document.gd | ExampleDocument.cs | count/title 字段验证、同 schema 兼容 |
| basic_usage.gd/.tscn | BasicUsage.cs/.tscn | F6 实例、Inspector 配置、增量/提交/保存/重载/擦除 |
| 原五组 tests | DatabaseTests*.cs + 原测试保留 | 原功能全部保留，增加故障阶段和边界覆盖 |

C# 的 `Func<QuickDatabaseDocument>` 工厂替代 GDScript 脚本对象，`Action<QuickDatabaseDocument>` 替代 Callable。命名采用 C# PascalCase；运行时完全独立，不以调用 GDScript 包装实现。

## 原插件不存在的范围

没有自定义 Inspector、编辑器 dock、Resource 数据类、SQL/SQLite、CSV/JSON 导入导出或网络存档。原有 ConfigFile 读写和双向文档字典转换已覆盖。无需新增原生依赖或 TapTap 专用逻辑。

## 有意修复

仅 C# 加固：按路径保留损坏恢复状态、先擦除恢复文件、唯一备份名、失败临时文件清理、深复制文档快照、FlushAll 在发信号期间使用槽名快照、CloseSlot 不丢弃回调中新改的数据/替换槽、载入信号先快照诊断状态。两版 EditorPlugin 均只移除自己持有的注册，并能识别编辑器重新打开后已有的同路径注册，并解析 Godot 4.7 的 uid:// Autoload 引用。
