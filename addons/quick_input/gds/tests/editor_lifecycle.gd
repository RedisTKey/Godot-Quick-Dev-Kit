@tool
extends EditorPlugin
## Used only by tests/run_quick_input.py in a disposable project.

const GDS := "quick_input/gds"
const CS := "quick_input/csharp"
const GDS_VALUE := "*res://addons/quick_input/gds/runtime/quick_input_manager.gd"
const CS_VALUE := "*res://addons/quick_input/csharp/runtime/QuickInputManager.cs"
var _failures := 0

func _enter_tree() -> void:
	_run.call_deferred()

func _check(condition: bool, message: String) -> void:
	if not condition:
		_failures += 1
		push_error(message)

func _value() -> Variant:
	var value: String = ProjectSettings.get_setting("autoload/QuickInput", "")
	if value.begins_with("*uid://"):
		var uid := ResourceUID.text_to_id(value.trim_prefix("*"))
		if ResourceUID.has_id(uid):
			return "*" + ResourceUID.get_id_path(uid)
	return value

func _reject_enable(plugin: String) -> void:
	# Only the rejected activation's expected diagnostic is suppressed.
	var previous := Engine.print_error_messages
	Engine.print_error_messages = false
	EditorInterface.set_plugin_enabled(plugin, true)
	Engine.print_error_messages = previous

func _run() -> void:
	await get_tree().process_frame
	while EditorInterface.get_resource_filesystem().is_scanning():
		await get_tree().process_frame
	await get_tree().process_frame
	_check(not ProjectSettings.has_setting("autoload/QuickInput"), "Fixture starts without QuickInput")
	EditorInterface.set_plugin_enabled(GDS, true)
	_check(_value() == GDS_VALUE, "GDS enable installs its autoload")
	_reject_enable(CS)
	_check(_value() == GDS_VALUE, "C# rejected enable preserves GDS autoload")
	EditorInterface.set_plugin_enabled(CS, false)
	_check(_value() == GDS_VALUE, "C# rejected disable preserves GDS autoload")
	EditorInterface.set_plugin_enabled(GDS, false)
	_check(not ProjectSettings.has_setting("autoload/QuickInput"), "GDS disable removes own autoload")
	await get_tree().process_frame
	EditorInterface.set_plugin_enabled(CS, true)
	_check(_value() == CS_VALUE, "C# enable installs its autoload")
	_reject_enable(GDS)
	_check(_value() == CS_VALUE, "GDS rejected enable preserves C# autoload")
	EditorInterface.set_plugin_enabled(GDS, false)
	_check(_value() == CS_VALUE, "GDS rejected disable preserves C# autoload")
	EditorInterface.set_plugin_enabled(CS, false)
	_check(not ProjectSettings.has_setting("autoload/QuickInput"), "C# disable removes own autoload")
	await get_tree().process_frame
	# A third-party autoload is never overwritten or removed by either entry point.
	add_autoload_singleton("QuickInput", "res://foreign_service.gd")
	for plugin: String in [GDS, CS]:
		_reject_enable(plugin)
		_check(_value() == "*res://foreign_service.gd", "Foreign service survives rejected enable")
		EditorInterface.set_plugin_enabled(plugin, false)
		_check(_value() == "*res://foreign_service.gd", "Foreign service survives rejected disable")
	remove_autoload_singleton("QuickInput")
	print("Quick Input editor lifecycle: 13 checks, %d failures." % _failures)
	get_tree().quit(0 if _failures == 0 else 1)
