extends SceneTree

const ADDON_ROOT := "res://addons/quick_database"
const REQUIRED_FILES := [
	"plugin.cfg",
	"quick_database_plugin.gd",
	"README.md",
	"runtime/quick_database_document.gd",
	"runtime/quick_database_repository.gd",
	"runtime/quick_database_service.gd",
	"runtime/stores/quick_database_store.gd",
	"runtime/stores/quick_database_config_file_store.gd",
	"examples/example_document.gd",
	"examples/basic_usage.gd",
	"examples/basic_usage.tscn",
]
const FORBIDDEN_REFERENCES := [
	"res://_Core/",
	"res://_Main/",
	"res://_Level/",
	"res://_UI/",
	"/root/GameSave",
	"/root/GameProgress",
	"/root/AudioManager",
	"GameSaveData",
	"GameSaveService",
	"GameSaveRepository",
	"highest_unlocked_level_id",
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
		print("Quick Database package tests passed.")
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
		"quick_database_plugin.gd",
		"plugin.cfg must declare the EditorPlugin entry"
	)
	var source := _read_text("%s/quick_database_plugin.gd" % ADDON_ROOT)
	_assert_true(
		source.contains('AUTOLOAD_NAME := "QuickDatabase"'),
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
	for relative_path: String in REQUIRED_FILES:
		if not relative_path.ends_with(".gd"):
			continue
		var source := _read_text("%s/%s" % [ADDON_ROOT, relative_path])
		for needle: String in FORBIDDEN_REFERENCES:
			_assert_false(
				source.contains(needle),
				"Addon file must not reference CountDown: %s (%s)"
				% [relative_path, needle]
			)


func _test_example_loads() -> void:
	var scene: PackedScene = load("%s/examples/basic_usage.tscn" % ADDON_ROOT)
	_assert_true(scene != null, "example scene must load")
	if scene != null:
		var instance := scene.instantiate()
		_assert_true(instance != null, "example scene must instantiate")
		if instance != null:
			instance.free()


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
