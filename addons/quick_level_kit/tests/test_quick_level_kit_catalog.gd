extends SceneTree

const PROBE_SCENE := "res://addons/quick_level_kit/tests/fixtures/QuickProbeLevel.tscn"

var _failures := 0


func _init() -> void:
	call_deferred("_run")


func _run() -> void:
	_test_navigation()
	_test_validation_reports_errors()
	_test_flow_resolution()
	if _failures == 0:
		print("Quick Level Kit catalog tests passed.")
	quit(_failures)


func _make_catalog() -> QuickLevelKitCatalog:
	var catalog := QuickLevelKitCatalog.new()
	catalog.levels = [
		_make_definition(&"level_001", 0),
		_make_definition(&"level_002", 1),
		_make_definition(&"level_003", 2),
	]
	return catalog


func _make_definition(level_id: StringName, probe_index: int) -> QuickLevelKitDefinition:
	var definition := QuickLevelKitDefinition.new()
	definition.level_id = level_id
	definition.scene = load(PROBE_SCENE)
	return definition


func _test_navigation() -> void:
	var catalog := _make_catalog()
	_assert_equal(catalog.get_level_count(), 3, "catalog must count ordered levels")
	_assert_equal(catalog.get_first_level_id(), &"level_001", "first id must be index 0")
	_assert_equal(catalog.get_last_level_id(), &"level_003", "last id must be final entry")
	_assert_equal(catalog.find_index(&"level_002"), 1, "find_index must locate by id")
	_assert_equal(catalog.find_index(&"missing"), -1, "unknown id must return -1")

	var next := catalog.resolve_next(&"level_001")
	_assert_equal(next.status, QuickLevelKitCatalog.NextStatus.AVAILABLE, "middle level must resolve")
	_assert_equal(next.level_id, &"level_002", "next level id must be returned")
	var final := catalog.resolve_next(&"level_003")
	_assert_equal(final.status, QuickLevelKitCatalog.NextStatus.FINAL, "last level must be final")
	var unknown := catalog.resolve_next(&"missing")
	_assert_equal(unknown.status, QuickLevelKitCatalog.NextStatus.UNKNOWN, "unknown must report unknown")


func _test_validation_reports_errors() -> void:
	var valid := _make_catalog()
	_assert_true(valid.validate(), "well-formed catalog must validate")

	var empty_id := _make_catalog()
	empty_id.levels[0].level_id = &""
	_assert_false(empty_id.validate(), "empty level id must fail validation")

	var duplicate := _make_catalog()
	duplicate.levels[1].level_id = &"level_001"
	_assert_false(duplicate.validate(), "duplicate level id must fail validation")

	var missing_scene := _make_catalog()
	missing_scene.levels[0].scene = null
	_assert_false(missing_scene.validate(), "missing scene must fail validation")

	var bad_root := _make_catalog()
	bad_root.levels[0].scene = PackedScene.new()
	var bad_root_node := Node.new()
	bad_root_node.name = "NotALevel"
	bad_root.levels[0].scene.pack(bad_root_node)
	bad_root_node.free()
	_assert_false(bad_root.validate(), "non QuickLevelKitLevel root must fail validation")

	var errors := empty_id.get_validation_errors()
	_assert_true(errors.size() >= 1, "validation errors must be observable")
	_assert_true(errors[0].has(&"code"), "validation error must carry a code")


func _test_flow_resolution() -> void:
	var catalog := _make_catalog()
	var shared := QuickLevelKitFlow.new()
	catalog.default_flow = shared
	_assert_equal(catalog.get_flow_for(&"level_001"), shared, "catalog default flow must be used")

	var override := QuickLevelKitFlow.new()
	catalog.levels[0].flow = override
	_assert_equal(
		catalog.get_flow_for(&"level_001"),
		override,
		"level-specific flow must override the catalog default"
	)


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
