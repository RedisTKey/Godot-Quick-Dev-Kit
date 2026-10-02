# Quick Input (GDScript)

Godot 4.x 输入绑定插件。支持键盘、鼠标按钮及滚轮、手柄按钮；每个 Action 可配置多个槽位，并提供冲突预览、四种冲突策略、恢复默认、文件持久化和可复用的捕获按钮。运行时只依赖 Godot 自带 API。

## 安装与边界

复制 `addons/quick_input/gds` 到目标项目的同名路径，在「项目 → 项目设置 → 插件」启用 **Quick Input (GDScript)**。只安装本目录即可，不依赖 C#、.NET 或工具箱其他插件；也可以复制整个 `addons/quick_input` 保留两种语言实现。

GDScript 与 C# 版共用 `QuickInput` Autoload，**只能启用其中一个**。切换前先取消勾选当前实现，再启用另一个；若误把两项都勾选，后启用的一项会报错并保持不注册，需要手动取消它的勾选。插件不会自动停用另一版，也不会覆盖已有的同名服务。

GDScript 版仅识别 `*res://addons/quick_input/gds/runtime/quick_input_manager.gd` 为自己的 Autoload 配置；重复启用不重复注册，编辑器重启/脚本重载后仍可正确清理。其他路径、未开启全局单例访问或格式错误的配置均会被保留并提示检查。停用插件时，只移除精确匹配的本版 Autoload；被拒绝的启用过程不会改变原有配置。

从旧布局迁移时，先停用旧 Quick Input 插件，再移动脚本及其 `.uid` 文件到 `gds/`，更新场景中的显式路径，最后启用本版。如果旧 `QuickInput` Autoload 仍指向 `addons/quick_input/runtime/`，请先在项目设置中移除旧条目；插件不会擅自覆盖它。所有 `class_name`、信号与运行时 API 保持不变。

插件不自动扫描或接管游戏 Action。先在 Godot「输入映射」中定义动作及默认事件，再调用 `QuickInput.configure()`。可以与项目已有的输入服务并存，但不要同时让两套服务管理同一批动作。

本版只支持 `InputEventKey`、`InputEventMouseButton`、`InputEventJoypadButton`；模拟摇杆轴、手柄配置档、云存档、编辑器 Dock 暂不提供。菜单/UI 的事件阻断仍由游戏自身负责。

## 配置

可在 Inspector 中创建 `QuickInputSettings` 和 `QuickInputActionDefinition` Resource，也可以在脚本中创建：

```gdscript
var jump := QuickInputActionDefinition.new()
jump.action = &"jump"                 # 必须已存在于 InputMap
jump.display_name = "Jump"
jump.binding_slots = 2                  # 包括已有默认事件的槽位数
jump.conflict_group = &"gameplay"
jump.allowed_event_types = (
	QuickInputActionDefinition.KEYBOARD
	| QuickInputActionDefinition.GAMEPAD_BUTTON
)

var bindings := QuickInputSettings.new()
bindings.actions.append(jump)
bindings.reserved_events.append(my_escape_key_event)
bindings.allow_modifier_combinations = false

var error := QuickInput.configure(bindings) # 默认 user://quick_input_bindings.cfg
if error != OK:
	push_error("Input configuration failed: %d" % error)
```

也可提供自己的 `QuickInputStore` 子类给 `configure(bindings, store)`，实现 `load_bindings() -> Dictionary`、`save_bindings(data) -> Error`，加载后通过 `load_error` 汇报错误。只有存储成功才会更新运行时 `InputMap`；失败时发出 `bindings_save_failed(error)`。

同一 `conflict_group` 的动作参与冲突检查；组名不同的动作可共享按键。空组名同样是一组。单个动作的默认事件数量不得超过声明的槽位数；不支持的默认事件会使配置失败，以免被意外抹掉。`allowed_event_types` 和 `allow_modifier_combinations` 同时约束存档数据与新输入。被保留的事件不可作为新绑定；示例中的 Escape 保留事件由项目自行定义。

## 重绑定 API

```gdscript
var preview := QuickInput.preview_rebind(&"jump", 1, new_event)
if preview.status == QuickInputRebindResult.Status.CONFLICT:
	# preview.conflicts 是已占用此输入的动作和槽位。
	pass

var error := QuickInput.apply_rebind(
	preview.transaction,
	QuickInputManager.ConflictPolicy.SWAP
)

# 直接应用（默认 REJECT）：
QuickInput.rebind(&"jump", 1, new_event)
QuickInput.clear_binding(&"jump", 1)
QuickInput.reset_action(&"jump")
QuickInput.reset_all()
```

冲突策略：`ALLOW` 保留重复绑定；`REJECT` 不改动；`REPLACE` 清空冲突槽；`SWAP` 把被挤出的原绑定移到唯一冲突槽（冲突槽超过一个时拒绝）。预览事务绑定到当时的快照；若期间配置发生变化，提交会返回 `ERR_BUSY`，应重新预览。`binding_changed(action, slot)` 通知 UI 刷新。

`get_binding(action, slot)` 返回输入事件副本，`get_binding_text(action, slot)` 返回可显示文字或 `Unbound`。恢复默认使用首次配置时的 `InputMap` 快照；只持久化与默认不同的动作。恢复时文件读写出错将返回错误，不会静默覆盖现有映射。

若使用者在运行期间临时实例化管理器，可调用 `restore_defaults_in_input_map()` 还原由该管理器接管的动作并允许重新配置；**这不会修改磁盘存档**。

## 捕获 UI 与示例

将 `QuickInputRebindButton` 放入场景，在 Inspector 中填写 `action` 和 `slot`。它默认查找 `/root/QuickInput`，也可调用 `button.set_manager(manager)` 注入管理器。点击后捕获下一个键盘键、鼠标按钮/滚轮或手柄按钮；Escape / 手柄 UI Cancel 取消捕获。默认冲突策略 `REJECT` 会发出 `conflict_detected(preview)`，可通过 `confirm_conflict(policy)` 确认；也可以预设 `conflict_policy`。错误通过 `capture_failed(error)` 发出。

`res://addons/quick_input/gds/examples/basic_usage.tscn` 提供独立演示场景，会创建一个示例动作、两个槽位和捕获按钮；退出场景时恢复原始映射。示例使用默认绑定存档路径，建议在空白测试项目中运行。

## 文件格式与自检

默认 `QuickInputConfigFileStore` 使用 `user://quick_input_bindings.cfg`，写入前使用 `.tmp` 文件验证并通过 `.previous` 备份替换。未完成替换而主文件缺失时，下次加载会恢复 `.previous`。文件版本不匹配或数据无效时配置失败。手柄事件存储为“任意手柄”的按钮索引，不锁定捕获时的设备编号。

在项目目录运行（根据本机安装修改 Godot 路径）：

```text
godot --headless --editor --import
godot --headless --script res://addons/quick_input/gds/tests/test_quick_input_package.gd
godot --headless --script res://addons/quick_input/gds/tests/test_quick_input_runtime.gd
```

建议先建立独立的空白测试项目并复制本目录。包测试检查新路径、UID 文件、既有公共类名与插件互斥/所有权判断；若同装 C# 版，还会检查其独立插件配置，但不加载或编译 C#。运行时测试只临时创建三个 `quick_input_test_*` 动作，并使用带进程 ID 和时间戳的独立 `user://quick_input_gds_test_*.cfg` 文件及其 `.tmp` / `.previous` 辅助文件；正常结束时清理。已有同名动作或测试文件会使测试直接拒绝运行。测试不请求网络、不启动外部进程、不修改项目持久设置。
