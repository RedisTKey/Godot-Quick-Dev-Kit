extends SceneTree

const STORE_PATH := "user://quick_level_kit_test_progress.cfg"

var _failures := 0


func _init() -> void:
	call_deferred("_run")


func _run() -> void:
	_test_progress_round_trip()
	_test_config_store_round_trip()
	_test_config_store_clear()
	if _failures == 0:
		print("Quick Level Kit progress tests passed.")
	quit(_failures)


func _test_progress_round_trip() -> void:
	var progress := QuickLevelKitProgress.new()
	progress.current_level_id = &"level_002"
	progress.highest_unlocked_level_id = &"level_004"
	progress.set_level_state(&"level_001", {&"stars": 3})
	_assert_equal(
		progress.get_level_state(&"level_001"),
		{&"stars": 3},
		"level state must round-trip in memory"
	)

	var restored := QuickLevelKitProgress.new()
	restored.from_dictionary(progress.to_dictionary())
	_assert_equal(restored.current_level_id, &"level_002", "current level must round-trip")
	_assert_equal(
		restored.highest_unlocked_level_id,
		&"level_004",
		"highest unlocked must round-trip"
	)
	_assert_equal(
		restored.get_level_state(&"level_001"),
		{&"stars": 3},
		"level state must survive dictionary round-trip"
	)
	var copy := progress.duplicate_progress()
	copy.set_level_state(&"level_002", {&"done": true})
	_assert_equal(
		progress.get_level_state(&"level_002"),
		{},
		"duplicate_progress must deep-copy level states"
	)


func _test_config_store_round_trip() -> void:
	var store := QuickLevelKitConfigFileProgressStore.new(STORE_PATH)
	store.clear()
	var progress := QuickLevelKitProgress.new()
	progress.current_level_id = &"level_003"
	progress.highest_unlocked_level_id = &"level_005"
	progress.set_level_state(&"level_002", {&"deaths": 7})
	_assert_equal(store.save_progress(progress), OK, "saving progress must succeed")

	var loaded := store.load_progress()
	_assert_equal(loaded.current_level_id, &"level_003", "store must persist current level")
	_assert_equal(
		loaded.highest_unlocked_level_id,
		&"level_005",
		"store must persist highest unlocked"
	)
	_assert_equal(
		loaded.get_level_state(&"level_002"),
		{&"deaths": 7},
		"store must persist custom level state"
	)


func _test_config_store_clear() -> void:
	var store := QuickLevelKitConfigFileProgressStore.new(STORE_PATH)
	store.clear()
	_assert_true(
		not FileAccess.file_exists(STORE_PATH),
		"clear must remove the save file"
	)
	var empty := store.load_progress()
	_assert_equal(empty.current_level_id, &"", "missing file must load empty progress")


func _assert_equal(actual: Variant, expected: Variant, message: String) -> void:
	if actual != expected:
		_fail("%s; expected %s, got %s" % [message, expected, actual])


func _assert_true(value: bool, message: String) -> void:
	if not value:
		_fail(message)


func _fail(message: String) -> void:
	_failures += 1
	push_error(message)
