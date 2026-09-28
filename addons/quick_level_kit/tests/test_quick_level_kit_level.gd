extends SceneTree

var _failures := 0


func _init() -> void:
	call_deferred("_run")


func _run() -> void:
	_test_lifecycle_hooks()
	_test_unified_transition_entry()
	_test_preparation_emits_once()
	if _failures == 0:
		print("Quick Level Kit level tests passed.")
	quit(_failures)


func _test_lifecycle_hooks() -> void:
	var level := QuickLevelProbe.new()
	level.begin_preparation()
	_assert_equal(level.events, [&"prepare"], "begin_preparation must call _prepare_level")
	level.complete_preparation()
	level.begin_running()
	_assert_equal(level.events, [&"prepare", &"start"], "begin_running must call _start_level")
	level.complete({&"score": 5})
	_assert_equal(level.events, [&"prepare", &"start", &"finish"], "complete must finish the level")
	level.teardown()
	_assert_equal(level.events[-1], &"teardown", "teardown must call _teardown_level")
	level.free()


func _test_unified_transition_entry() -> void:
	var level := QuickLevelProbe.new()
	var captured: Array[Dictionary] = []
	level.transition_requested.connect(
		func(context: Dictionary) -> void: captured.append(context)
	)
	level.complete({&"score": 9})
	_assert_equal(
		captured,
		[{&"type": &"completed", &"data": {&"score": 9}}],
		"complete must emit the unified transition context"
	)
	level.fail()
	_assert_equal(captured[1][&"type"], &"failed", "fail must emit failed context")
	level.retry()
	_assert_equal(captured[2][&"type"], &"retry", "retry must emit retry context")
	level.free()


func _test_preparation_emits_once() -> void:
	var level := QuickLevelProbe.new()
	var count := [0]
	level.preparation_completed.connect(func() -> void: count[0] += 1)
	level.begin_preparation()
	level.complete_preparation()
	level.complete_preparation()
	_assert_equal(count[0], 1, "preparation completion must be idempotent")
	level.free()


func _assert_equal(actual: Variant, expected: Variant, message: String) -> void:
	if actual != expected:
		_fail("%s; expected %s, got %s" % [message, expected, actual])


func _fail(message: String) -> void:
	_failures += 1
	push_error(message)
