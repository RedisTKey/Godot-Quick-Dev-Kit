extends SceneTree

const ROOT := "res://addons/quick_input/gds"
const PluginGuard = preload("res://addons/quick_input/gds/quick_input_plugin_guard.gd")
const FILES := [
	"plugin.cfg",
	"quick_input_plugin.gd",
	"quick_input_plugin_guard.gd",
	"README.md",
	"runtime/quick_input_manager.gd",
	"runtime/quick_input_settings.gd",
	"runtime/quick_input_binding.gd",
	"runtime/quick_input_binding_transaction.gd",
	"runtime/quick_input_rebind_result.gd",
	"runtime/definitions/quick_input_action_definition.gd",
	"runtime/adapters/quick_input_map_adapter.gd",
	"runtime/storage/quick_input_store.gd",
	"runtime/storage/quick_input_config_file_store.gd",
	"runtime/serialization/quick_input_event_codec.gd",
	"ui/quick_input_rebind_button.gd",
	"examples/basic_usage.gd",
	"examples/basic_usage.tscn",
	"tests/test_quick_input_package.gd",
	"tests/test_quick_input_runtime.gd",
]
const PUBLIC_CLASSES := {
	"runtime/quick_input_manager.gd": "QuickInputManager",
	"runtime/quick_input_settings.gd": "QuickInputSettings",
	"runtime/quick_input_binding.gd": "QuickInputBinding",
	"runtime/quick_input_binding_transaction.gd": "QuickInputBindingTransaction",
	"runtime/quick_input_rebind_result.gd": "QuickInputRebindResult",
	"runtime/definitions/quick_input_action_definition.gd": "QuickInputActionDefinition",
	"runtime/adapters/quick_input_map_adapter.gd": "QuickInputMapAdapter",
	"runtime/storage/quick_input_store.gd": "QuickInputStore",
	"runtime/storage/quick_input_config_file_store.gd": "QuickInputConfigFileStore",
	"runtime/serialization/quick_input_event_codec.gd": "QuickInputEventCodec",
	"ui/quick_input_rebind_button.gd": "QuickInputRebindButton",
}

var _failures := 0


func _init() -> void:
	call_deferred("_run")


func _run() -> void:
	for file: String in FILES:
		_assert(FileAccess.file_exists("%s/%s" % [ROOT, file]), "缺少插件文件：%s" % file)
		if file.ends_with(".gd"):
			_assert(FileAccess.file_exists("%s/%s.uid" % [ROOT, file]), "缺少脚本 UID：%s" % file)
		if file.ends_with(".gd") and not file.begins_with("tests/"):
			var source := FileAccess.get_file_as_string("%s/%s" % [ROOT, file])
			_assert(not source.contains("res://_Core/") and not source.contains("/root/GameSave"),
				"插件引用了当前项目的输入或存档服务：%s" % file)
			_assert(not source.contains("res://addons/quick_input/runtime/")
				and not source.contains("res://addons/quick_input/ui/"),
				"插件仍引用迁移前的路径：%s" % file)
	for file: String in PUBLIC_CLASSES:
		var source := FileAccess.get_file_as_string("%s/%s" % [ROOT, file])
		_assert(source.contains("class_name %s\n" % PUBLIC_CLASSES[file]),
			"GDScript 公共类名不应改变：%s" % file)
	_assert(not FileAccess.file_exists("res://addons/quick_input/plugin.cfg"),
		"公共目录不应再注册第三个 Quick Input 插件")

	var config := ConfigFile.new()
	_assert(config.load("%s/plugin.cfg" % ROOT) == OK, "plugin.cfg 不能加载")
	_assert(config.get_value("plugin", "name", "") == "Quick Input (GDScript)",
		"GDScript 插件必须有独立的显示名称")
	_assert(
		config.get_value("plugin", "script", "") == "quick_input_plugin.gd",
		"插件入口不正确"
	)
	var plugin := load("%s/quick_input_plugin.gd" % ROOT) as Script
	_assert(plugin != null, "编辑器插件脚本不能加载")
	if plugin != null:
		_assert(plugin.get_script_constant_map().get("AUTOLOAD_PATH", "")
			== ROOT + "/runtime/quick_input_manager.gd", "Autoload 路径未迁移")
	_test_registration_guard()
	_test_optional_csharp_package()
	_assert(load("%s/runtime/quick_input_manager.gd" % ROOT) is Script,
		"运行时管理器脚本不能加载")
	if _failures > 0:
		quit(_failures)
		return
	var scene := load("%s/examples/basic_usage.tscn" % ROOT) as PackedScene
	_assert(scene != null, "最小示例场景不能加载")
	var manager := QuickInputManager.new()
	_assert(
		manager.configure(QuickInputSettings.new(), QuickInputStore.new()) == ERR_INVALID_PARAMETER,
		"空动作配置必须被拒绝"
	)
	manager.free()
	if _failures == 0:
		print("Quick Input package tests passed.")
	quit(_failures)


func _test_registration_guard() -> void:
	var no_plugins := PackedStringArray()
	var sibling_enabled := PackedStringArray([PluginGuard.OTHER_PLUGIN])
	var our_autoload: String = PluginGuard.AUTOLOAD_VALUE
	var sibling_autoload := "*res://addons/quick_input/csharp/runtime/QuickInputManager.cs"
	var unrelated_autoload := "*res://my_game_input.gd"
	var uid := ResourceLoader.get_resource_uid(our_autoload.trim_prefix("*"))
	_assert(uid != ResourceUID.INVALID_ID, "GDScript manager must have an imported UID")
	_assert(PluginGuard.owns_autoload("*" + ResourceUID.id_to_text(uid)),
		"Godot 4.6 UID Autoload must resolve to the owned script")
	_assert(PluginGuard.should_remove_autoload("*" + ResourceUID.id_to_text(uid), false),
		"UID Autoload must also clean up after editor restart")
	_assert(PluginGuard.registration_error(false, "", no_plugins).is_empty(),
		"无冲突时必须允许注册")
	_assert(PluginGuard.registration_error(true, our_autoload, no_plugins).is_empty(),
		"启用相同的 Autoload 必须幂等")
	_assert(not PluginGuard.registration_error(false, "", sibling_enabled).is_empty(),
		"C# 插件启用时不得注册 GDScript Autoload")
	_assert(not PluginGuard.registration_error(true, our_autoload, sibling_enabled).is_empty(),
		"已有本版 Autoload 也不能绕过插件互斥检查")
	for value: Variant in [sibling_autoload, unrelated_autoload, our_autoload.trim_prefix("*"), "", 42]:
		_assert(not PluginGuard.registration_error(true, value, no_plugins).is_empty(),
			"已占用或错误的 Autoload 配置不得被覆盖：%s" % str(value))
		_assert(not PluginGuard.should_remove_autoload(value, false),
			"停用不得移除其他实现或错误配置的 Autoload：%s" % str(value))
	_assert(PluginGuard.should_remove_autoload(our_autoload, false),
		"重新加载后应能根据确切路径清理本版 Autoload")
	_assert(not PluginGuard.should_remove_autoload(our_autoload, true),
		"拒绝启用后的停用不得改变原有 Autoload")
	_assert(not PluginGuard.should_remove_autoload("", false),
		"重复停用必须安全")


func _test_optional_csharp_package() -> void:
	# GDScript remains independently installable. Verify sibling identity when both
	# are installed, without compiling/loading C# or depending on a .NET editor.
	if not FileAccess.file_exists(PluginGuard.OTHER_PLUGIN):
		return
	var config := ConfigFile.new()
	_assert(config.load(PluginGuard.OTHER_PLUGIN) == OK, "C# 插件配置不能加载")
	_assert(config.get_value("plugin", "name", "") == "Quick Input (C#)",
		"两个实现必须有独立的插件名称")
	var entry: String = config.get_value("plugin", "script", "")
	_assert(not entry.is_empty() and not entry.contains("..") and entry.ends_with(".cs"),
		"C# 插件入口必须位于自己的目录中")
	_assert(FileAccess.file_exists(PluginGuard.OTHER_PLUGIN.get_base_dir().path_join(entry)),
		"C# 插件入口文件不存在")


func _assert(condition: bool, message: String) -> void:
	if not condition:
		_failures += 1
		push_error(message)
