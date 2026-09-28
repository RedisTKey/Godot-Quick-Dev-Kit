extends SceneTree

const ROOT := "res://addons/quick_input"
const FILES := [
	"plugin.cfg",
	"quick_input_plugin.gd",
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
]

var _failures := 0


func _init() -> void:
	call_deferred("_run")


func _run() -> void:
	for file: String in FILES:
		_assert(FileAccess.file_exists("%s/%s" % [ROOT, file]), "缺少插件文件：%s" % file)
		if file.ends_with(".gd") and not file.begins_with("tests/"):
			var source := FileAccess.get_file_as_string("%s/%s" % [ROOT, file])
			_assert(not source.contains("res://_Core/") and not source.contains("/root/GameSave"),
				"插件引用了当前项目的输入或存档服务：%s" % file)

	var config := ConfigFile.new()
	_assert(config.load("%s/plugin.cfg" % ROOT) == OK, "plugin.cfg 不能加载")
	_assert(
		config.get_value("plugin", "script", "") == "quick_input_plugin.gd",
		"插件入口不正确"
	)
	var plugin := load("%s/quick_input_plugin.gd" % ROOT) as Script
	_assert(plugin != null, "编辑器插件脚本不能加载")
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


func _assert(condition: bool, message: String) -> void:
	if not condition:
		_failures += 1
		push_error(message)
