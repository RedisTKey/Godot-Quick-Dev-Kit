extends SceneTree

const ADDON_ROOT := "res://addons/quick_audio_manager/gds"
const PluginGuard = preload("res://addons/quick_audio_manager/gds/quick_audio_manager_plugin_guard.gd")
const REQUIRED_FILES := [
	"plugin.cfg",
	"quick_audio_manager_plugin.gd",
	"quick_audio_manager_plugin_guard.gd",
	"README.md",
	"runtime/quick_audio_asset.gd",
	"runtime/quick_audio_track_definition.gd",
	"runtime/quick_audio_track_library.gd",
	"runtime/quick_audio_manager_service.gd",
	"resources/default_track_library.tres",
	"resources/tracks/music.tres",
	"resources/tracks/sfx.tres",
	"resources/tracks/ui.tres",
	"examples/basic_usage.gd",
	"examples/basic_usage.tscn",
	"tests/test_quick_audio_manager.gd",
	"tests/test_quick_audio_manager_package.gd",
	"tests/editor_lifecycle.gd",
]
const PUBLIC_CLASSES := {
	"runtime/quick_audio_asset.gd": "QuickAudioAsset",
	"runtime/quick_audio_track_definition.gd": "QuickAudioTrackDefinition",
	"runtime/quick_audio_track_library.gd": "QuickAudioTrackLibrary",
	"runtime/quick_audio_manager_service.gd": "QuickAudioManagerService",
}

var _failures := 0


func _init() -> void:
	call_deferred("_run")


func _run() -> void:
	_test_required_files()
	_test_plugin_registration_contract()
	_test_registration_guard()
	_test_optional_csharp_package()
	_test_runtime_is_project_independent()
	_test_example_loads()
	if _failures == 0:
		print("Quick Audio Manager package tests passed.")
	quit(_failures)


func _test_required_files() -> void:
	for relative_path: String in REQUIRED_FILES:
		_assert_true(
			FileAccess.file_exists("%s/%s" % [ADDON_ROOT, relative_path]),
			"Missing addon file: %s" % relative_path
		)
		if relative_path.ends_with(".gd"):
			_assert_true(FileAccess.file_exists("%s/%s.uid" % [ADDON_ROOT, relative_path]),
				"Missing script UID: %s" % relative_path)
	_assert_false(FileAccess.file_exists("res://addons/quick_audio_manager/plugin.cfg"),
		"Common root must not register a third audio plugin")
	for relative_path: String in PUBLIC_CLASSES:
		_assert_true(_read_text("%s/%s" % [ADDON_ROOT, relative_path]).contains(
			"class_name %s\n" % PUBLIC_CLASSES[relative_path]),
			"GDScript public class name must be preserved: %s" % relative_path)


func _test_plugin_registration_contract() -> void:
	var config := ConfigFile.new()
	_assert_equal(config.load("%s/plugin.cfg" % ADDON_ROOT), OK,
		"plugin.cfg must be readable")
	_assert_equal(config.get_value("plugin", "name", ""), "Quick Audio Manager (GDScript)",
		"GDScript plugin must have a distinct display name")
	_assert_equal(config.get_value("plugin", "script", ""), "quick_audio_manager_plugin.gd",
		"plugin.cfg must declare the EditorPlugin entry")
	var plugin := load("%s/quick_audio_manager_plugin.gd" % ADDON_ROOT) as Script
	_assert_true(plugin != null, "EditorPlugin script must load")
	if plugin != null:
		_assert_equal(plugin.get_script_constant_map().get("AUTOLOAD_NAME", ""),
			"QuickAudioManager", "EditorPlugin must keep the public Autoload name")
		_assert_equal(plugin.get_script_constant_map().get("AUTOLOAD_PATH", ""),
			ADDON_ROOT + "/runtime/quick_audio_manager_service.gd",
			"EditorPlugin must register the migrated service")


func _test_registration_guard() -> void:
	var no_plugins := PackedStringArray()
	var sibling_enabled := PackedStringArray([PluginGuard.OTHER_PLUGIN])
	var our_autoload: String = PluginGuard.AUTOLOAD_VALUE
	var sibling_autoload := "*res://addons/quick_audio_manager/csharp/runtime/QuickAudioManagerService.cs"
	var unrelated_autoload := "*res://project_audio.gd"
	var uid := ResourceLoader.get_resource_uid(our_autoload.trim_prefix("*"))
	_assert_true(uid != ResourceUID.INVALID_ID, "GDScript service must have an imported UID")
	var uid_autoload := "*" + ResourceUID.id_to_text(uid)
	_assert_true(PluginGuard.owns_autoload(uid_autoload),
		"Godot UID Autoload must resolve to the owned service")
	_assert_true(PluginGuard.should_remove_autoload(uid_autoload, false),
		"UID ownership must survive editor restart")
	_assert_true(PluginGuard.registration_error(false, "", no_plugins).is_empty(),
		"Unoccupied registration must succeed")
	_assert_true(PluginGuard.registration_error(true, our_autoload, no_plugins).is_empty(),
		"Registering the existing owned service must be idempotent")
	_assert_true(PluginGuard.registration_error(true, uid_autoload, no_plugins).is_empty(),
		"Registering the existing owned UID service must be idempotent")
	_assert_false(PluginGuard.registration_error(false, "", sibling_enabled).is_empty(),
		"Enabled C# sibling must block GDScript registration")
	_assert_false(PluginGuard.registration_error(true, our_autoload, sibling_enabled).is_empty(),
		"An existing owned Autoload must not bypass mutual exclusion")
	var unknown_uid := "*" + ResourceUID.id_to_text(ResourceUID.create_id())
	for value: Variant in [sibling_autoload, unrelated_autoload,
		our_autoload.trim_prefix("*"), uid_autoload.trim_prefix("*"), unknown_uid, "", 42]:
		_assert_false(PluginGuard.registration_error(true, value, no_plugins).is_empty(),
			"Occupied or malformed Autoload must be rejected: %s" % str(value))
		_assert_false(PluginGuard.should_remove_autoload(value, false),
			"Disable must preserve unowned or malformed Autoload: %s" % str(value))
	_assert_true(PluginGuard.should_remove_autoload(our_autoload, false),
		"Exact-path ownership must survive editor restart")
	_assert_false(PluginGuard.should_remove_autoload(our_autoload, true),
		"Rejected enable must not later delete a pre-existing owned Autoload")
	_assert_false(PluginGuard.should_remove_autoload(uid_autoload, true),
		"Rejected enable must preserve a pre-existing owned UID Autoload")
	_assert_false(PluginGuard.should_remove_autoload("", false),
		"Repeated disable must be safe")


func _test_optional_csharp_package() -> void:
	# GDScript can be installed alone and never needs to compile/load C#.
	if not FileAccess.file_exists(PluginGuard.OTHER_PLUGIN):
		return
	var config := ConfigFile.new()
	_assert_equal(config.load(PluginGuard.OTHER_PLUGIN), OK, "C# plugin.cfg must be readable")
	_assert_equal(config.get_value("plugin", "name", ""), "Quick Audio Manager (C#)",
		"Sibling plugin must have its own display name")
	var entry: String = config.get_value("plugin", "script", "")
	_assert_true(not entry.is_empty() and not entry.contains("..") and entry.ends_with(".cs"),
		"C# plugin entry must remain inside its own directory")
	_assert_true(FileAccess.file_exists(PluginGuard.OTHER_PLUGIN.get_base_dir().path_join(entry)),
		"C# plugin entry must exist")


func _test_runtime_is_project_independent() -> void:
	for relative_path: String in REQUIRED_FILES:
		if relative_path.begins_with("tests/"):
			continue
		if not relative_path.ends_with(".gd") and not relative_path.ends_with(".tres") and not relative_path.ends_with(".tscn"):
			continue
		var source := _read_text("%s/%s" % [ADDON_ROOT, relative_path])
		_assert_false(source.contains("res://_Core/Audio") or source.contains("/root/AudioManager"),
			"Addon must not reference project-specific audio code: %s" % relative_path)
		for directory: String in ["runtime", "resources", "examples"]:
			_assert_false(source.contains("res://addons/quick_audio_manager/%s/" % directory),
				"Addon must not reference pre-migration paths: %s" % relative_path)
		_assert_false(source.contains("class_name AudioAsset")
			or source.contains("class_name AudioTrackDefinition")
			or source.contains("class_name AudioTrackLibrary")
			or source.contains("class_name AudioManagerService"),
			"Addon public classes must use QuickAudio names: %s" % relative_path)


func _test_example_loads() -> void:
	_assert_true(ResourceLoader.exists("%s/examples/basic_usage.tscn" % ADDON_ROOT),
		"Basic usage scene must be importable")
	_assert_true(load("%s/examples/basic_usage.tscn" % ADDON_ROOT) is PackedScene,
		"Basic usage scene must load as PackedScene")


func _read_text(path: String) -> String:
	return FileAccess.get_file_as_string(path)


func _assert_equal(actual: Variant, expected: Variant, message: String) -> void:
	if actual != expected:
		_fail("%s; expected %s, got %s" % [message, expected, actual])


func _assert_true(value: bool, message: String) -> void:
	if not value:
		_fail(message)


func _assert_false(value: bool, message: String) -> void:
	if value:
		_fail(message)


func _fail(message: String) -> void:
	_failures += 1
	push_error(message)
