extends SceneTree


class ProbeDocument:
	extends QuickDatabaseDocument

	var count := 0

	func get_schema_version() -> int:
		return 1

	func to_dictionary() -> Dictionary:
		return {"count": count}

	func from_dictionary(data: Dictionary) -> void:
		var raw_count: Variant = data.get("count", 0)
		count = int(raw_count) if typeof(raw_count) == TYPE_INT else 0


class RenameFailureStore:
	extends QuickDatabaseConfigFileStore

	var promote_failures_remaining := 0

	func _rename_absolute(source_path: String, target_path: String) -> Error:
		if promote_failures_remaining > 0 and source_path.ends_with(".tmp"):
			promote_failures_remaining -= 1
			return ERR_CANT_CREATE
		return super._rename_absolute(source_path, target_path)


var _failures := 0
var _owned_paths: Array[String] = []


func _init() -> void:
	call_deferred("_run")


func _run() -> void:
	_test_open_and_defaults()
	_test_update_marks_dirty_and_flushes()
	_test_multi_slot_isolation()
	_test_commit_success_and_failure_rollback()
	_test_reload_and_erase()
	_test_close_flushes_dirty_slot()
	_cleanup_owned_paths()
	if _failures == 0:
		print("Quick Database service tests passed.")
	quit(_failures)


func _test_open_and_defaults() -> void:
	var path := _new_path("open")
	var service := QuickDatabaseService.new()
	var opened: Array[StringName] = []
	service.slot_opened.connect(func(slot: StringName) -> void: opened.append(slot))
	_assert_true(
		service.open_slot(&"main", ProbeDocument, path),
		"open_slot must succeed"
	)
	_assert_false(
		service.open_slot(&"main", ProbeDocument, path),
		"opening the same slot twice must fail"
	)
	_assert_true(service.has_slot(&"main"), "has_slot must report the open slot")
	_assert_equal(service.get_slot_names(), [&"main"], "slot names must be exposed")
	_assert_equal(service.get_save_path(&"main"), path, "slot path must be exposed")
	_assert_false(service.is_dirty(&"main"), "a fresh slot must not be dirty")
	_assert_equal(opened, [&"main"], "open_slot must emit slot_opened once")

	var snapshot := service.get_document(&"main")
	_assert_equal(snapshot.get("count"), 0, "new slot must use document defaults")
	snapshot.set("count", 99)
	_assert_equal(
		int(service.get_document(&"main").get("count")),
		0,
		"get_document must return an isolated snapshot"
	)
	service.free()


func _test_update_marks_dirty_and_flushes() -> void:
	var path := _new_path("update")
	var service := QuickDatabaseService.new()
	service.open_slot(&"main", ProbeDocument, path)
	var changed: Array[StringName] = []
	service.data_changed.connect(
		func(_slot: StringName, field: StringName) -> void: changed.append(field)
	)
	_assert_true(
		service.update_slot(
			&"main",
			&"count",
			func(document: QuickDatabaseDocument) -> void: document.set("count", 5)
		),
		"update_slot must succeed"
	)
	_assert_true(service.is_dirty(&"main"), "update_slot must mark the slot dirty")
	_assert_equal(changed, [&"count"], "update_slot must emit data_changed")
	_assert_equal(service.flush_slot(&"main"), OK, "flush_slot must succeed")
	_assert_false(service.is_dirty(&"main"), "successful flush must clear dirty")

	var reloader := QuickDatabaseService.new()
	reloader.open_slot(&"main", ProbeDocument, path)
	_assert_equal(
		int(reloader.get_document(&"main").get("count")),
		5,
		"flushed value must persist"
	)
	service.free()
	reloader.free()


func _test_multi_slot_isolation() -> void:
	var path_a := _new_path("slot-a")
	var path_b := _new_path("slot-b")
	var service := QuickDatabaseService.new()
	service.open_slot(&"a", ProbeDocument, path_a)
	service.open_slot(&"b", ProbeDocument, path_b)
	service.update_slot(
		&"a",
		&"count",
		func(document: QuickDatabaseDocument) -> void: document.set("count", 7)
	)
	_assert_true(service.is_dirty(&"a"), "slot a must be dirty")
	_assert_false(service.is_dirty(&"b"), "slot b must remain clean")
	_assert_equal(service.flush_all(), OK, "flush_all must succeed")
	_assert_false(service.is_dirty(&"a"), "flush_all must clear slot a dirty")

	var reloader := QuickDatabaseService.new()
	reloader.open_slot(&"a", ProbeDocument, path_a)
	reloader.open_slot(&"b", ProbeDocument, path_b)
	_assert_equal(int(reloader.get_document(&"a").get("count")), 7, "slot a must persist")
	_assert_equal(int(reloader.get_document(&"b").get("count")), 0, "slot b must stay default")
	service.free()
	reloader.free()


func _test_commit_success_and_failure_rollback() -> void:
	var path := _new_path("commit")
	var service := QuickDatabaseService.new()
	service.open_slot(&"main", ProbeDocument, path)
	var changed: Array[StringName] = []
	service.data_changed.connect(
		func(_slot: StringName, field: StringName) -> void: changed.append(field)
	)
	_assert_equal(
		service.commit_slot(
			&"main",
			&"count",
			func(document: QuickDatabaseDocument) -> void: document.set("count", 5)
		),
		OK,
		"commit_slot must succeed"
	)
	_assert_equal(
		int(service.get_document(&"main").get("count")),
		5,
		"successful commit must update the live document"
	)
	_assert_equal(changed, [&"count"], "successful commit must emit data_changed")
	_assert_false(service.is_dirty(&"main"), "successful commit must not leave dirty")

	var failing_store := RenameFailureStore.new()
	failing_store.promote_failures_remaining = 1
	var failing := QuickDatabaseService.new()
	failing.open_slot(&"main", ProbeDocument, _new_path("commit-failure"), failing_store)
	var failure_changed: Array[StringName] = []
	failing.data_changed.connect(
		func(_slot: StringName, field: StringName) -> void: failure_changed.append(field)
	)
	var errors: Array[Error] = []
	failing.save_failed.connect(
		func(_slot: StringName, _path: String, error: Error) -> void: errors.append(error)
	)
	_assert_true(
		failing.commit_slot(
			&"main",
			&"count",
			func(document: QuickDatabaseDocument) -> void: document.set("count", 9)
		) != OK,
		"failed commit must surface the repository error"
	)
	_assert_equal(
		int(failing.get_document(&"main").get("count")),
		0,
		"failed commit must not mutate the live document"
	)
	_assert_true(failure_changed.is_empty(), "failed commit must not emit data_changed")
	_assert_equal(errors.size(), 1, "failed commit must emit save_failed once")
	service.free()
	failing.free()


func _test_reload_and_erase() -> void:
	var path := _new_path("reload")
	var service := QuickDatabaseService.new()
	service.open_slot(&"main", ProbeDocument, path)
	service.update_slot(
		&"main",
		&"count",
		func(document: QuickDatabaseDocument) -> void: document.set("count", 4)
	)
	service.flush_slot(&"main")
	service.update_slot(
		&"main",
		&"count",
		func(document: QuickDatabaseDocument) -> void: document.set("count", 8)
	)
	_assert_true(service.reload_slot(&"main"), "reload_slot must succeed")
	_assert_equal(
		int(service.get_document(&"main").get("count")),
		4,
		"reload must discard unsaved changes"
	)
	_assert_false(service.is_dirty(&"main"), "reload must clear dirty")

	_assert_equal(service.erase_slot(&"main"), OK, "erase_slot must succeed")
	_assert_false(FileAccess.file_exists(path), "erase_slot must remove the file")
	_assert_equal(
		int(service.get_document(&"main").get("count")),
		0,
		"erase must reset to defaults"
	)
	service.free()


func _test_close_flushes_dirty_slot() -> void:
	var path := _new_path("close")
	var service := QuickDatabaseService.new()
	service.open_slot(&"main", ProbeDocument, path)
	service.update_slot(
		&"main",
		&"count",
		func(document: QuickDatabaseDocument) -> void: document.set("count", 6)
	)
	_assert_true(service.close_slot(&"main"), "close_slot must succeed")
	_assert_false(service.has_slot(&"main"), "closed slot must be removed")
	_assert_true(FileAccess.file_exists(path), "close_slot must flush dirty data first")
	service.free()


func _new_path(label: String) -> String:
	var path := "user://quick_database_service_%s_%d.cfg" % [
		label, Time.get_ticks_usec()
	]
	_owned_paths.append(ProjectSettings.globalize_path(path))
	return path


func _cleanup_owned_paths() -> void:
	for path: String in _owned_paths:
		if FileAccess.file_exists(path):
			DirAccess.remove_absolute(path)
	for suffix: String in [".previous"]:
		for path: String in _owned_paths:
			var candidate := "%s%s" % [path, suffix]
			if FileAccess.file_exists(candidate):
				DirAccess.remove_absolute(candidate)


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
