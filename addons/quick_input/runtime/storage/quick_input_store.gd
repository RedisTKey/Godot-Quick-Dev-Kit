class_name QuickInputStore
extends RefCounted

var load_error: Error = OK


func load_bindings() -> Dictionary:
	load_error = ERR_UNAVAILABLE
	return {}


func save_bindings(_bindings: Dictionary) -> Error:
	return ERR_UNAVAILABLE
