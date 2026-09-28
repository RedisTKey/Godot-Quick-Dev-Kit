extends SceneTree

var _failures := 0


func _init() -> void:
	call_deferred("_run")


func _run() -> void:
	_test_default_flow_structure()
	_test_default_flow_routes()
	if _failures == 0:
		print("Quick Level Kit default flow tests passed.")
	quit(_failures)


func _test_default_flow_structure() -> void:
	var flow := QuickLevelKitDefaultFlow.create()
	_assert_true(flow != null, "default flow must be created")
	_assert_true(flow.validate(), "default flow must validate: %s" % [flow.get_validation_errors()])
	_assert_equal(flow.start_stage_id, &"prepare", "default flow must start at prepare")
	for stage_id: StringName in [&"prepare", &"run", &"advance", &"reload", &"stop"]:
		_assert_true(flow.has_stage(stage_id), "default flow must contain stage %s" % stage_id)


func _test_default_flow_routes() -> void:
	var flow := QuickLevelKitDefaultFlow.create()
	var run := flow.get_stage(&"run")
	_assert_equal(
		run.resolve_route({&"type": &"completed", &"data": {}}).target_stage_id,
		&"advance",
		"completed must route to advance"
	)
	_assert_equal(
		run.resolve_route({&"type": &"failed", &"data": {}}).target_stage_id,
		&"reload",
		"failed must route to reload"
	)
	_assert_equal(
		run.resolve_route({&"type": &"retry", &"data": {}}).target_stage_id,
		&"reload",
		"retry must route to reload"
	)
	var prepare := flow.get_stage(&"prepare")
	_assert_equal(
		prepare.resolve_route({&"type": &"ready", &"data": {}}).target_stage_id,
		&"run",
		"ready must route to run"
	)
	var advance := flow.get_stage(&"advance")
	_assert_equal(
		advance.resolve_route({&"type": &"finished", &"data": {}}).target_stage_id,
		&"stop",
		"finished must route to stop"
	)


func _assert_equal(actual: Variant, expected: Variant, message: String) -> void:
	if actual != expected:
		_fail("%s; expected %s, got %s" % [message, expected, actual])


func _assert_true(value: bool, message: String) -> void:
	if not value:
		_fail(message)


func _fail(message: String) -> void:
	_failures += 1
	push_error(message)
