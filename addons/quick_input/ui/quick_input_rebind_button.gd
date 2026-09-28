class_name QuickInputRebindButton
extends Button

signal conflict_detected(result: QuickInputRebindResult)
signal capture_failed(error: Error)
signal capture_cancelled

@export var action: StringName
@export_range(0, 7, 1) var slot := 0
@export var conflict_policy := QuickInputManager.ConflictPolicy.REJECT

var _manager: QuickInputManager
var _capturing := false
var _pending: QuickInputBindingTransaction


func _ready() -> void:
	process_mode = Node.PROCESS_MODE_ALWAYS
	pressed.connect(begin_capture)
	if _manager == null:
		var autoload := get_node_or_null("/root/QuickInput")
		if autoload is QuickInputManager:
			set_manager(autoload)


func _exit_tree() -> void:
	if is_instance_valid(_manager) and _manager.binding_changed.is_connected(_on_binding_changed):
		_manager.binding_changed.disconnect(_on_binding_changed)


func set_manager(manager: QuickInputManager) -> void:
	if is_instance_valid(_manager) and _manager.binding_changed.is_connected(_on_binding_changed):
		_manager.binding_changed.disconnect(_on_binding_changed)
	_manager = manager
	if is_instance_valid(_manager):
		_manager.binding_changed.connect(_on_binding_changed)
	refresh_text()


func refresh_text() -> void:
	if is_instance_valid(_manager) and _manager.is_configured():
		text = _manager.get_binding_text(action, slot)


func begin_capture() -> void:
	if not is_instance_valid(_manager) or not _manager.is_configured():
		capture_failed.emit(ERR_UNCONFIGURED)
		return
	_capturing = true
	_pending = null
	text = "Press a key or button…"


func cancel_capture() -> void:
	if not _capturing and _pending == null:
		return
	_capturing = false
	_pending = null
	refresh_text()
	capture_cancelled.emit()


func confirm_conflict(policy: QuickInputManager.ConflictPolicy) -> Error:
	if _pending == null or not is_instance_valid(_manager):
		return ERR_INVALID_PARAMETER
	var result := _manager.apply_rebind(_pending, policy)
	if result == OK:
		_capturing = false
		_pending = null
		refresh_text()
	else:
		capture_failed.emit(result)
	return result


func _input(event: InputEvent) -> void:
	if not _capturing:
		return
	if event is InputEventKey:
		var key := event as InputEventKey
		if not key.pressed or key.echo:
			return
		get_viewport().set_input_as_handled()
		if key.keycode == KEY_ESCAPE or key.physical_keycode == KEY_ESCAPE:
			cancel_capture()
			return
	elif event is InputEventMouseButton:
		if not (event as InputEventMouseButton).pressed:
			return
		get_viewport().set_input_as_handled()
	elif event is InputEventJoypadButton:
		if not (event as InputEventJoypadButton).pressed:
			return
		get_viewport().set_input_as_handled()
		if event.is_action_pressed(&"ui_cancel"):
			cancel_capture()
			return
	else:
		return
	var preview := _manager.preview_rebind(action, slot, event)
	if preview.transaction == null:
		capture_failed.emit(ERR_INVALID_PARAMETER)
		return
	if preview.status == QuickInputRebindResult.Status.CONFLICT and conflict_policy == QuickInputManager.ConflictPolicy.REJECT:
		_pending = preview.transaction
		_capturing = false
		text = "Binding conflict"
		conflict_detected.emit(preview)
		return
	var error := _manager.apply_rebind(preview.transaction, conflict_policy)
	if error != OK:
		capture_failed.emit(error)
		return
	_capturing = false
	refresh_text()


func _on_binding_changed(changed_action: StringName, changed_slot: int) -> void:
	if changed_action == action and changed_slot == slot and not _capturing and _pending == null:
		refresh_text()
