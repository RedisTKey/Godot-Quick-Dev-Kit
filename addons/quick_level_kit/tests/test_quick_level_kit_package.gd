extends SceneTree

const ADDON_ROOT := "res://addons/quick_level_kit"
const REQUIRED_FILES := [
	"plugin.cfg",
	"quick_level_kit_plugin.gd",
	"README.md",
	"runtime/quick_level_kit_manager.gd",
	"runtime/quick_level_kit_level.gd",
	"runtime/quick_level_kit_context.gd",
	"runtime/quick_level_kit_stage_runtime.gd",
	"runtime/quick_level_kit_session.gd",
	"runtime/definitions/quick_level_kit_definition.gd",
	"runtime/definitions/quick_level_kit_catalog.gd",
	"runtime/definitions/quick_level_kit_route.gd",
	"runtime/definitions/quick_level_kit_stage.gd",
	"runtime/definitions/quick_level_kit_flow.gd",
	"runtime/stages/quick_level_kit_prepare_stage.gd",
	"runtime/stages/quick_level_kit_run_stage.gd",
	"runtime/stages/quick_level_kit_advance_stage.gd",
	"runtime/stages/quick_level_kit_reload_stage.gd",
	"runtime/stages/quick_level_kit_stop_stage.gd",
	"runtime/stages/quick_level_kit_default_flow.gd",
	"runtime/progress/quick_level_kit_progress.gd",
	"runtime/progress/quick_level_kit_progress_store.gd",
	"runtime/progress/quick_level_kit_config_file_progress_store.gd",
	"editor/quick_level_kit_dock.gd",
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
		print("Quick Level Kit package tests passed.")
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
		"quick_level_kit_plugin.gd",
		"plugin.cfg must declare the EditorPlugin entry"
	)
	var source := _read_text("%s/quick_level_kit_plugin.gd" % ADDON_ROOT)
	_assert_true(
		source.contains('AUTOLOAD_NAME := "QuickLevelKit"'),
		"EditorPlugin must register the isolated Autoload name"
	)
	_assert_true(
		source.contains("add_autoload_singleton"),
		"Enabling the plugin must register its Autoload"
	)
	_assert_true(
		source.contains("remove_autoload_singleton"),
		"Disabling the plugin must remove its own Autoload"
	)


func _test_runtime_is_project_independent() -> void:
	var forbidden := [
		"res://_Core/",
		"res://_Main/",
		"res://_Level/",
		"res://_UI/",
		"/root/AudioManager",
		"/root/GameProgress",
		"/root/GameSave",
	]
	for relative_path: String in REQUIRED_FILES:
		if not relative_path.ends_with(".gd"):
			continue
		var source := _read_text("%s/%s" % [ADDON_ROOT, relative_path])
		for needle: String in forbidden:
			_assert_false(
				source.contains(needle),
				"Addon file must not reference CountDown: %s (%s)" % [relative_path, needle]
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


func _test_example_loads() -> void:
	_assert_true(
		ResourceLoader.exists("%s/examples/basic_usage.tscn" % ADDON_ROOT),
		"Basic usage scene must be importable"
	)
	_assert_true(
		load("%s/examples/basic_usage.tscn" % ADDON_ROOT) is PackedScene,
		"Basic usage scene must load as PackedScene"
	)
