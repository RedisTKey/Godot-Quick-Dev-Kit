# Godot Quick Dev Kit

一个面向 **Godot 4.x** 的快速开发工具箱，收录可独立复制、按需启用的通用插件，帮助你更快搭建 Game Jam 原型、独立游戏和小型项目。

当前工具箱包含音频管理、关卡流程管理和本地数据管理模块。每个模块都位于 `addons/` 下，可以单独安装和使用，不需要引入整个工具箱。

> 项目仍在持续开发中，API 和目录结构可能会发生变化。

## 功能模块

| 模块 | 路径 | 说明 |
| --- | --- | --- |
| **Quick VFX Manager (C#)** | [`addons/quick_vfx_manager`](addons/quick_vfx_manager) | 调用者组件注册 Scene/Resource、组合特效生命周期、世界/附着播放；无 Autoload、无池化。 |
| **Quick Audio Manager** | [`addons/quick_audio_manager`](addons/quick_audio_manager) | 基于 `Resource` 的音频资产、轨道、音量、静音和播放管理。 |
| **Quick Database** | [`addons/quick_database`](addons/quick_database) | 面向本地存档的数据文档、存储后端、仓储和多存档槽服务。 |
| **Quick Level Kit** | [`addons/quick_level_kit`](addons/quick_level_kit) | 数据驱动的关卡目录、关卡流程、准备/运行/通关/失败状态和进度管理。 |

每个插件目录都包含独立的 README，提供更完整的 API 说明、接入方式和边界定义。

## 设计目标

- **模块化**：每个插件都可以独立复制到 Godot 项目中。
- **低耦合**：尽量通过 `Resource`、信号和清晰的运行时服务连接项目代码。
- **面向快速开发**：优先覆盖 Game Jam 和原型开发中常见的基础设施需求。
- **数据驱动**：将音频轨道、关卡目录、流程和存档数据配置化。
- **易于替换**：存储后端、关卡 Stage、Route 和项目侧 UI 都保留扩展点。
- **明确边界**：插件只负责通用基础能力，不强行接管项目玩法、UI 或视觉表现。

## 安装方式

### 方式一：安装整个工具箱

将仓库中的 `addons` 目录复制到目标 Godot 项目的根目录：

```text
你的项目/
├── project.godot
└── addons/
    ├── quick_audio_manager/
    ├── quick_database/
    └── quick_level_kit/
```

然后打开 Godot 编辑器，在 **Project → Project Settings → Plugins** 中按需启用插件。

### 方式二：只安装单个插件

只复制需要的插件目录即可。例如只使用音频管理：

```text
目标项目/addons/quick_audio_manager/
```

启用插件后，它会根据自身配置注册对应的 Autoload；禁用插件时会尝试自动移除由插件注册的 Autoload。

## Quick VFX 独立演示工程

本仓库根目录 `project.godot` 是独立 Quick VFX Manager 实验室（Godot 4.7.2 .NET + .NET 9）。打开后先 Build 再运行；只编译 VFX 插件，不启用或依赖其他 Quick 模块。安装到现有项目时只复制所需插件目录，详见 [Quick VFX 文档](addons/quick_vfx_manager/README.md)。

## 快速开始

### Quick Audio Manager

用于统一管理音乐、音效和 UI 音频轨道。

```gdscript
@export var click_asset: QuickAudioAsset

func _ready() -> void:
    var audio := get_node("/root/QuickAudioManager")
    var player := audio.play(click_asset)
    if player == null:
        push_warning("播放失败，请检查音频资源和轨道配置。")
```

主要能力：

- 使用 `QuickAudioAsset` 描述音频资源、目标轨道、音量和音高。
- 使用 `QuickAudioTrackDefinition` 配置音轨、bus、send、静音和最大并发 voice 数。
- 提供 `Music`、`SFX`、`UI` 等默认轨道库。
- 支持播放、停止、重复播放检测、轨道音量和静音控制。
- 通过 `QuickAudioManager` Autoload 访问运行时服务。

详细文档：[`addons/quick_audio_manager/README.md`](addons/quick_audio_manager/README.md)

### Quick Database

用于管理本地数据、玩家资料和多个存档槽。

```gdscript
var database := get_node("/root/QuickDatabase")
database.open_slot(&"profile", PlayerProfile, "user://profile.cfg")

database.update_slot(&"profile", &"name", func(document: QuickDatabaseDocument) -> void:
    document.set("name", "Player")
)

database.commit_slot(&"profile", &"level", func(document: QuickDatabaseDocument) -> void:
    document.set("level", 2)
)
```

主要能力：

- 通过 `QuickDatabaseDocument` 定义可序列化的数据文档。
- 默认使用基于 `ConfigFile` 的本地存储。
- 支持事务写入、损坏数据备份和事务恢复保护。
- 支持多个命名存档槽。
- 可以继承 `QuickDatabaseStore` 替换为 JSON、云存档或其他存储实现。
- 通过 `QuickDatabase` Autoload 管理存档生命周期。

详细文档：[`addons/quick_database/README.md`](addons/quick_database/README.md)

现在提供独立的 `gds/` 与原生 `csharp/` 两版；旧 GDScript 显式路径需加上 `gds/`。两版共用 `QuickDatabase` Autoload，启用时二选一。C# 版先 Build，再启用插件；交互示例为 `addons/quick_database/csharp/examples/BasicUsage.tscn`（F6）。

### Quick Level Kit

用于快速搭建“关卡目录 → 进入关卡 → 准备 → 运行 → 通关/失败 → 下一关或重试”的基础流程。

```gdscript
var catalog := QuickLevelKitCatalog.new()

var definition := QuickLevelKitDefinition.new()
definition.level_id = &"level_001"
definition.scene = load("res://levels/Level001.tscn")
catalog.levels = [definition]
catalog.default_flow = QuickLevelKitDefaultFlow.create()

QuickLevelKit.set_catalog(catalog)
QuickLevelKit.load_first_level()
```

主要能力：

- 使用 `QuickLevelKitCatalog` 和 `QuickLevelKitDefinition` 配置关卡目录。
- 提供默认的 `prepare`、`run`、`advance`、`reload` 和 `stop` 流程。
- 支持自定义 Stage、Route 和关卡生命周期钩子。
- 支持当前关卡、最高解锁关卡和关卡状态存档。
- 提供编辑器 Dock，用于关卡目录的增删、排序和校验。
- 通过 `QuickLevelKit` Autoload 管理运行时关卡流程。

详细文档：[`addons/quick_level_kit/README.md`](addons/quick_level_kit/README.md)

## Autoload

启用插件后，默认会注册以下 Autoload：

| 插件 | Autoload |
| --- | --- |
| Quick Audio Manager | `QuickAudioManager` |
| Quick Database | `QuickDatabase` |
| Quick Level Kit | `QuickLevelKit` |

如果项目中已经存在同名 Autoload，请在启用插件前检查配置，避免名称冲突。

## 兼容性

- 目标版本：**Godot 4.x**
- 主要代码语言：**GDScript**；Quick VFX Manager 为独立 **C# / .NET** 插件
- 不依赖第三方库。
- 各插件会尽量使用 Godot 4.x 的通用 API；具体验证版本和限制请以对应插件的 README 为准。

## 项目结构

```text
Godot-Quick-Dev-Kit/
├── addons/
│   ├── quick_audio_manager/
│   │   └── README.md
│   ├── quick_database/
│   │   └── README.md
│   └── quick_level_kit/
│       └── README.md
└── README.md
```

## 边界说明

这个项目提供的是通用开发基础设施，不会替项目决定具体的玩法和表现：

- 不包含完整游戏模板或示例游戏。
- 不负责具体的 HUD、输入系统、视觉转场和玩法规则。
- 不强制项目使用特定的 UI、场景组织方式或存档字段。
- 插件的持久化、关卡逻辑和表现层扩展由使用者的项目负责。

## 参与开发

欢迎提交 Issue 或 Pull Request：

1. Fork 本仓库。
2. 创建功能分支。
3. 修改对应插件，并同步更新文档或测试。
4. 提交 Pull Request，说明变更内容和验证方式。

## 许可证

当前仓库尚未声明正式开源许可证。如需在项目中使用、修改或再发布，请先确认仓库作者的授权范围。

## 作者

- GitHub：[@RedisTKey](https://github.com/RedisTKey)
- 仓库：[RedisTKey/Godot-Quick-Dev-Kit](https://github.com/RedisTKey/Godot-Quick-Dev-Kit)
