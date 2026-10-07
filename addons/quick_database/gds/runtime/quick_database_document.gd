class_name QuickDatabaseDocument
extends RefCounted

const SCHEMA_VERSION := 1


func get_schema_version() -> int:
	return SCHEMA_VERSION


func to_dictionary() -> Dictionary:
	return {}


func from_dictionary(_data: Dictionary) -> void:
	pass


func duplicate_document() -> QuickDatabaseDocument:
	var copy: QuickDatabaseDocument = get_script().new()
	copy.from_dictionary(to_dictionary())
	return copy
