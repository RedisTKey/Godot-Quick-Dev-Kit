class_name QuickLevelProbe
extends QuickLevelKitLevel

var events: Array[StringName] = []


func _prepare_level() -> void:
	events.append(&"prepare")


func _start_level() -> void:
	events.append(&"start")


func _finish_level(_context: Dictionary) -> void:
	events.append(&"finish")


func _teardown_level() -> void:
	events.append(&"teardown")
