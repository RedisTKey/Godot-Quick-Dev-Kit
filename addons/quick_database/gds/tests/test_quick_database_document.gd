extends SceneTree


class ProbeDocument:
	extends QuickDatabaseDocument

	var count := 0
	var label := "untitled"
	var ratios: Dictionary = {}

	func get_schema_version() -> int:
		return 7

	func to_dictionary() -> Dictionary:
		return {
			"count": count,
			"label": label,
			"ratios": ratios.duplicate(true),
		}

	func from_dictionary(data: Dictionary) -> void:
		var raw_count: Variant = data.get("count", 0)
		count = int(raw_count) if typeof(raw_count) == TYPE_INT else 0
		var raw_label: Variant = data.get("label", "untitled")
		label = String(raw_label) if typeof(raw_label) == TYPE_STRING else "untitled"
		var raw_ratios: Variant = data.get("ratios", {})
		ratios = (raw_ratios as Dictionary).duplicate(true) if raw_ratios is Dictionary else {}


var _failures := 0


func _init() -> void:
	call_deferred("_run")


func _run() -> void:
	_test_round_trip()
	_test_duplicate_is_deep()
	_test_invalid_fields_fall_back_individually()
	if _failures == 0:
		print("Quick Database document tests passed.")
	quit(_failures)


func _test_round_trip() -> void:
	var document := ProbeDocument.new()
	document.count = 3
	document.label = "hello"
	document.ratios = {"a": 0.5}
	_assert_equal(document.get_schema_version(), 7, "schema version must be overridable")

	var restored := ProbeDocument.new()
	restored.from_dictionary(document.to_dictionary())
	_assert_equal(restored.count, 3, "count must round-trip")
	_assert_equal(restored.label, "hello", "label must round-trip")
	_assert_equal(restored.ratios, {"a": 0.5}, "nested dictionary must round-trip")


func _test_duplicate_is_deep() -> void:
	var document := ProbeDocument.new()
	document.count = 1
	document.ratios = {"a": 0.5}
	var copy := document.duplicate_document()
	_assert_true(copy != null, "duplicate_document must return a document")
	if copy == null:
		return
	_assert_equal(copy.get("count"), 1, "duplicate must copy scalar fields")
	copy.set("count", 99)
	copy.get("ratios")["a"] = 9.0
	_assert_equal(document.count, 1, "mutating a duplicate must not change the original")
	_assert_equal(document.ratios["a"], 0.5, "duplicate must deep-copy nested data")
	_assert_true(
		copy is QuickDatabaseDocument,
		"duplicate must preserve the document type"
	)


func _test_invalid_fields_fall_back_individually() -> void:
	var document := ProbeDocument.new()
	document.from_dictionary({
		"count": "not-an-int",
		"label": 42,
		"ratios": "not-a-dictionary",
	})
	_assert_equal(document.count, 0, "invalid count must fall back to default")
	_assert_equal(document.label, "untitled", "invalid label must fall back to default")
	_assert_equal(document.ratios, {}, "invalid nested value must fall back to default")

	var partial := ProbeDocument.new()
	partial.from_dictionary({"count": 5})
	_assert_equal(partial.count, 5, "valid field must load")
	_assert_equal(partial.label, "untitled", "missing field must use default")


func _assert_equal(actual: Variant, expected: Variant, message: String) -> void:
	if actual != expected:
		_fail("%s; expected %s, got %s" % [message, expected, actual])


func _assert_true(value: bool, message: String) -> void:
	if not value:
		_fail(message)


func _fail(message: String) -> void:
	_failures += 1
	push_error(message)
