extends SceneTree

var _failures := 0

func _init() -> void:
	call_deferred("_run")

func _run() -> void:
	var native = load("res://addons/quick_database/csharp/runtime/stores/QuickDatabaseConfigFileStore.cs").new()
	var original := QuickDatabaseConfigFileStore.new()
	var path := "user://database_interop_%d.cfg" % Time.get_ticks_usec()
	var payload := {"count": 7, "title": "跨语言存档", "nested": {"flag": true, "items": [1, "two", Vector2(3, 4)]}}
	_check(original.save_store(path, 1, payload) == OK, "GDScript writes")
	var from_gd: Dictionary = native.LoadStore(path, 1)
	_check(from_gd.get("payload") == payload, "C# reads exact original Variant payload")
	_check(int(from_gd.get("status")) == QuickDatabaseStore.LoadStatus.LOADED, "status values preserved")
	payload["count"] = 12
	payload["nested"]["flag"] = false
	_check(native.SaveStore(path, 1, payload) == OK, "C# writes")
	_check(original.load_store(path, 1).get("payload") == payload, "GDScript reads native C# payload")
	_check(native.EraseStore(path) == OK, "native erase interoperable file")
	_check(not FileAccess.file_exists(path), "fixture erased")
	# Native document serialization matches the original example schema.
	var cs_document = load("res://addons/quick_database/csharp/examples/ExampleDocument.cs").new()
	var gd_document = load("res://addons/quick_database/gds/examples/example_document.gd").new()
	cs_document.FromDictionary({"count": 2147483648, "title": "same"})
	gd_document.from_dictionary(cs_document.ToDictionary())
	_check(gd_document.to_dictionary() == cs_document.ToDictionary(), "document schema parity")
	_check(cs_document.ToDictionary().get("count") == 2147483648, "document preserves int64")
	_check(native.SaveStore(path, 2147483648, payload) == OK, "large int64 schema writes")
	_check(original.load_store(path, 2147483648).get("payload") == payload, "large schema interoperates")
	_check(native.EraseStore(path) == OK, "large schema fixture erased")
	if _failures == 0:
		print("DATABASE_INTEROP_PASS assertions=12")
	quit(0 if _failures == 0 else 1)

func _check(condition: bool, description: String) -> void:
	if not condition:
		_failures += 1
		push_error("DATABASE_INTEROP_FAIL " + description)
