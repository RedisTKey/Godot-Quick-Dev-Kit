class_name QuickLevelKitProgress
extends RefCounted

var current_level_id: StringName = &""
var highest_unlocked_level_id: StringName = &""
var level_states: Dictionary = {}


func to_dictionary() -> Dictionary:
	return {
		&"current_level_id": String(current_level_id),
		&"highest_unlocked_level_id": String(highest_unlocked_level_id),
		&"level_states": level_states.duplicate(true),
	}


func from_dictionary(data: Dictionary) -> void:
	current_level_id = StringName(data.get(&"current_level_id", &""))
	highest_unlocked_level_id = StringName(data.get(&"highest_unlocked_level_id", &""))
	level_states = (data.get(&"level_states", {}) as Dictionary).duplicate(true)


func duplicate_progress() -> QuickLevelKitProgress:
	var copy := QuickLevelKitProgress.new()
	copy.from_dictionary(to_dictionary())
	return copy


func get_level_state(level_id: StringName) -> Dictionary:
	var state: Variant = level_states.get(String(level_id), {})
	if state is Dictionary:
		return (state as Dictionary).duplicate(true)
	return {}


func set_level_state(level_id: StringName, state: Dictionary) -> void:
	level_states[String(level_id)] = state.duplicate(true)
