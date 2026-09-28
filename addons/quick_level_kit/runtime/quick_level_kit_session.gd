class_name QuickLevelKitSession
extends RefCounted

signal transition_rejected(context: Dictionary)
signal session_stopped()

var flow: QuickLevelKitFlow
var context: QuickLevelKitContext

var _current_stage_id: StringName = &""
var _stage_runtime: QuickLevelKitStageRuntime
var _active := false
var _pending_contexts: Array[Dictionary] = []
var _flushing := false


func _init(p_flow: QuickLevelKitFlow, p_context: QuickLevelKitContext) -> void:
	flow = p_flow
	context = p_context


func start() -> void:
	if _active or flow == null:
		return
	_active = true
	_enter_stage(flow.start_stage_id)
	_flush_pending()


func request_transition(context_payload: Dictionary) -> bool:
	if not _active or _stage_runtime == null:
		return false
	var route := _stage_runtime.stage.resolve_route(context_payload)
	var accepted := route != null and flow.has_stage(route.target_stage_id)
	_pending_contexts.append(context_payload)
	_flush_pending()
	return accepted


func stop() -> void:
	if not _active:
		return
	_active = false
	_pending_contexts.clear()
	_exit_current_stage()
	_stage_runtime = null
	_current_stage_id = &""
	session_stopped.emit()


func is_active() -> bool:
	return _active


func get_current_stage_id() -> StringName:
	return _current_stage_id


func get_current_stage() -> QuickLevelKitStage:
	return _stage_runtime.stage if _stage_runtime != null else null


func _flush_pending() -> void:
	if _flushing:
		return
	_flushing = true
	while _active and not _pending_contexts.is_empty():
		var payload := _pending_contexts.pop_front()
		_apply_transition(payload)
	_flushing = false


func _apply_transition(context_payload: Dictionary) -> void:
	if _stage_runtime == null:
		return
	var route := _stage_runtime.stage.resolve_route(context_payload)
	if route == null:
		transition_rejected.emit(context_payload)
		return
	var target := route.target_stage_id
	if not flow.has_stage(target):
		transition_rejected.emit(context_payload)
		return
	_exit_current_stage()
	_enter_stage(target)


func _enter_stage(stage_id: StringName) -> void:
	var stage := flow.get_stage(stage_id)
	if stage == null:
		_active = false
		return
	_current_stage_id = stage_id
	_stage_runtime = QuickLevelKitStageRuntime.new(stage, self)
	stage.on_enter(_stage_runtime)


func _exit_current_stage() -> void:
	if _stage_runtime != null:
		_stage_runtime.stage.on_exit(_stage_runtime)
