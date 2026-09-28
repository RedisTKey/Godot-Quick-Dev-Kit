extends SceneTree

const ADDON_ROOT := "res://addons/quick_audio_manager"
const REQUIRED_FILES := [
	"plugin.cfg",
	"quick_audio_manager_plugin.gd",
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
]

var _failures := 0


func _init() -> void:
	call_deferred("_run")


func _run() -> void:
	_test_required_files()
	_test_plugin_registration_contract()
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


func _test_plugin_registration_contract() -> void:
	var config := ConfigFile.new()
	_assert_equal(
		config.load("%s/plugin.cfg" % ADDON_ROOT),
		OK,
		"plugin.cfg must be readable"
	)
	_assert_equal(
		config.get_value("plugin", "script", ""),
		"quick_audio_manager_plugin.gd",
		"plugin.cfg must declare the EditorPlugin entry"
	)
	var source := _read_text("%s/quick_audio_manager_plugin.gd" % ADDON_ROOT)
	_assert_true(
		source.contains('AUTOLOAD_NAME := "QuickAudioManager"'),
		"EditorPlugin must use the isolated Autoload name"
	)
	_assert_true(
		source.contains("add_autoload_singleton"),
		"Enabling the plugin must register its Autoload"
	)
	_assert_true(
		source.contains("remove_autoload_singleton"),
		"Disabling the plugin must remove its own Autoload"
	)
	_assert_true(
		source.contains('AUTOLOAD_PATH := "res://addons/quick_audio_manager/runtime/quick_audio_manager_service.gd"'),
		"EditorPlugin must register the packaged service"
	)
	_assert_true(
		source.contains("ProjectSettings.has_setting"),
		"EditorPlugin must detect an occupied Autoload name"
	)
	_assert_true(
		source.contains("ProjectSettings.get_setting"),
		"EditorPlugin must verify ownership before removal"
	)


func _test_runtime_is_project_independent() -> void:
	for relative_path: String in REQUIRED_FILES:
		if not relative_path.ends_with(".gd") and not relative_path.ends_with(".tres"):
			continue
		var source := _read_text("%s/%s" % [ADDON_ROOT, relative_path])
		_assert_false(
			source.contains("res://_Core/Audio") or source.contains("/root/AudioManager"),
			"Addon file must not reference CountDown audio code: %s" % relative_path
		)
		_assert_false(
			source.contains("class_name AudioAsset")
			or source.contains("class_name AudioTrackDefinition")
			or source.contains("class_name AudioTrackLibrary")
			or source.contains("class_name AudioManagerService"),
			"Addon public classes must use QuickAudio names: %s" % relative_path
		)


func _test_example_loads() -> void:
	_assert_true(
		ResourceLoader.exists("%s/examples/basic_usage.tscn" % ADDON_ROOT),
		"Basic usage scene must be importable"
	)
	_assert_true(
		load("%s/examples/basic_usage.tscn" % ADDON_ROOT) is PackedScene,
		"Basic usage scene must load as PackedScene"
	)


func _read_text(path: String) -> String:
	var file := FileAccess.open(path, FileAccess.READ)
	if file == null:
		return ""
	return file.get_as_text()


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
