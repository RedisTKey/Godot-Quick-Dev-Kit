@tool
extends EditorPlugin

# Installed only by run_cold_load.sh into its disposable project.
var _frames := 0
var _failures := 0
var _assertions := 0

func _enter_tree() -> void:
	set_process(true)

func _process(_delta: float) -> void:
	if EditorInterface.get_resource_filesystem().is_scanning():
		return
	_frames += 1
	if _frames != 90:
		return
	set_process(false)
	var compiled := OS.get_environment("QUICK_VFX_EXPECT_COMPILED") == "1"
	var phase := "built" if compiled else "cold"
	var config := ConfigFile.new()
	_check(config.load("res://addons/quick_vfx_manager/csharp/plugin.cfg") == OK, "manifest loads")
	_check(config.get_value("plugin", "script", "missing") == "", "manifest has no C# initialization dependency")
	_check(not FileAccess.file_exists("res://addons/quick_vfx_manager/csharp/QuickVfxPlugin.cs"), "obsolete editor source removed")
	_check(not FileAccess.file_exists("res://addons/quick_vfx_manager/csharp/QuickVfxPlugin.cs.uid"), "obsolete editor UID removed")
	_check(EditorInterface.is_plugin_enabled("quick_vfx_manager/csharp"), "plugin is really enabled in the editor")
	_check(ProjectSettings.get_setting("editor_plugins/enabled").has("res://addons/quick_vfx_manager/csharp/plugin.cfg"), "enabled setting retains VFX")
	var playback_script = load("res://addons/quick_vfx_manager/csharp/runtime/VfxPlayback.cs")
	var definition_script = load("res://addons/quick_vfx_manager/csharp/runtime/VfxDefinition.cs")
	if compiled:
		_check(playback_script.get_instance_base_type() == "Node", "compiled playback type loads")
		_check(definition_script.get_instance_base_type() == "Resource", "compiled definition type loads")
		var registered := {}
		for entry in ProjectSettings.get_global_class_list():
			registered[entry.get("class", "")] = true
		for expected in ["VfxPlayback", "VfxDefinition", "VfxEffect", "VfxParticles", "VfxAnimation", "VfxShaderParameter"]:
			_check(registered.has(expected), "GlobalClass registered: " + expected)
		if playback_script.get_instance_base_type() == "Node" and definition_script.get_instance_base_type() == "Resource":
			var playback = playback_script.new()
			var definition = definition_script.new()
			_check(playback is Node, "playback instantiates after Build")
			_check(definition is Resource, "definition instantiates after Build")
			_check(playback.get("Definitions") is Array, "Inspector definition array is exported")
			playback.free()
	else:
		_check(not FileAccess.file_exists("res://.godot/mono/temp/bin/Debug/QuickVfxColdLoad.dll"), "no project DLL exists during cold check")
		_check(playback_script.get_instance_base_type() == "", "C# runtime remains unavailable before Build")
	if _failures == 0:
		print("VFX_EDITOR_LOAD_PASS phase=", phase, " assertions=", _assertions)
	else:
		push_error("VFX_EDITOR_LOAD_FAIL phase=%s failures=%d" % [phase, _failures])
	get_tree().quit(0 if _failures == 0 else 1)

func _check(condition: bool, description: String) -> void:
	_assertions += 1
	if not condition:
		_failures += 1
		push_error("VFX editor-load assertion failed: " + description)
