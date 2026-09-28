extends SceneTree

var _failures := 0


func _init() -> void:
	call_deferred("_run")


func _run() -> void:
	_test_stage_enter_and_route()
	_test_unmatched_context_is_rejected()
	_test_context_persists_across_stages()
	_test_synchronous_transition_chain()
	_test_stop_disables_session()
	if _failures == 0:
		print("Quick Level Kit session tests passed.")
	quit(_failures)


func _make_route(context_type: StringName, target: StringName) -> QuickLevelKitRoute:
	var route := QuickLevelKitRoute.new()
	route.context_type = context_type
	route.target_stage_id = target
	return route


func _test_stage_enter_and_route() -> void:
	var probe := QuickSessionProbeStage.new()
	probe.stage_id = &"start"
	probe.routes = [_make_route(&"go", &"finish")]
	var finish := QuickLevelKitStage.new()
	finish.stage_id = &"finish"
	var flow := QuickLevelKitFlow.new()
	flow.start_stage_id = &"start"
	flow.stages = [probe, finish]

	var context := QuickLevelKitContext.new()
	var session := QuickLevelKitSession.new(flow, context)
	session.start()
	_assert_equal(session.get_current_stage_id(), &"start", "session must start at start stage")
	_assert_equal(probe.entered, 1, "start stage must be entered once")
	_assert_true(session.request_transition({&"type": &"go", &"data": {}}), "route must succeed")
	_assert_equal(session.get_current_stage_id(), &"finish", "session must move to target stage")
	_assert_equal(probe.exited, 1, "start stage must be exited")
	session.stop()


func _test_unmatched_context_is_rejected() -> void:
	var stage := QuickLevelKitStage.new()
	stage.stage_id = &"start"
	var flow := QuickLevelKitFlow.new()
	flow.start_stage_id = &"start"
	flow.stages = [stage]
	var session := QuickLevelKitSession.new(flow, QuickLevelKitContext.new())
	var rejected: Array[Dictionary] = []
	session.transition_rejected.connect(
		func(context: Dictionary) -> void: rejected.append(context)
	)
	session.start()
	_assert_false(
		session.request_transition({&"type": &"nope", &"data": {}}),
		"unmatched context must return false"
	)
	_assert_equal(session.get_current_stage_id(), &"start", "session must keep current stage")
	_assert_equal(rejected.size(), 1, "rejection must be observable")
	session.stop()


func _test_context_persists_across_stages() -> void:
	var first := QuickSessionProbeStage.new()
	first.stage_id = &"start"
	first.routes = [_make_route(&"go", &"finish")]
	var second := QuickSessionProbeStage.new()
	second.stage_id = &"finish"
	var flow := QuickLevelKitFlow.new()
	flow.start_stage_id = &"start"
	flow.stages = [first, second]
	var context := QuickLevelKitContext.new()
	context.set_value(&"score", 3)
	var session := QuickLevelKitSession.new(flow, context)
	session.start()
	session.request_transition({&"type": &"go", &"data": {}})
	_assert_equal(second.seen_score, 3, "same context must be shared across stages")
	session.stop()


func _test_synchronous_transition_chain() -> void:
	var start := QuickSessionProbeStage.new()
	start.stage_id = &"start"
	start.auto_request = {&"type": &"go", &"data": {}}
	start.routes = [_make_route(&"go", &"middle")]
	var middle := QuickSessionProbeStage.new()
	middle.stage_id = &"middle"
	middle.auto_request = {&"type": &"go", &"data": {}}
	middle.routes = [_make_route(&"go", &"end")]
	var end := QuickLevelKitStage.new()
	end.stage_id = &"end"
	var flow := QuickLevelKitFlow.new()
	flow.start_stage_id = &"start"
	flow.stages = [start, middle, end]
	var session := QuickLevelKitSession.new(flow, QuickLevelKitContext.new())
	session.start()
	_assert_equal(
		session.get_current_stage_id(),
		&"end",
		"transitions requested synchronously during enter must be queued and processed"
	)
	session.stop()


func _test_stop_disables_session() -> void:
	var stage := QuickLevelKitStage.new()
	stage.stage_id = &"start"
	var flow := QuickLevelKitFlow.new()
	flow.start_stage_id = &"start"
	flow.stages = [stage]
	var session := QuickLevelKitSession.new(flow, QuickLevelKitContext.new())
	session.start()
	session.stop()
	_assert_false(session.is_active(), "stopped session must be inactive")
	_assert_false(
		session.request_transition({&"type": &"any", &"data": {}}),
		"stopped session must reject transitions"
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
