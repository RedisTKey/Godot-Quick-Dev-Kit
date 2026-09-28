# Quick Level Kit 关卡管理插件

`Quick Level Kit` 是一个可独立复制到任意 Godot 4.x 项目的关卡管理插件，用于在游戏 Jam 中快速搭建“关卡目录 → 进入关卡 → 准备 → 运行 → 通关/失败 → 下一关”的数据驱动流程。

## 定位与边界

插件分为三层：

- **后端数据层（Definition）**：纯 `Resource` 资产，只保存配置，可被多个关卡共享。包含 `QuickLevelKitCatalog`、`QuickLevelKitDefinition`、`QuickLevelKitFlow`、`QuickLevelKitStage`、`QuickLevelKitRoute`、`QuickLevelKitProgress`。
- **运行时层（Runtime）**：每次运行创建隔离实例，按数据驱动的 Stage 图执行，并通过有序 Route 匹配 `transition_requested(context)`。包含 Autoload `QuickLevelKit`（`QuickLevelKitManager`）、`QuickLevelKitSession`、`QuickLevelKitStageRuntime`、`QuickLevelKitContext`。
- **编辑器层（Editor）**：`QuickLevelKitDock` 只负责目录增删、排序与校验；导出游戏不携带任何 UI。

插件**不处理**：视觉转场、动画、音频、HUD、输入锁、玩法规则、时间缩放。转场只能由用户自定义的 Stage 实现。

第一版为**单线顺序关卡**，不实现章节、分支、多路线、成就或最佳成绩。

## 安装

1. 把整个 `addons/quick_level_kit` 目录复制到目标项目。
2. 打开 `Project Settings → Plugins`，启用 `Quick Level Kit`。
3. 启用后插件会自动注册名为 `QuickLevelKit` 的 Autoload（`res://addons/quick_level_kit/runtime/quick_level_kit_manager.gd`）；禁用插件时会自动移除。

## 最小接入

```gdscript
func _ready() -> void:
	var catalog := QuickLevelKitCatalog.new()

	var definition := QuickLevelKitDefinition.new()
	definition.level_id = &"level_001"
	definition.scene = load("res://levels/Level001.tscn")
	catalog.levels = [definition]

	# 使用内置默认流程，或自建 QuickLevelKitFlow
	catalog.default_flow = QuickLevelKitDefaultFlow.create()

	QuickLevelKit.set_catalog(catalog)
	QuickLevelKit.load_first_level()
```

- `QuickLevelKit.load_first_level()`：从目录第一关开始。
- `QuickLevelKit.continue_game()`：从 `highest_unlocked_level_id` 继续。
- `QuickLevelKit.load_level(level_id)`：加载指定关卡。
- `QuickLevelKit.advance_level()` / `reload_level()` / `finish_flow()`：由内置 Stage 调用，也可手动调用。

## 关卡基类

推荐关卡根节点继承 `QuickLevelKitLevel`，并按需覆写生命周期钩子：

```gdscript
extends QuickLevelKitLevel

func _prepare_level() -> void:
	# 进入准备阶段：等待玩家布置机关等
	# 准备好后调用 complete_preparation()
	complete_preparation()

func _start_level() -> void:
	# 开始运行关卡
	pass

func _finish_level(context: Dictionary) -> void:
	# 收到非空 data 的完成/失败上下文
	pass

func _teardown_level() -> void:
	# 清理
	pass
```

统一出口（都会发出 `transition_requested(context)`，并转交给 `QuickLevelKit`）：

- `complete(context := {})` → `{ type = &"completed" }`
- `fail(context := {})` → `{ type = &"failed" }`
- `retry(context := {})` → `{ type = &"retry" }`

`QuickLevelKitLevel` 也可以不继承：只要关卡场景根节点带有 `flow_manager` 或调用 `QuickLevelKit.request_transition()` 即可。

## 自定义 Stage 与有序 Route

自定义 Stage 继承 `QuickLevelKitStage`，覆写 `on_enter` / `on_exit` / `on_preparation_completed`：

```gdscript
class_name MyStage
extends QuickLevelKitStage

func on_enter(runtime: QuickLevelKitStageRuntime) -> void:
	# runtime.session.context 可访问当前上下文
	pass
```

Stage 的 `routes` 是**有序列表**，`transition_requested` 会从上到下匹配，命中第一条即跳转；无匹配时保持当前 Stage，并发出 `transition_rejected(context)`。

`QuickLevelKitRoute` 字段：

- `context_type`：期望的上下文类型；留空表示通配。
- `target_stage_id`：目标 Stage 的 `stage_id`。
- `required_payload_keys`：要求 `context.data` 中必须存在的键。

上下文约定为 `{ &"type": StringName, &"data": Dictionary }`。

内置流程 `QuickLevelKitDefaultFlow.create()` 的 Stage 为：

- `prepare`：进入时调用关卡的 `begin_preparation()`，收到 `preparation_completed` 后跳到 `run`。`ready → run`
- `run`：进入时调用关卡的 `begin_running()`。`completed → advance`，`failed / retry → reload`
- `advance`：调用 `QuickLevelKit.advance_level()`；若已是最后一关则请求 `finished`。`finished → stop`
- `reload`：调用 `QuickLevelKit.reload_level()`。
- `stop`：调用 `QuickLevelKit.finish_flow()`。

## 存档

进度数据 `QuickLevelKitProgress`：

- `current_level_id`：当前关卡。
- `highest_unlocked_level_id`：最高已解锁关卡。
- `level_states`：按稳定 `level_id` 存储的每关自定义状态（运行时可存档数据）。

`QuickLevelKitDefinition.metadata` 是静态配置，不会被存档；需要存档的运行时数据请写入 `QuickLevelKitProgress.level_states`。

默认存档实现为 `QuickLevelKitConfigFileProgressStore`，保存到 `user://quick_level_kit_progress.cfg`。可替换：

```gdscript
class_name MyStore
extends QuickLevelKitProgressStore

func load_progress() -> QuickLevelKitProgress: ...
func save_progress(progress: QuickLevelKitProgress) -> Error: ...
func clear() -> Error: ...
```

```gdscript
QuickLevelKit.set_progress_store(MyStore.new())
```

其他相关 API：`get_progress()`、`save_progress()`、`reset_progress()`、`is_level_unlocked(id)`、`unlock_level(id)`。

## 默认 Hosts 布局

插件在未配置 hosts 时自动创建：

```
QuickLevelKit (Autoload)
└── LevelContainer
    ├── ActiveLevelHost
    └── StagingLevelHost
```

可在自定义场景根中提前准备 hosts 并调用 `QuickLevelKit.configure_hosts(active_host, staging_host)`。

## 编辑器 Dock

`Quick Level Kit` Dock 提供：

- 目录新增 / 删除 / 上移 / 下移。
- 目录校验（空 id、重复 id、缺失场景、场景根未继承 `QuickLevelKitLevel`）。
- 为选中关卡创建默认 `QuickLevelKitFlow`。

Stage / Route 的细节在 Inspector 中编辑对应的 `Resource`。

## 已知边界与后续方向

- 仅支持单线顺序关卡。
- 不含视觉转场 Presenter（作为 Stage 扩展点保留）。
- 不含多存档槽。
- 不含章节、分支、多路线、成就、最佳成绩。
