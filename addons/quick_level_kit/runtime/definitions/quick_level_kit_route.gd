class_name QuickLevelKitRoute
extends Resource

@export var context_type: StringName = &""
@export var target_stage_id: StringName = &""
@export var required_payload_keys: PackedStringArray = PackedStringArray()


func matches(context: Dictionary) -> bool:
	if not context_type.is_empty() and context.get(&"type", &"") != context_type:
		return false
	if required_payload_keys.is_empty():
		return true
	var data: Variant = context.get(&"data", {})
	if not data is Dictionary:
		return false
	for key: String in required_payload_keys:
		if not (data as Dictionary).has(StringName(key)):
			return false
	return true
