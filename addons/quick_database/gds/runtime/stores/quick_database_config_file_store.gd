class_name QuickDatabaseConfigFileStore
extends QuickDatabaseStore

const DEFAULT_PATH := "user://quick_database.cfg"
const SECTION_META := "meta"
const SECTION_DATA := "data"
const KEY_SCHEMA_VERSION := "schema_version"
const KEY_PAYLOAD := "payload"

var _last_load_status := LoadStatus.MISSING
var _last_backup_path := ""
var _recovery_pending := false
var _last_recovery_error: Error = OK
var _transaction_recovery_pending := false
var _last_transaction_recovery_error: Error = OK
var _transaction_was_recovered := false


func load_store(path: String, expected_schema_version: int) -> Dictionary:
	var resolved := _resolve_path(path)
	_reset_state()
	_last_transaction_recovery_error = _recover_interrupted_transaction(resolved)
	if _last_transaction_recovery_error != OK:
		_transaction_recovery_pending = true
		if not FileAccess.file_exists(resolved):
			_last_load_status = LoadStatus.TRANSACTION_RECOVERY_BLOCKED
			return _empty_result()
	if not FileAccess.file_exists(resolved):
		_last_load_status = LoadStatus.MISSING
		return _empty_result()

	var config := ConfigFile.new()
	if _load_config(config, resolved) != OK:
		_last_recovery_error = _backup_corrupt_save(resolved)
		_recovery_pending = _last_recovery_error != OK
		_last_load_status = (
			LoadStatus.RECOVERY_BLOCKED_CORRUPT
			if _recovery_pending
			else LoadStatus.RECOVERED_CORRUPT
		)
		return _empty_result()

	var raw_schema: Variant = config.get_value(
		SECTION_META, KEY_SCHEMA_VERSION, null
	)
	if typeof(raw_schema) != TYPE_INT or int(raw_schema) != expected_schema_version:
		_last_recovery_error = _backup_corrupt_save(resolved)
		_recovery_pending = _last_recovery_error != OK
		_last_load_status = (
			LoadStatus.RECOVERY_BLOCKED_UNKNOWN_SCHEMA
			if _recovery_pending
			else LoadStatus.RECOVERED_UNKNOWN_SCHEMA
		)
		return _empty_result()

	_last_load_status = (
		LoadStatus.TRANSACTION_RECOVERY_BLOCKED
		if _transaction_recovery_pending
		else (
			LoadStatus.RECOVERED_INTERRUPTED_TRANSACTION
			if _transaction_was_recovered
			else LoadStatus.LOADED
		)
	)
	var raw_payload: Variant = config.get_value(SECTION_DATA, KEY_PAYLOAD, {})
	var payload: Dictionary = {}
	if raw_payload is Dictionary:
		payload = (raw_payload as Dictionary).duplicate(true)
	return {"payload": payload, "status": _last_load_status}


func save_store(path: String, schema_version: int, payload: Dictionary) -> Error:
	var resolved := _resolve_path(path)
	_last_transaction_recovery_error = _recover_interrupted_transaction(resolved)
	if _last_transaction_recovery_error != OK:
		_transaction_recovery_pending = true
		return _last_transaction_recovery_error
	_transaction_recovery_pending = false
	if _recovery_pending:
		_last_recovery_error = _backup_corrupt_save(resolved)
		if _last_recovery_error != OK:
			return _last_recovery_error
		_recovery_pending = false

	var absolute_path := ProjectSettings.globalize_path(resolved)
	var parent_path := absolute_path.get_base_dir()
	if not DirAccess.dir_exists_absolute(parent_path):
		var directory_error := DirAccess.make_dir_recursive_absolute(parent_path)
		if directory_error != OK:
			return directory_error

	var config := ConfigFile.new()
	config.set_value(SECTION_META, KEY_SCHEMA_VERSION, schema_version)
	config.set_value(SECTION_DATA, KEY_PAYLOAD, payload.duplicate(true))
	return _save_config_transactionally(config, absolute_path)


func erase_store(path: String) -> Error:
	var absolute_path := ProjectSettings.globalize_path(_resolve_path(path))
	var remove_error := _remove_absolute(absolute_path)
	if remove_error != OK:
		return remove_error
	return _remove_absolute(_get_previous_path(absolute_path))


func get_last_load_status() -> LoadStatus:
	return _last_load_status


func get_last_backup_path() -> String:
	return _last_backup_path


func is_recovery_pending() -> bool:
	return _recovery_pending


func get_last_recovery_error() -> Error:
	return _last_recovery_error


func is_transaction_recovery_pending() -> bool:
	return _transaction_recovery_pending


func get_last_transaction_recovery_error() -> Error:
	return _last_transaction_recovery_error


func _resolve_path(path: String) -> String:
	return path if not path.is_empty() else DEFAULT_PATH


func _empty_result() -> Dictionary:
	return {"payload": {}, "status": _last_load_status}


func _reset_state() -> void:
	_last_backup_path = ""
	_recovery_pending = false
	_last_recovery_error = OK
	_transaction_recovery_pending = false
	_last_transaction_recovery_error = OK
	_transaction_was_recovered = false


func _backup_corrupt_save(resolved_path: String) -> Error:
	var source_path := ProjectSettings.globalize_path(resolved_path)
	if not FileAccess.file_exists(source_path):
		return OK

	var datetime := Time.get_datetime_dict_from_system()
	var timestamp := "%04d%02d%02d_%02d%02d%02d_%d" % [
		int(datetime.get("year", 0)),
		int(datetime.get("month", 0)),
		int(datetime.get("day", 0)),
		int(datetime.get("hour", 0)),
		int(datetime.get("minute", 0)),
		int(datetime.get("second", 0)),
		Time.get_ticks_msec(),
	]
	var backup_path := "%s.%s.corrupt.cfg" % [source_path.get_basename(), timestamp]
	var backup_error := _rename_absolute(source_path, backup_path)
	if backup_error == OK:
		_last_backup_path = backup_path
		return OK
	push_error(
		"Could not back up corrupt database %s (error %d)."
		% [source_path, backup_error]
	)
	return backup_error


func _save_config_transactionally(
	config: ConfigFile,
	absolute_path: String
) -> Error:
	var nonce := "%d_%d" % [Time.get_ticks_usec(), randi()]
	var temporary_path := "%s.%s.tmp" % [absolute_path, nonce]
	var previous_path := _get_previous_path(absolute_path)
	var save_error := _save_config(config, temporary_path)
	if save_error != OK:
		return save_error

	var verification_config := ConfigFile.new()
	var verification_error := _load_config(verification_config, temporary_path)
	if verification_error != OK:
		_remove_absolute(temporary_path)
		return verification_error

	if not FileAccess.file_exists(absolute_path):
		var promote_new_error := _rename_absolute(temporary_path, absolute_path)
		if promote_new_error != OK:
			_remove_absolute(temporary_path)
		return promote_new_error

	var preserve_error := _rename_absolute(absolute_path, previous_path)
	if preserve_error != OK:
		_remove_absolute(temporary_path)
		return preserve_error

	var promote_error := _rename_absolute(temporary_path, absolute_path)
	if promote_error != OK:
		var restore_error := _rename_absolute(previous_path, absolute_path)
		_remove_absolute(temporary_path)
		if restore_error != OK:
			_transaction_recovery_pending = true
			_last_transaction_recovery_error = restore_error
			push_error(
				"Could not restore previous database %s after error %d (restore error %d)."
				% [absolute_path, promote_error, restore_error]
			)
			return restore_error
		return promote_error

	var cleanup_error := _remove_absolute(previous_path)
	if cleanup_error != OK:
		push_warning(
			"Saved successfully but could not remove previous database %s (error %d)."
			% [previous_path, cleanup_error]
		)
	return OK


func _recover_interrupted_transaction(resolved_path: String) -> Error:
	var absolute_path := ProjectSettings.globalize_path(resolved_path)
	var previous_path := _get_previous_path(absolute_path)
	if not FileAccess.file_exists(previous_path):
		return OK
	if FileAccess.file_exists(absolute_path):
		var cleanup_error := _remove_absolute(previous_path)
		if cleanup_error != OK:
			push_error(
				"Could not remove completed transaction file %s (error %d)."
				% [previous_path, cleanup_error]
			)
			return cleanup_error
		return OK

	var restore_error := _rename_absolute(previous_path, absolute_path)
	if restore_error != OK:
		push_error(
			"Could not recover interrupted database transaction %s (error %d)."
			% [previous_path, restore_error]
		)
		return restore_error
	_transaction_was_recovered = true
	return OK


func _get_previous_path(absolute_path: String) -> String:
	return "%s.previous" % absolute_path


func _save_config(config: ConfigFile, path: String) -> Error:
	return config.save(path)


func _load_config(config: ConfigFile, path: String) -> Error:
	return config.load(path)


func _rename_absolute(source_path: String, target_path: String) -> Error:
	return DirAccess.rename_absolute(source_path, target_path)


func _remove_absolute(path: String) -> Error:
	if not FileAccess.file_exists(path):
		return OK
	return DirAccess.remove_absolute(path)
