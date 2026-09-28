class_name QuickLevelKitContext
extends RefCounted

var level_id: StringName = &""
var level: Node
var manager: Object
var progress: QuickLevelKitProgress
var data: Dictionary = {}


func set_value(key: Variant, value: Variant) -> void:
	data[key] = value


func get_value(key: Variant, default_value: Variant = null) -> Variant:
	return data.get(key, default_value)
