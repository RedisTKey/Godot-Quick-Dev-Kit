extends Node

const ExampleDocument := preload(
	"res://addons/quick_database/examples/example_document.gd"
)
const SLOT := &"example"
const SAVE_PATH := "user://quick_database_example.cfg"


func _ready() -> void:
	var database := _database()
	if database == null:
		return
	database.open_slot(SLOT, ExampleDocument, SAVE_PATH)
	var document = database.get_document(SLOT)
	if document != null:
		print(
			"Quick Database example loaded count=%s title=%s"
			% [str(document.get("count")), str(document.get("title"))]
		)


func increment() -> void:
	var database := _database()
	if database == null:
		return
	database.update_slot(
		SLOT,
		&"count",
		func(document: QuickDatabaseDocument) -> void: document.set("count", int(document.get("count")) + 1)
	)
	database.flush_slot(SLOT)


func _database() -> Node:
	return get_node_or_null("/root/QuickDatabase")
