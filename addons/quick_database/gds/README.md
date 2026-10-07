# Quick Database

Godot 4.x 通用本地数据管理插件：文档 + 可插拔存储后端 + 仓储 + 多存档槽服务。

## 安装与启用

1. 把`addons/quick_database/gds`（保留父目录层级） 目录复制到目标项目的 `addons/` 下。
2. 在 Project Settings → Plugins 中启用 Quick Database (GDScript)。与 C# 版二选一。
3. 插件会自动注册 Autoload `QuickDatabase`；禁用插件时自动移除。

## 核心概念

- `QuickDatabaseDocument`：数据文档基类。子类覆写 `get_schema_version()`、`to_dictionary()`、`from_dictionary()`；`from_dictionary()` 负责逐字段类型校验并在非法或缺失时回退默认值。
- `QuickDatabaseStore`：可插拔存储后端基类。默认 `QuickDatabaseConfigFileStore` 使用 `ConfigFile`，带事务写入、损坏备份与断电事务恢复。
- `QuickDatabaseRepository`：组合文档脚本、store 与路径，负责 schema 版本门禁。
- `QuickDatabaseService`（Autoload `QuickDatabase`）：管理多个命名存档槽。

## 定义文档

```gdscript
class_name PlayerProfile
extends QuickDatabaseDocument

var name := ""
var level := 1

func get_schema_version() -> int:
	return 1

func to_dictionary() -> Dictionary:
	return {"name": name, "level": level}

func from_dictionary(data: Dictionary) -> void:
	var raw_name: Variant = data.get("name", "")
	name = String(raw_name) if typeof(raw_name) == TYPE_STRING else ""
	var raw_level: Variant = data.get("level", 1)
	level = int(raw_level) if typeof(raw_level) == TYPE_INT else 1
```

## 打开存档槽与读写

```gdscript
var database := get_node("/root/QuickDatabase")
database.open_slot(&"profile", PlayerProfile, "user://profile.cfg")

# 修改并标记为脏，稍后 flush
database.update_slot(&"profile", &"name", func(document: QuickDatabaseDocument) -> void:
	document.set("name", "Reed"))

# 原子提交：成功才落盘并替换内存文档
database.commit_slot(&"profile", &"level", func(document: QuickDatabaseDocument) -> void:
	document.set("level", 2))

database.flush_slot(&"profile")
database.flush_all()
```

## 多存档槽

每个槽有独立的文档类、路径与存储后端：`open_slot(&"slot_1", PlayerProfile, "user://slot_1.cfg")`、`reload_slot`、`erase_slot`、`close_slot`。

## 替换存储后端

继承 `QuickDatabaseStore` 覆写 `load_store` / `save_store` / `erase_store`（并把状态 getter 一并实现），再传给 `open_slot` 的第四个参数即可替换为 JSON、云存档等后端。默认实现为 `QuickDatabaseConfigFileStore`。

## 信号

`database_loaded`、`data_changed`、`save_succeeded`、`save_failed`、`corrupt_database_backed_up`、`database_recovery_blocked`、`database_transaction_recovery_blocked`、`slot_opened`、`slot_closed`。

## 边界

- 不负责玩法进度、关卡目录、设置应用、UI 或防抖自动保存。
- 不持久化任何游戏专有字段；字段校验完全由使用者的文档子类决定。
- 默认存储为 `ConfigFile`；插件不引入第三方依赖。
