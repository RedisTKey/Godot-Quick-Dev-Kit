class_name QuickInputMapAdapter
extends RefCounted


func get_events(action: StringName) -> Array[InputEvent]:
	if not InputMap.has_action(action):
		return []
	var events: Array[InputEvent] = []
	for event: InputEvent in InputMap.action_get_events(action):
		events.append(event.duplicate())
	return events


func has_action(action: StringName) -> bool:
	return InputMap.has_action(action)


func set_events(action: StringName, events: Array[InputEvent]) -> void:
	InputMap.action_erase_events(action)
	for event: InputEvent in events:
		InputMap.action_add_event(action, event.duplicate())
