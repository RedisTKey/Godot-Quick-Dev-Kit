extends SceneTree

const PROBE_SCENE := "res://addons/quick_level_kit/tests/fixtures/QuickProbeLevel.tscn"
const STORE_PATH := "user://quick_level_kit_manager_test_progress.cfg"
const ManagerScript := preload("res://addons/quick_level_kit/runtime/quick_level_kit_manager.gd")

var _failures := 0


func _init() -> void:
	call_deferred("_run")


func _run() -> void:
	await _test_load_and_flow()
	await _test_advance_unlocks_next()
	await _test_reload_and_finish()
	await _test_transition_rejected()
	await _test_reset_progress()
	if _failures == 0:
		print("Quick Level Kit manager tests passed.")
	quit(_failures)


func _make_catalog() -> QuickLevelKitCatalog:
	var catalog := QuickLevelKitCatalog.new()
	for level_id: StringName in [&"level_001", &"level_002", &"level_003"]:
		var definition := QuickLevelKitDefinition.new()
		definition.level_id = level_id
		definition.scene = load(PROBE_SCENE)
		catalog.levels.append(definition)
	catalog.default_flow = QuickLevelKitDefaultFlow.create()
	return catalog


func _make_manager() -> QuickLevelKitManager:
	var manager: QuickLevelKitManager = ManagerScript.new()
	root.add_child(manager)
	manager.set_catalog(_make_catalog())
	manager.set_progress_store(QuickLevelKitConfigFileProgressStore.new(STORE_PATH))
	manager.reset_progress()
	return manager


func _test_load_and_flow() -> void:
	var manager := _make_manager()
	var events: Array[String] = []
	manager.level_loaded.connect(func(_level: Node) -> void: events.append("loaded"))
	manager.session_started.connect(
		func(level_id: StringName) -> void: events.append("started:%s" % level_id)
	)
	_assert_true(manager.load_first_level(), "first level must load")
	_assert_equal(manager.get_current_level_id(), &"level_001", "current level must be set")
	_assert_equal(
		manager.get_current_stage_id(),
		&"run",
		"probe level completes preparation and must reach run"
	)
	_assert_equal(events, ["loaded", "started:level_001"], "load and session signals must fire")
	await _dispose(manager)


func _test_advance_unlocks_next() -> void:
	var manager := _make_manager()
	_assert_true(manager.load_first_level(), "first level must load")
	var level := manager.get_current_level()
	level.complete()
	_assert_equal(manager.get_current_level_id(), &"level_002", "complete must advance")
	_assert_true(manager.is_level_unlocked(&"level_002"), "advance must unlock next level")
	_assert_equal(
		manager.get_progress().highest_unlocked_level_id,
		&"level_002",
		"highest unlocked must persist"
	)
	await _dispose(manager)


func _test_reload_and_finish() -> void:
	var manager := _make_manager()
	manager.load_level(&"level_002")
	var before := manager.get_current_level()
	before.fail()
	_assert_equal(manager.get_current_level_id(), &"level_002", "fail must reload the same level")
	_assert_true(manager.get_current_level() != before, "reload must instantiate a new level")

	var final_manager := _make_manager()
	final_manager.load_level(&"level_003")
	final_manager.get_current_level().complete()
	_assert_false(final_manager.is_flow_active(), "final level completion must stop the flow")
	await _dispose(final_manager)
	await _dispose(manager)


func _test_transition_rejected() -> void:
	var manager := _make_manager()
	var rejected: Array[Dictionary] = []
	manager.transition_rejected.connect(
		func(context: Dictionary) -> void: rejected.append(context)
	)
	manager.load_first_level()
	manager.get_current_stage_id()
	_assert_false(
		manager.request_transition({&"type": &"unknown", &"data": {}}),
		"unrouted transition must return false"
	)
	_assert_equal(rejected.size(), 1, "unrouted transition must be observable")
	await _dispose(manager)


func _test_reset_progress() -> void:
	var manager := _make_manager()
	manager.load_level(&"level_003")
	manager.get_current_level().complete()
	_assert_true(manager.reset_progress(), "reset must succeed")
	_assert_equal(
		manager.get_progress().highest_unlocked_level_id,
		&"level_001",
		"reset must return to first level"
	)
	_assert_equal(
		manager.get_progress().level_states,
		{},
		"reset must clear custom level states"
	)
	await _dispose(manager)


func _dispose(manager: QuickLevelKitManager) -> void:
	if is_instance_valid(manager):
		manager.finish_flow()
		manager.free()
	await process_frame


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
