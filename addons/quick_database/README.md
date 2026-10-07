# Quick Database：GDScript / C# 双版本

Godot 4.x 通用本地数据插件：版本化文档、可插拔存储、仓储、多存档槽服务。原 GDScript 代码保留在 `gds/`；`csharp/` 是独立原生 C# 实现，不调用 GDScript 核心，不依赖 SQLite、第三方包或游戏项目。

## 选择版本

| 版本 | 目录 | 启用项 | 要求 |
| --- | --- | --- | --- |
| GDScript | `addons/quick_database/gds` | Quick Database (GDScript) | Godot 4.x；本次以 4.7.2 验证 |
| C# | `addons/quick_database/csharp` | Quick Database (C#) | Godot 4.7.2 .NET / .NET SDK 9 |

两版默认注册同一个 Autoload：`/root/QuickDatabase`。一次只启用一个；遇到已被另一版或用户占用的名字，插件会警告并保留原注册。关闭占用方后，重新开关目标插件。不需要 Autoload 时，可以直接创建 `QuickDatabaseService` 节点。

## 安装与冷启动

1. 复制整个 `addons/quick_database` 到项目。也可只复制选用的语言目录，但保留父目录层级。
2. C# 项目先点击编辑器右上角 **Build**，或执行 `dotnet build`。第一次导入时尚无程序集，先保持 C# 插件关闭；构建成功后再启用它。标准 Godot 不支持 C#，需使用 .NET 版。
3. Project → Project Settings → Plugins 启用所选版本。项目使用显式 `<Compile Include=...>` 时，加入 `addons/quick_database/csharp/**/*.cs`。
4. 直接打开 `csharp/examples/BasicUsage.tscn`，按 F6。示例可在未启用插件时自行创建独立服务。

仓库默认场景仍是原有 VFX 实验室；此分支不会更改它。Database 示例在 Inspector 中可设置 `SlotName`、`SavePath`、`InitialTitle`。示例写入 `user://quick_database_example.cfg`；“Erase example”仅清除这个示例使用的路径。

### 原 GDScript 项目迁移

旧路径 `res://addons/quick_database/runtime/...`、`examples/...`、`plugin.cfg` 现分别位于 `gds/runtime/...`、`gds/examples/...`、`gds/plugin.cfg`。原 `.gd.uid` 均保留。先关闭旧插件，再更新项目中的显式 preload、场景引用以及 `autoload/QuickDatabase` 路径，最后启用新位置的 GDScript 版。已有 `.cfg` 数据文件不需要迁移。

## C# 定义文档

```csharp
using Godot;
using Godot.Collections;
using QuickDev.Database;

public partial class PlayerProfile : QuickDatabaseDocument
{
    public string PlayerName { get; set; } = "";
    public long Level { get; set; } = 1;
    public override long GetSchemaVersion() => 1;
    public override Dictionary ToDictionary() => new()
    {
        ["name"] = PlayerName, ["level"] = Level
    };
    public override void FromDictionary(Dictionary data)
    {
        PlayerName = data.TryGetValue("name", out var name)
            && name.VariantType == Variant.Type.String ? name.AsString() : "";
        Level = data.TryGetValue("level", out var level)
            && level.VariantType == Variant.Type.Int ? level.AsInt64() : 1;
    }
}
```

文档为 `RefCounted`，保持原版模型，不是 Inspector 数据资源。字段校验、默认值和游戏业务由文档子类负责。`DuplicateDocument()` 用具体运行时类型创建副本，并对字典/数组做深复制；默认需要公开无参构造函数。带构造参数时覆写此方法。不要把共享 `GodotObject`/`Resource` 引用当作值数据；Godot 的 Dictionary 深复制不克隆对象。

## 打开、修改与原子提交

```csharp
var database = GetNode<QuickDatabaseService>("/root/QuickDatabase");
database.OpenSlot<PlayerProfile>("profile", "user://profile.cfg");
database.UpdateSlot("profile", "name", d => ((PlayerProfile)d).PlayerName = "Reed");
Error error = database.FlushSlot("profile");

// 先写候选副本，成功后才替换内存；失败不污染原文档与 dirty 状态。
error = database.CommitSlot("profile", "level", d => ((PlayerProfile)d).Level = 2);
using var snapshot = database.GetDocument<PlayerProfile>("profile");
// snapshot 是副本；要持久化修改请使用 UpdateSlot / CommitSlot。
```

`OpenSlot("profile", () => new PlayerProfile(), path, customStore)` 是工厂重载，对应 GDScript 的文档脚本参数。仓储的 `GetDocumentFactory()` 返回原工厂；C# 不需要也不加载 GDScript。

- `UpdateSlot`：立即修改当前文档并标脏，不立即保存
- `CommitSlot`：复制、修改、保存成功后交换文档；即使原槽未标脏也会保存
- `FlushSlot` / `FlushAll`：只写脏槽；遇到错误立即返回，未成功的槽仍然标脏
- `CloseSlot`：先保存脏槽；失败则保留槽，不丢数据
- `ReloadSlot`：重新读盘并丢弃未保存变更
- `EraseSlot`：删除该存档及 `.previous` 恢复文件，再恢复文档默认值
- `GetSlotNames`、`HasSlot`、`GetSavePath`、`IsDirty`、`MarkDirty`：查询与显式标记
- 服务退出场景树时调用 `FlushAll`；重要存档仍应主动 Flush 并检查返回值，不要依赖进程被强制终止后的回调

mutator 是同步调用。保持回调短小，不要在回调里关闭/重载同一个槽，不要跨线程使用服务；回调抛出的 C# 异常会交回调用者，不会转换成保存成功。

## 存储、兼容和恢复

默认 `QuickDatabaseConfigFileStore` 使用原版的 ConfigFile 格式：`[meta] schema_version` 和 `[data] payload`。GDScript 与 C# 可以互读同一存档及嵌套 Variant 字典、数组、数值、字符串、向量。schema 数值必须相等且类型必须是整数；不会自动升级旧 schema。缺失文件只返回默认文档，不自动写盘。

保存先写唯一 `.tmp` 并重新读取验证，再将旧存档改名 `.previous`，提升新文件，最后清理 `.previous`。下一次操作会恢复未完成事务。损坏数据/未知 schema 会先移动到唯一的 `*.corrupt.cfg` 备份；备份受阻时禁止覆盖源文件，后续保存可重试恢复。

`LoadStatus` 的 0–7 数值与 GDScript 一致：Missing、Loaded、RecoveredCorrupt、RecoveredUnknownSchema、RecoveryBlockedCorrupt、RecoveryBlockedUnknownSchema、RecoveredInterruptedTransaction、TransactionRecoveryBlocked。

C# 包含以下有意的安全加固：

1. 多路径共用一个 store 时，待备份状态按绝对路径隔离
2. 擦除优先删除 `.previous`，失败时保留原存档，避免下次载入意外恢复已擦除数据
3. 损坏备份加入唯一后缀，避免同一毫秒覆盖此前备份
4. 写入失败尽量清理该次 `.tmp`，保留原始错误码
5. 文档快照深复制嵌套字典/数组；每个服务的槽表独立
6. 载入信号发送前快照状态，关闭槽前复核回调是否再次修改数据，避免同步信号回调串扰

这不是 SQLite 或通用数据库引擎，不提供 SQL、网络同步、加密、备份轮换、自动 schema 迁移、定时自动保存或文件系统 fsync 保证。原插件没有专用编辑器面板、导入导出器或 Resource 类型，因此 C# 也不虚构这些功能。不同槽不要指向同一文件，多个进程不要并发写同一路径。

### 自定义后端

继承 `QuickDatabaseStore` 并覆写 `LoadStore`、`SaveStore`、`EraseStore`，以及 `GetLastLoadStatus`、`GetLastBackupPath`、`IsRecoveryPending`、`GetLastRecoveryError`、`IsTransactionRecoveryPending`、`GetLastTransactionRecoveryError`。传入 `OpenSlot` 或仓储的 `store` 参数即可。`LoadStore` 返回 `{ "payload": Dictionary, "status": int }`。

## 信号

C# 可直接订阅事件，亦可通过 Godot 信号系统连接：

`DatabaseLoaded(slot, status)`、`DataChanged(slot, field)`、`SaveSucceeded(slot, path)`、`SaveFailed(slot, path, error)`、`CorruptDatabaseBackedUp(slot, backupPath)`、`DatabaseRecoveryBlocked(slot, path, error)`、`DatabaseTransactionRecoveryBlocked(slot, path, error)`、`SlotOpened(slot)`、`SlotClosed(slot)`。

载入时顺序为备份/恢复告警、DatabaseLoaded，再 SlotOpened。成功 Commit 先 DataChanged，再 SaveSucceeded；失败只发 SaveFailed。

## 验证

```bash
dotnet build QuickVfxDemo.csproj -c Debug
dotnet build QuickVfxDemo.csproj -c Release
GODOT=/path/to/Godot_mono bash tests/quick_database/run.sh
```

脚本在临时项目中只复制 Database 插件，覆盖冷导入、首次构建、编辑器开关/互斥、原 GDScript 五组测试、C# 故障注入与信号测试、双向格式互通和跨进程存档。默认清理临时项目；设置 `QUICK_DATABASE_TEST_WORK` 为新的空目录可保留日志。自动 CI 只编译 Debug/Release，完整 Godot 验证仅手动触发。

[范围与功能对应表](docs/PLAN.md) · [验证记录](docs/VALIDATION.md) · [原 GDScript 用法](gds/README.md)
