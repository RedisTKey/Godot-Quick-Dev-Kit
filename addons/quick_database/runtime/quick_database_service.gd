class_name QuickDatabaseService
extends Node

signal database_loaded(slot: StringName, status: int)
signal data_changed(slot: StringName, field: StringName)
signal save_succeeded(slot: StringName, path: String)
signal save_failed(slot: StringName, path: String, error: Error)
signal corrupt_database_backed_up(slot: StringName, path: String)
signal database_recovery_blocked(slot: StringName, path: String, error: Error)
signal database_transaction_recovery_blocked(
	slot: StringName, path: String, error: Error
)
signal slot_opened(slot: StringName)
signal slot_closed(slot: StringName)


class Slot:
	var slot_name: StringName
	var repository: QuickDatabaseRepository
	var document: QuickDatabaseDocument
	var dirty := false


var _slots: Dictionary = {}


func open_slot(
	slot: StringName,
	document_script: GDScript,
	path: String = "",
	store: QuickDatabaseStore = null
) -> bool:
	if slot.is_empty() or document_script == null or _slots.has(slot):
		return false
	var repository := QuickDatabaseRepository.new(document_script, path, store)
	var entry := Slot.new()
	entry.slot_name = slot
	entry.repository = repository
	entry.document = repository.load_document()
	entry.dirty = false
	_slots[slot] = entry
	_emit_load_signals(slot, repository)
	slot_opened.emit(slot)
	return true


func close_slot(slot: StringName) -> bool:
	var entry: Slot = _slots.get(slot)
	if entry == null:
		return false
	if entry.dirty and _save_entry(entry) != OK:
		return false
	_slots.erase(slot)
	slot_closed.emit(slot)
	return true


func has_slot(slot: StringName) -> bool:
	return _slots.has(slot)


func get_slot_names() -> Array[StringName]:
	var names: Array[StringName] = []
	for slot: StringName in _slots:
		names.append(slot)
	return names


func get_save_path(slot: StringName) -> String:
	var entry: Slot = _slots.get(slot)
	return entry.repository.get_save_path() if entry != null else ""


func is_dirty(slot: StringName) -> bool:
	var entry: Slot = _slots.get(slot)
	return entry.dirty if entry != null else false


func get_document(slot: StringName) -> QuickDatabaseDocument:
	var entry: Slot = _slots.get(slot)
	return entry.document.duplicate_document() if entry != null else null


func update_slot(slot: StringName, field: StringName, mutator: Callable) -> bool:
	var entry: Slot = _slots.get(slot)
	if entry == null:
		return false
	mutator.call(entry.document)
	entry.dirty = true
	data_changed.emit(slot, field)
	return true


func commit_slot(slot: StringName, field: StringName, mutator: Callable) -> Error:
	var entry: Slot = _slots.get(slot)
	if entry == null:
		return ERR_DOES_NOT_EXIST
	var candidate := entry.document.duplicate_document()
	mutator.call(candidate)
	var error := entry.repository.save_document(candidate)
	if error != OK:
		save_failed.emit(slot, entry.repository.get_save_path(), error)
		return error
	entry.document = candidate
	entry.dirty = false
	data_changed.emit(slot, field)
	save_succeeded.emit(slot, entry.repository.get_save_path())
	return OK


func flush_slot(slot: StringName) -> Error:
	var entry: Slot = _slots.get(slot)
	if entry == null:
		return ERR_DOES_NOT_EXIST
	return _save_entry(entry)


func flush_all() -> Error:
	for slot: StringName in _slots:
		var entry: Slot = _slots[slot]
		if not entry.dirty:
			continue
		var error := _save_entry(entry)
		if error != OK:
			return error
	return OK


func reload_slot(slot: StringName) -> bool:
	var entry: Slot = _slots.get(slot)
	if entry == null:
		return false
	entry.document = entry.repository.load_document()
	entry.dirty = false
	_emit_load_signals(slot, entry.repository)
	return true


func erase_slot(slot: StringName) -> Error:
	var entry: Slot = _slots.get(slot)
	if entry == null:
		return ERR_DOES_NOT_EXIST
	var error := entry.repository.get_store().erase_store(
		entry.repository.get_save_path()
	)
	if error != OK:
		return error
	entry.document = entry.repository.load_document()
	entry.dirty = false
	return OK


func mark_dirty(slot: StringName, field: StringName) -> bool:
	var entry: Slot = _slots.get(slot)
	if entry == null:
		return false
	entry.dirty = true
	data_changed.emit(slot, field)
	return true


func _save_entry(entry: Slot) -> Error:
	if not entry.dirty:
		return OK
	var error := entry.repository.save_document(entry.document)
	if error != OK:
		save_failed.emit(entry.slot_name, entry.repository.get_save_path(), error)
		return error
	entry.dirty = false
	save_succeeded.emit(entry.slot_name, entry.repository.get_save_path())
	return OK


func _emit_load_signals(
	slot: StringName,
	repository: QuickDatabaseRepository
) -> void:
	var store := repository.get_store()
	var backup_path := store.get_last_backup_path()
	if not backup_path.is_empty():
		corrupt_database_backed_up.emit(slot, backup_path)
	if store.is_recovery_pending():
		database_recovery_blocked.emit(
			slot,
			repository.get_save_path(),
			store.get_last_recovery_error()
		)
	if store.is_transaction_recovery_pending():
		database_transaction_recovery_blocked.emit(
			slot,
			repository.get_save_path(),
			store.get_last_transaction_recovery_error()
		)
	database_loaded.emit(slot, store.get_last_load_status())


func _exit_tree() -> void:
	flush_all()
