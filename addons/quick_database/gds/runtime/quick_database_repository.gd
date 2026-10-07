class_name QuickDatabaseRepository
extends RefCounted

var _document_script: GDScript
var _store: QuickDatabaseStore
var _path: String


func _init(
	document_script: GDScript,
	path: String = "",
	store: QuickDatabaseStore = null
) -> void:
	_document_script = document_script
	_path = path
	_store = store if store != null else QuickDatabaseConfigFileStore.new()


func get_document_script() -> GDScript:
	return _document_script


func get_store() -> QuickDatabaseStore:
	return _store


func get_save_path() -> String:
	return _path


func load_document() -> QuickDatabaseDocument:
	var document: QuickDatabaseDocument = _document_script.new()
	var result := _store.load_store(_path, document.get_schema_version())
	var payload: Variant = result.get("payload", {})
	if payload is Dictionary:
		document.from_dictionary((payload as Dictionary).duplicate(true))
	return document


func save_document(document: QuickDatabaseDocument) -> Error:
	if document == null:
		return ERR_INVALID_DATA
	return _store.save_store(
		_path,
		document.get_schema_version(),
		document.to_dictionary()
	)


func get_last_load_status() -> int:
	return _store.get_last_load_status()


func get_last_backup_path() -> String:
	return _store.get_last_backup_path()


func is_recovery_pending() -> bool:
	return _store.is_recovery_pending()


func get_last_recovery_error() -> Error:
	return _store.get_last_recovery_error()


func is_transaction_recovery_pending() -> bool:
	return _store.is_transaction_recovery_pending()


func get_last_transaction_recovery_error() -> Error:
	return _store.get_last_transaction_recovery_error()
