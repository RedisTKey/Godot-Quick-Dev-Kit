extends SceneTree

var _failures := 0


func _init() -> void:
	call_deferred("_run")


func _run() -> void:
	_test_route_matching()
	_test_ordered_route_priority()
	_test_flow_lookup_and_validation()
	if _failures == 0:
		print("Quick Level Kit flow tests passed.")
	quit(_failures)


func _make_stage(stage_id: StringName, routes: Array[QuickLevelKitRoute]) -> QuickLevelKitStage:
	var stage := QuickLevelKitStage.new()
	stage.stage_id = stage_id
	stage.routes = routes
	return stage


func _make_route(context_type: StringName, target: StringName) -> QuickLevelKitRoute:
	var route := QuickLevelKitRoute.new()
	route.context_type = context_type
	route.target_stage_id = target
	return route


func _test_route_matching() -> void:
	var route := _make_route(&"completed", &"advance")
	_assert_true(
		route.matches({&"type": &"completed", &"data": {}}),
		"route must match its context type"
	)
	_assert_false(
		route.matches({&"type": &"failed", &"data": {}}),
		"route must reject a different context type"
	)

	var typed := QuickLevelKitRoute.new()
	typed.context_type = &""
	_assert_true(
		typed.matches({&"type": &"anything", &"data": {}}),
		"empty context type must act as a wildcard"
	)

	var keyed := _make_route(&"completed", &"advance")
	keyed.required_payload_keys = PackedStringArray(["score"])
	_assert_false(
		keyed.matches({&"type": &"completed", &"data": {}}),
		"required payload key must be present"
	)
	_assert_true(
		keyed.matches({&"type": &"completed", &"data": {&"score": 10}}),
		"route must match when required payload key is present"
	)


func _test_ordered_route_priority() -> void:
	var stage := _make_stage(
		&"run",
		[
			_make_route(&"completed", &"advance"),
			_make_route(&"completed", &"reward"),
		]
	)
	var route := stage.resolve_route({&"type": &"completed", &"data": {}})
	_assert_equal(
		route.target_stage_id,
		&"advance",
		"first matching route in list order must win"
	)
	_assert_equal(
		stage.resolve_route({&"type": &"unknown"}),
		null,
		"unmatched context must return null"
	)


func _test_flow_lookup_and_validation() -> void:
	var flow := QuickLevelKitFlow.new()
	flow.start_stage_id = &"prepare"
	flow.stages = [
		_make_stage(&"prepare", [_make_route(&"ready", &"run")]),
		_make_stage(&"run", [_make_route(&"completed", &"advance")]),
		_make_stage(&"advance", []),
	]
	_assert_true(flow.validate(), "complete flow must validate")
	_assert_true(flow.has_stage(&"run"), "has_stage must find existing stage")
	_assert_equal(flow.get_stage(&"run").stage_id, &"run", "get_stage must return stage")
	_assert_equal(flow.get_stage(&"missing"), null, "get_stage must return null for missing")

	var broken := QuickLevelKitFlow.new()
	broken.start_stage_id = &"missing"
	broken.stages = flow.stages
	_assert_false(broken.validate(), "missing start stage must fail validation")

	var dangling := QuickLevelKitFlow.new()
	dangling.start_stage_id = &"prepare"
	dangling.stages = [
		_make_stage(&"prepare", [_make_route(&"ready", &"ghost")]),
	]
	_assert_false(dangling.validate(), "route to missing stage must fail validation")


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
