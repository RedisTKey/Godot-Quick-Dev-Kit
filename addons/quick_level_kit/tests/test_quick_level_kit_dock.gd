extends SceneTree

const DOCK_SCRIPT := preload("res://addons/quick_level_kit/editor/quick_level_kit_dock.gd")
const PROBE_SCENE := "res://addons/quick_level_kit/tests/fixtures/QuickProbeLevel.tscn"

var _failures := 0


func _init() -> void:
	call_deferred("_run")


func _run() -> void:
	var dock: Control = DOCK_SCRIPT.new()
	_test_add_and_reorder(dock)
	_test_validate_and_default_flow(dock)
	dock.free()
	if _failures == 0:
		print("Quick Level Kit dock tests passed.")
	quit(_failures)


func _test_add_and_reorder(dock: Control) -> void:
	var catalog := QuickLevelKitCatalog.new()
	dock.set_catalog(catalog)
	_assert_true(dock.add_level(&"level_001", load(PROBE_SCENE)), "dock must add a level")
	_assert_true(dock.add_level(&"level_002", load(PROBE_SCENE)), "dock must add a second level")
	_assert_true(dock.move_level(1, -1), "dock must move a level up")
	_assert_equal(catalog.levels[0].level_id, &"level_002", "move must reorder the catalog")
	_assert_true(dock.remove_level(0), "dock must remove a level")
	_assert_equal(catalog.get_level_count(), 1, "remove must shrink the catalog")


func _test_validate_and_default_flow(dock: Control) -> void:
	var catalog := QuickLevelKitCatalog.new()
	dock.set_catalog(catalog)
	dock.add_level(&"level_001", load(PROBE_SCENE))
	_assert_true(dock.validate_catalog().is_empty(), "valid catalog must report no errors")
	var flow: QuickLevelKitFlow = dock.create_default_flow_for(0)
	_assert_true(flow != null, "dock must create a default flow")
	_assert_true(flow.validate(), "created default flow must validate")
	_assert_true(catalog.levels[0].flow != null, "default flow must be assigned to the level")


func _assert_equal(actual: Variant, expected: Variant, message: String) -> void:
	if actual != expected:
		_fail("%s; expected %s, got %s" % [message, expected, actual])


func _assert_true(value: bool, message: String) -> void:
	if not value:
		_fail(message)


func _fail(message: String) -> void:
	_failures += 1
	push_error(message)
