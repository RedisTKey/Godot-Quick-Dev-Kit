class_name QuickInputBinding
extends RefCounted

var action: StringName
var slot := 0
var event: InputEvent


func _init(p_action: StringName = &"", p_slot: int = 0, p_event: InputEvent = null) -> void:
	action = p_action
	slot = p_slot
	event = p_event.duplicate() if p_event != null else null
