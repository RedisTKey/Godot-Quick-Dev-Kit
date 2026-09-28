class_name QuickLevelKitLevel
extends Node

signal preparation_completed
signal transition_requested(context: Dictionary)

@export var level_id: StringName = &""
var flow_manager: Object

var _preparation_started := false
var _preparation_completed := false
var _finished := false
var _torn_down := false


func begin_preparation() -> void:
	if _torn_down:
		return
	_preparation_started = true
	_preparation_completed = false
	_finished = false
	_prepare_level()


func complete_preparation() -> void:
	if not _preparation_started or _preparation_completed:
		return
	_preparation_completed = true
	preparation_completed.emit()


func begin_running() -> void:
	if _torn_down or _finished:
		return
	_preparation_started = false
	_start_level()


func complete(context: Dictionary = {}) -> void:
	_request_transition(&"completed", context)


func fail(context: Dictionary = {}) -> void:
	_request_transition(&"failed", context)


func retry(context: Dictionary = {}) -> void:
	_request_transition(&"retry", context)


func teardown() -> void:
	if _torn_down:
		return
	_torn_down = true
	_teardown_level()


func _request_transition(type: StringName, data: Dictionary) -> void:
	if _torn_down:
		return
	_finished = true
	if not data.is_empty():
		_finish_level(data)
	var context := {&"type": type, &"data": data}
	transition_requested.emit(context)
	if flow_manager != null and flow_manager.has_method(&"request_transition"):
		flow_manager.call(&"request_transition", context)
	elif is_inside_tree():
		var autoload := get_node_or_null("/root/QuickLevelKit")
		if autoload != null:
			autoload.call(&"request_transition", context)


func _prepare_level() -> void:
	pass


func _start_level() -> void:
	pass


func _finish_level(_context: Dictionary) -> void:
	pass


func _teardown_level() -> void:
	pass
