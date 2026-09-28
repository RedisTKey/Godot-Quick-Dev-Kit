class_name QuickDatabaseStore
extends RefCounted

enum LoadStatus {
	MISSING,
	LOADED,
	RECOVERED_CORRUPT,
	RECOVERED_UNKNOWN_SCHEMA,
	RECOVERY_BLOCKED_CORRUPT,
	RECOVERY_BLOCKED_UNKNOWN_SCHEMA,
	RECOVERED_INTERRUPTED_TRANSACTION,
	TRANSACTION_RECOVERY_BLOCKED,
}


func load_store(_path: String, _expected_schema_version: int) -> Dictionary:
	return {"payload": {}, "status": LoadStatus.MISSING}


func save_store(
	_path: String,
	_expected_schema_version: int,
	_payload: Dictionary
) -> Error:
	return OK


func erase_store(_path: String) -> Error:
	return OK


func get_last_load_status() -> LoadStatus:
	return LoadStatus.MISSING


func get_last_backup_path() -> String:
	return ""


func is_recovery_pending() -> bool:
	return false


func get_last_recovery_error() -> Error:
	return OK


func is_transaction_recovery_pending() -> bool:
	return false


func get_last_transaction_recovery_error() -> Error:
	return OK
