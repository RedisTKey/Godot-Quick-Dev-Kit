extends QuickDatabaseDocument

const EXAMPLE_SCHEMA_VERSION := 1

var count := 0
var title := ""


func get_schema_version() -> int:
	return EXAMPLE_SCHEMA_VERSION


func to_dictionary() -> Dictionary:
	return {"count": count, "title": title}


func from_dictionary(data: Dictionary) -> void:
	var raw_count: Variant = data.get("count", 0)
	count = int(raw_count) if typeof(raw_count) == TYPE_INT else 0
	var raw_title: Variant = data.get("title", "")
	title = String(raw_title) if typeof(raw_title) == TYPE_STRING else ""
