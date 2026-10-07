extends SceneTree


class RenameFailureStore:
	extends QuickDatabaseConfigFileStore

	var corrupt_backup_failures_remaining := 0
	var promote_failures_remaining := 0
	var restore_failures_remaining := 0
	var cleanup_failures_remaining := 0

	func _rename_absolute(source_path: String, target_path: String) -> Error:
		if corrupt_backup_failures_remaining > 0 and target_path.ends_with(".corrupt.cfg"):
			corrupt_backup_failures_remaining -= 1
			return ERR_BUSY
		if promote_failures_remaining > 0 and source_path.ends_with(".tmp"):
			promote_failures_remaining -= 1
			return ERR_CANT_CREATE
		if restore_failures_remaining > 0 and source_path.ends_with(".previous"):
			restore_failures_remaining -= 1
			return ERR_BUSY
		return super._rename_absolute(source_path, target_path)

	func _remove_absolute(path: String) -> Error:
		if cleanup_failures_remaining > 0 and path.ends_with(".previous"):
			cleanup_failures_remaining -= 1
			return ERR_BUSY
		return super._remove_absolute(path)


var _failures := 0
var _owned_paths: Array[String] = []


func _init() -> void:
	call_deferred("_run")


func _run() -> void:
	_test_missing_does_not_write()
	_test_round_trip()
	_test_unknown_schema_is_backed_up()
	_test_corrupt_is_backed_up()
	_test_transaction_failure_preserves_previous()
	_test_interrupted_transaction_recovers()
	_test_blocked_recovery_blocks_overwrite()
	_test_erase()
	_cleanup_owned_paths()
	if _failures == 0:
		print("Quick Database store tests passed.")
	quit(_failures)


func _test_missing_does_not_write() -> void:
	var path := _new_path("missing")
	var store := QuickDatabaseConfigFileStore.new()
	var result := store.load_store(path, 1)
	_assert_equal(
		store.get_last_load_status(),
		QuickDatabaseStore.LoadStatus.MISSING,
		"missing file must report MISSING"
	)
	_assert_equal(result.get("payload"), {}, "missing file must load an empty payload")
	_assert_false(FileAccess.file_exists(path), "loading a missing file must not create it")


func _test_round_trip() -> void:
	var path := _new_path("round-trip")
	var store := QuickDatabaseConfigFileStore.new()
	var payload := {"count": 3, "nested": {"flag": true}}
	_assert_equal(store.save_store(path, 1, payload), OK, "save must succeed")

	var loader := QuickDatabaseConfigFileStore.new()
	var result := loader.load_store(path, 1)
	_assert_equal(
		loader.get_last_load_status(),
		QuickDatabaseStore.LoadStatus.LOADED,
		"valid file must report LOADED"
	)
	_assert_equal(result.get("payload"), payload, "payload must round-trip")


func _test_unknown_schema_is_backed_up() -> void:
	var path := _new_path("unknown-schema")
	var store := QuickDatabaseConfigFileStore.new()
	_assert_equal(store.save_store(path, 999, {"count": 1}), OK, "fixture must save")

	var loader := QuickDatabaseConfigFileStore.new()
	loader.load_store(path, 1)
	_assert_equal(
		loader.get_last_load_status(),
		QuickDatabaseStore.LoadStatus.RECOVERED_UNKNOWN_SCHEMA,
		"unknown schema must be recovered"
	)
	_assert_false(FileAccess.file_exists(path), "unknown schema source must be moved away")
	_assert_backup_exists(loader.get_last_backup_path())


func _test_corrupt_is_backed_up() -> void:
	var path := _new_path("corrupt")
	var file := FileAccess.open(path, FileAccess.WRITE)
	file.store_string("[this is not valid")
	file.close()

	var store := QuickDatabaseConfigFileStore.new()
	store.load_store(path, 1)
	_assert_equal(
		store.get_last_load_status(),
		QuickDatabaseStore.LoadStatus.RECOVERED_CORRUPT,
		"malformed file must be recovered"
	)
	_assert_false(FileAccess.file_exists(path), "malformed source must be moved away")
	_assert_backup_exists(store.get_last_backup_path())


func _test_transaction_failure_preserves_previous() -> void:
	var path := _new_path("transaction")
	_assert_equal(
		QuickDatabaseConfigFileStore.new().save_store(path, 1, {"count": 1}),
		OK,
		"initial save must succeed"
	)

	var failing := RenameFailureStore.new()
	failing.promote_failures_remaining = 1
	_assert_true(
		failing.save_store(path, 1, {"count": 2}) != OK,
		"injected promotion failure must surface"
	)
	var preserved := QuickDatabaseConfigFileStore.new().load_store(path, 1)
	_assert_equal(
		preserved.get("payload"),
		{"count": 1},
		"failed replacement must preserve the previous save"
	)


func _test_interrupted_transaction_recovers() -> void:
	var path := _new_path("interrupted")
	var previous_path := "%s.previous" % ProjectSettings.globalize_path(path)
	_owned_paths.append(previous_path)
	_assert_equal(
		QuickDatabaseConfigFileStore.new().save_store(path, 1, {"count": 1}),
		OK,
		"initial save must succeed"
	)

	var failing := RenameFailureStore.new()
	failing.promote_failures_remaining = 1
	failing.restore_failures_remaining = 1
	_assert_equal(
		failing.save_store(path, 1, {"count": 2}),
		ERR_BUSY,
		"double rename failure must surface the restore error"
	)
	_assert_false(
		FileAccess.file_exists(ProjectSettings.globalize_path(path)),
		"interrupted replacement must leave the canonical path absent"
	)
	_assert_true(
		FileAccess.file_exists(previous_path),
		"interrupted replacement must leave a recovery file"
	)

	var recovery := QuickDatabaseConfigFileStore.new()
	var recovered := recovery.load_store(path, 1)
	_assert_equal(
		recovery.get_last_load_status(),
		QuickDatabaseStore.LoadStatus.RECOVERED_INTERRUPTED_TRANSACTION,
		"next load must report interrupted transaction recovery"
	)
	_assert_equal(
		recovered.get("payload"),
		{"count": 1},
		"recovery must restore the previous valid payload"
	)
	_assert_false(
		FileAccess.file_exists(previous_path),
		"recovery must consume the recovery file"
	)


func _test_blocked_recovery_blocks_overwrite() -> void:
	var path := _new_path("blocked-recovery")
	var corrupt_contents := "[still corrupt"
	var file := FileAccess.open(path, FileAccess.WRITE)
	file.store_string(corrupt_contents)
	file.close()

	var store := RenameFailureStore.new()
	store.corrupt_backup_failures_remaining = 2
	store.load_store(path, 1)
	_assert_equal(
		store.get_last_load_status(),
		QuickDatabaseStore.LoadStatus.RECOVERY_BLOCKED_CORRUPT,
		"failed backup must report blocked recovery"
	)
	_assert_true(store.is_recovery_pending(), "blocked recovery must remain pending")
	_assert_true(
		store.save_store(path, 1, {"count": 1}) != OK,
		"save must refuse to overwrite a corrupt file before backup succeeds"
	)
	_assert_equal(
		FileAccess.get_file_as_string(path),
		corrupt_contents,
		"blocked recovery must preserve the corrupt bytes"
	)

	store.corrupt_backup_failures_remaining = 0
	_assert_equal(store.save_store(path, 1, {"count": 1}), OK, "recovery must retry")
	_assert_false(store.is_recovery_pending(), "successful backup must unblock writes")
	_assert_backup_exists(store.get_last_backup_path())


func _test_erase() -> void:
	var path := _new_path("erase")
	var store := QuickDatabaseConfigFileStore.new()
	_assert_equal(store.save_store(path, 1, {"count": 1}), OK, "fixture must save")
	_assert_equal(store.erase_store(path), OK, "erase must succeed")
	_assert_false(FileAccess.file_exists(path), "erase must remove the file")


func _new_path(label: String) -> String:
	var path := "user://quick_database_store_%s_%d.cfg" % [label, Time.get_ticks_usec()]
	_owned_paths.append(ProjectSettings.globalize_path(path))
	return path


func _assert_backup_exists(path: String) -> void:
	_assert_false(path.is_empty(), "recovery must expose a backup path")
	_assert_true(FileAccess.file_exists(path), "recovery backup must exist")
	_assert_true(path.ends_with(".corrupt.cfg"), "backup must use .corrupt.cfg suffix")
	_owned_paths.append(path)


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
