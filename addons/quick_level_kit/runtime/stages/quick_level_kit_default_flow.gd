class_name QuickLevelKitDefaultFlow
extends RefCounted

const STAGE_PREPARE := &"prepare"
const STAGE_RUN := &"run"
const STAGE_ADVANCE := &"advance"
const STAGE_RELOAD := &"reload"
const STAGE_STOP := &"stop"


static func create() -> QuickLevelKitFlow:
	var prepare := QuickLevelKitPrepareStage.new()
	prepare.stage_id = STAGE_PREPARE
	prepare.routes = [_route(&"ready", STAGE_RUN)]

	var run := QuickLevelKitRunStage.new()
	run.stage_id = STAGE_RUN
	run.routes = [
		_route(&"completed", STAGE_ADVANCE),
		_route(&"failed", STAGE_RELOAD),
		_route(&"retry", STAGE_RELOAD),
	]

	var advance := QuickLevelKitAdvanceStage.new()
	advance.stage_id = STAGE_ADVANCE
	advance.routes = [_route(&"finished", STAGE_STOP)]

	var reload := QuickLevelKitReloadStage.new()
	reload.stage_id = STAGE_RELOAD

	var stop := QuickLevelKitStopStage.new()
	stop.stage_id = STAGE_STOP

	var flow := QuickLevelKitFlow.new()
	flow.start_stage_id = STAGE_PREPARE
	flow.stages = [prepare, run, advance, reload, stop]
	return flow


static func _route(context_type: StringName, target: StringName) -> QuickLevelKitRoute:
	var route := QuickLevelKitRoute.new()
	route.context_type = context_type
	route.target_stage_id = target
	return route
