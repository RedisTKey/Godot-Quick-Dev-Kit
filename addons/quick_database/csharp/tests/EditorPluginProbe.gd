@tool
extends EditorPlugin

# Copied into a disposable observer addon by tests/quick_database/run.sh.
var _frames := 0
var _failures := 0
var _assertions := 0
const CS_PLUGIN := "quick_database/csharp"
const GD_PLUGIN := "quick_database/gds"
const CS_PATH := "res://addons/quick_database/csharp/runtime/QuickDatabaseService.cs"
const GD_PATH := "res://addons/quick_database/gds/runtime/quick_database_service.gd"

func _process(_delta: float) -> void:
	if EditorInterface.get_resource_filesystem().is_scanning():
		return
	_frames += 1
	if _frames != 30:
		return
	set_process(false)
	var phase := OS.get_environment("QUICK_DATABASE_EDITOR_PHASE")
	if phase == "cold":
		_check(not FileAccess.file_exists("res://.godot/mono/temp/bin/Debug/QuickDatabaseTests.dll"), "fresh import has no assembly")
		_check(not EditorInterface.is_plugin_enabled(CS_PLUGIN), "C# plugin waits for first build")
		_check(not ProjectSettings.has_setting("autoload/QuickDatabase"), "fresh import does not silently register a broken autoload")
	else:
		if phase == "restart":
			_check(EditorInterface.is_plugin_enabled(CS_PLUGIN), "C# enabled setting survives editor restart")
			_check(_autoload_path() == CS_PATH, "C# owned autoload survives editor restart")
			EditorInterface.set_plugin_enabled(CS_PLUGIN, false)
		var script = load(CS_PATH)
		_check(script.get_instance_base_type() == "Node", "native C# service type loads")
		var instance = script.new()
		_check(instance is Node, "native C# service derives Node")
		instance.free()
		for iteration in range(3):
			EditorInterface.set_plugin_enabled(CS_PLUGIN, true)
			_check(_autoload_path() == CS_PATH, "C# enable owns autoload cycle %d" % iteration)
			EditorInterface.set_plugin_enabled(CS_PLUGIN, false)
			_check(not ProjectSettings.has_setting("autoload/QuickDatabase"), "C# disable removes autoload cycle %d" % iteration)
		EditorInterface.set_plugin_enabled(GD_PLUGIN, true)
		_check(_autoload_path() == GD_PATH, "GDScript enable registers relocated path")
		EditorInterface.set_plugin_enabled(CS_PLUGIN, true)
		_check(_autoload_path() == GD_PATH, "C# must not overwrite GDScript autoload")
		EditorInterface.set_plugin_enabled(CS_PLUGIN, false)
		_check(_autoload_path() == GD_PATH, "disabling non-owner keeps GDScript autoload")
		EditorInterface.set_plugin_enabled(GD_PLUGIN, false)
		_check(not ProjectSettings.has_setting("autoload/QuickDatabase"), "GDScript disable removes owned autoload")
		EditorInterface.set_plugin_enabled(CS_PLUGIN, true)
		EditorInterface.set_plugin_enabled(GD_PLUGIN, true)
		_check(_autoload_path() == CS_PATH, "GDScript must not overwrite C# autoload")
		EditorInterface.set_plugin_enabled(GD_PLUGIN, false)
		_check(_autoload_path() == CS_PATH, "disabling GDScript non-owner keeps C# autoload")
		EditorInterface.set_plugin_enabled(CS_PLUGIN, false)
		var scene: PackedScene = load("res://addons/quick_database/csharp/examples/BasicUsage.tscn")
		var demo = scene.instantiate()
		var exported := {}
		for property in demo.get_property_list():
			if int(property.usage) & PROPERTY_USAGE_EDITOR:
				exported[property.name] = true
		for required in ["SlotName", "SavePath", "InitialTitle"]:
			_check(exported.has(required), "Inspector field: " + required)
		demo.free()
		if phase == "persist":
			EditorInterface.set_plugin_enabled(CS_PLUGIN, true)
			_check(_autoload_path() == CS_PATH, "registered autoload before editor restart")
			_check(ProjectSettings.save() == OK, "save editor plugin state for restart")
		else:
			_check(ProjectSettings.save() == OK, "save disabled plugin state for isolated runtime tests")
	if _failures == 0:
		print("DATABASE_EDITOR_PASS phase=", phase, " assertions=", _assertions)
	get_tree().quit(0 if _failures == 0 else 1)

func _autoload_path() -> String:
	var path := String(ProjectSettings.get_setting("autoload/QuickDatabase", "")).trim_prefix("*")
	if path.begins_with("uid://"):
		var id := ResourceUID.text_to_id(path)
		if ResourceUID.has_id(id):
			path = ResourceUID.get_id_path(id)
	return path

func _check(condition: bool, description: String) -> void:
	_assertions += 1
	if not condition:
		_failures += 1
		push_error("DATABASE_EDITOR_FAIL " + description)
