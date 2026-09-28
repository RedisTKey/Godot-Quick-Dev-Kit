extends SceneTree


class ProbeDocument:
	extends QuickDatabaseDocument

	var count := 0
	var label := "untitled"

	func get_schema_version() -> int:
		return 3

	func to_dictionary() -> Dictionary:
		return {"count": count, "label": label}

	func from_dictionary(data: Dictionary) -> void:
		var raw_count: Variant = data.get("count", 0)
		count = int(raw_count) if typeof(raw_count) == TYPE_INT else 0
		var raw_label: Variant = data.get("label", "untitled")
		label = String(raw_label) if typeof(raw_label) == TYPE_STRING else "untitled"


var _failures := 0
var _owned_paths: Array[String] = []


func _init() -> void:
	call_deferred("_run")


func _run() -> void:
	_test_missing_uses_defaults()
	_test_round_trip()
	_test_unknown_schema_defaults_and_status()
	_test_default_store_is_config_file()
	_cleanup_owned_paths()
	if _failures == 0:
		print("Quick Database repository tests passed.")
	quit(_failures)


func _test_missing_uses_defaults() -> void:
	var path := _new_path("missing")
	var repository := QuickDatabaseRepository.new(ProbeDocument, path)
	var document := repository.load_document()
	_assert_equal(document.get("count"), 0, "missing file must use default count")
	_assert_equal(document.get("label"), "untitled", "missing file must use default label")
	_assert_equal(
		repository.get_last_load_status(),
		QuickDatabaseStore.LoadStatus.MISSING,
		"repository must expose MISSING status"
	)
	_assert_false(FileAccess.file_exists(path), "loading must not create the file")


func _test_round_trip() -> void:
	var path := _new_path("round-trip")
	var repository := QuickDatabaseRepository.new(ProbeDocument, path)
	var document := ProbeDocument.new()
	document.count = 4
	document.label = "saved"
	_assert_equal(repository.save_document(document), OK, "save must succeed")

	var reloader := QuickDatabaseRepository.new(ProbeDocument, path)
	var loaded := reloader.load_document()
	_assert_equal(loaded.get("count"), 4, "count must round-trip")
	_assert_equal(loaded.get("label"), "saved", "label must round-trip")
	_assert_equal(
		reloader.get_last_load_status(),
		QuickDatabaseStore.LoadStatus.LOADED,
		"valid file must report LOADED"
	)
	_assert_equal(reloader.get_document_script(), ProbeDocument, "document script must be retained")


func _test_unknown_schema_defaults_and_status() -> void:
	var path := _new_path("unknown-schema")
	var store := QuickDatabaseConfigFileStore.new()
	_assert_equal(store.save_store(path, 99, {"count": 8}), OK, "fixture must save")

	var repository := QuickDatabaseRepository.new(ProbeDocument, path)
	var document := repository.load_document()
	_assert_equal(document.get("count"), 0, "unknown schema must fall back to defaults")
	_assert_equal(
		repository.get_last_load_status(),
		QuickDatabaseStore.LoadStatus.RECOVERED_UNKNOWN_SCHEMA,
		"repository must expose the store's recovery status"
	)
	_assert_true(
		repository.get_last_backup_path().ends_with(".corrupt.cfg"),
		"repository must expose the backup path"
	)
	_owned_paths.append(repository.get_last_backup_path())


func _test_default_store_is_config_file() -> void:
	var repository := QuickDatabaseRepository.new(ProbeDocument, "")
	_assert_true(
		repository.get_store() is QuickDatabaseConfigFileStore,
		"repository must default to the ConfigFile store"
	)
	_assert_equal(
		repository.get_save_path(),
		"",
		"repository must keep an empty path so the store uses its default"
	)


func _new_path(label: String) -> String:
	var path := "user://quick_database_repository_%s_%d.cfg" % [
		label, Time.get_ticks_usec()
	]
	_owned_paths.append(ProjectSettings.globalize_path(path))
	return path


func _cleanup_owned_paths() -> void:
	for path: String in _owned_paths:
		if FileAccess.file_exists(path):
			DirAccess.remove_absolute(path)


func _assert_equal(actual: Variant, expected: Variant, message: String) -> void:
	if actual != expected:
		_fail("%s; expected %s, got %s" % [message, expected, actual])


func _assert_true(value: bool, message: String) -> void:
	if not value:
		_fail(message)


func _assert_false(value: bool, message: String) -> void:
	if value:
		_fail(message)


func _fail(message: String) -> void:
	_failures += 1
	push_error(message)
