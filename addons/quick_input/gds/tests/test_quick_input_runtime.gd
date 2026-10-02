extends SceneTree

const ACTION_A := &"quick_input_test_a"
const ACTION_B := &"quick_input_test_b"
const ACTION_C := &"quick_input_test_c"


class MemoryStore:
	extends QuickInputStore

	var bindings: Dictionary = {}
	var failure: Error = OK
	var save_calls := 0

	func load_bindings() -> Dictionary:
		load_error = OK
		return bindings.duplicate(true)

	func save_bindings(data: Dictionary) -> Error:
		save_calls += 1
		if failure != OK:
			return failure
		bindings = data.duplicate(true)
		return OK


var _failures := 0
var _save_path := "user://quick_input_gds_test_%d_%d.cfg" % [OS.get_process_id(), Time.get_ticks_usec()]


func _init() -> void:
	call_deferred("_run")


func _run() -> void:
	if not load("res://addons/quick_input/gds/runtime/quick_input_manager.gd") is Script:
		push_error("Quick Input runtime script did not load")
		quit(1)
		return
	if not _setup_actions():
		quit(_failures)
		return
	_test_codec()
	_test_rebinding_and_conflicts()
	_test_swap_destination_validation()
	_test_persistence()
	_cleanup_actions()
	if _failures == 0:
		print("Quick Input runtime tests passed.")
	quit(_failures)


func _setup_actions() -> bool:
	# Never overwrite game actions or a file left by another test process.
	for action: StringName in [ACTION_A, ACTION_B, ACTION_C]:
		if InputMap.has_action(action):
			_assert(false, "Test action already exists; run tests in an isolated project: %s" % action)
			return false
	for suffix: String in ["", ".previous", ".tmp"]:
		if FileAccess.file_exists("%s%s" % [_save_path, suffix]):
			_assert(false, "Refusing to overwrite an existing test save")
			return false
	for item: Array in [[ACTION_A, KEY_A], [ACTION_B, KEY_B], [ACTION_C, KEY_C]]:
		InputMap.add_action(item[0])
		InputMap.action_add_event(item[0], _key(item[1]))
	return true


func _cleanup_actions() -> void:
	for action: StringName in [ACTION_A, ACTION_B, ACTION_C]:
		InputMap.erase_action(action)
	for suffix: String in ["", ".previous", ".tmp"]:
		var path := ProjectSettings.globalize_path("%s%s" % [_save_path, suffix])
		if FileAccess.file_exists(path):
			_assert(DirAccess.remove_absolute(path) == OK, "Test save could not be cleaned up")


func _settings() -> QuickInputSettings:
	var settings := QuickInputSettings.new()
	for action: StringName in [ACTION_A, ACTION_B, ACTION_C]:
		var definition := QuickInputActionDefinition.new()
		definition.action = action
		definition.binding_slots = 2
		definition.conflict_group = &"other" if action == ACTION_C else &"gameplay"
		settings.actions.append(definition)
	settings.reserved_events.append(_key(KEY_ESCAPE))
	return settings


func _key(code: Key) -> InputEventKey:
	var event := InputEventKey.new()
	event.physical_keycode = code
	return event


func _test_codec() -> void:
	var codec := QuickInputEventCodec.new()
	var key := _key(KEY_Z)
	key.shift_pressed = true
	var mouse := InputEventMouseButton.new()
	mouse.button_index = MOUSE_BUTTON_XBUTTON1
	var joy := InputEventJoypadButton.new()
	joy.button_index = JOY_BUTTON_A
	for event: InputEvent in [key, mouse, joy]:
		var encoded := codec.encode(event)
		_assert(not encoded.is_empty(), "Supported event must serialize")
		var decoded := codec.decode(encoded)
		_assert(decoded != null and codec.signature(decoded) == codec.signature(event),
			"Supported event must round-trip")
	_assert(codec.decode({"type": "key", "physical": "bad"}) == null,
		"Corrupt event must be rejected")
	_assert(codec.encode(_key(KEY_UNKNOWN)).is_empty(),
		"Unknown physical key must not serialize as a supported binding")


func _test_rebinding_and_conflicts() -> void:
	var store := MemoryStore.new()
	var manager := QuickInputManager.new()
	_assert(manager.configure(_settings(), store) == OK, "Configure failed")
	_assert(manager.get_binding_text(ACTION_A, 1) == "Unbound", "Second slot should start empty")
	_assert(manager.preview_rebind(ACTION_A, 0, _key(KEY_UNKNOWN)).status == QuickInputRebindResult.Status.INVALID,
		"Unknown physical key preview must be invalid")
	_assert(manager.rebind(ACTION_A, 0, _key(KEY_UNKNOWN)) == ERR_INVALID_PARAMETER,
		"Unknown physical key must not clear an existing binding")
	_assert((manager.get_binding(ACTION_A, 0) as InputEventKey).physical_keycode == KEY_A
		and InputMap.action_get_events(ACTION_A)[0].physical_keycode == KEY_A and store.save_calls == 0,
		"Unknown physical key rejection must preserve the mapping and avoid storage")
	var preview := manager.preview_rebind(ACTION_A, 0, _key(KEY_B))
	_assert(preview.status == QuickInputRebindResult.Status.CONFLICT and preview.conflicts.size() == 1,
		"Same-group binding must show conflict")
	_assert(manager.apply_rebind(preview.transaction) == ERR_ALREADY_IN_USE,
		"Reject policy must keep existing input")
	_assert(manager.apply_rebind(preview.transaction, QuickInputManager.ConflictPolicy.SWAP) == OK,
		"Swap policy failed")
	_assert((manager.get_binding(ACTION_A, 0) as InputEventKey).physical_keycode == KEY_B,
		"Swap must assign requested key")
	_assert((manager.get_binding(ACTION_B, 0) as InputEventKey).physical_keycode == KEY_A,
		"Swap must move displaced key")
	_assert(InputMap.action_get_events(ACTION_A)[0].physical_keycode == KEY_B,
		"Runtime InputMap must reflect changes")
	_assert(manager.apply_rebind(preview.transaction) == ERR_BUSY, "Stale preview must be rejected")
	_assert(manager.preview_rebind(ACTION_A, 1, _key(KEY_C)).conflicts.is_empty(),
		"Different conflict groups must be independent")
	_assert(manager.rebind(ACTION_A, 1, _key(KEY_ESCAPE)) == ERR_INVALID_PARAMETER,
		"Reserved event must be rejected")
	var modified := _key(KEY_F1)
	modified.ctrl_pressed = true
	_assert(manager.rebind(ACTION_A, 1, modified) == ERR_INVALID_PARAMETER,
		"Modifier combinations must obey settings")
	var mouse := InputEventMouseButton.new()
	mouse.button_index = MOUSE_BUTTON_RIGHT
	_assert(manager.rebind(ACTION_A, 1, mouse) == OK, "Mouse secondary slot failed")
	var joy := InputEventJoypadButton.new()
	joy.button_index = JOY_BUTTON_A
	joy.device = 3
	_assert(manager.rebind(ACTION_B, 1, joy) == OK, "Gamepad button slot failed")
	_assert(manager.get_binding(ACTION_B, 1).device == -1,
		"Captured gamepad input must work across controllers")
	_assert(store.bindings.has(String(ACTION_A)), "Custom slots must reach store")
	var collision := manager.preview_rebind(ACTION_B, 1, mouse)
	_assert(collision.conflicts.size() == 1 and collision.conflicts[0].action == ACTION_A,
		"Mouse collision must identify the existing slot")
	_assert(manager.apply_rebind(collision.transaction, QuickInputManager.ConflictPolicy.REPLACE) == OK,
		"Replace policy failed")
	_assert(manager.get_binding(ACTION_A, 1) == null,
		"Replace must clear the occupied slot")
	var saved_events := InputMap.action_get_events(ACTION_B)
	store.failure = ERR_CANT_CREATE
	_assert(manager.rebind(ACTION_B, 1, joy, QuickInputManager.ConflictPolicy.ALLOW) == ERR_CANT_CREATE,
		"Storage failure must be propagated")
	_assert(InputMap.action_get_events(ACTION_B).size() == saved_events.size(),
		"Storage failure must not change InputMap")
	_assert(manager.get_binding(ACTION_B, 1) is InputEventMouseButton,
		"Storage failure must preserve in-memory binding")
	store.failure = OK
	_assert(manager.clear_binding(ACTION_A, 1) == OK, "Slot clearing failed")
	_assert(manager.get_binding(ACTION_A, 1) == null, "Cleared slot must be unbound")
	_assert(manager.reset_action(ACTION_A) == OK, "Action reset failed")
	_assert((manager.get_binding(ACTION_A, 0) as InputEventKey).physical_keycode == KEY_A,
		"Action reset must restore default")
	_assert(manager.reset_all() == OK and store.bindings.is_empty(),
		"Reset all must remove overrides")
	_assert(manager.rebind(ACTION_A, 1, _key(KEY_B), QuickInputManager.ConflictPolicy.ALLOW) == OK,
		"Allow policy must permit duplicate keys")
	_assert(manager.get_binding(ACTION_B, 0) != null,
		"Allow policy must preserve occupied slots")
	_assert(manager.reset_all() == OK, "Defaults must be recoverable after duplicate bindings")
	var button := QuickInputRebindButton.new()
	button.action = ACTION_A
	button.slot = 1
	root.add_child(button)
	button.set_manager(manager)
	button.begin_capture()
	var captured := _key(KEY_F5)
	captured.pressed = true
	button._input(captured)
	_assert((manager.get_binding(ACTION_A, 1) as InputEventKey).physical_keycode == KEY_F5,
		"Reusable button must capture keyboard input")
	button.begin_capture()
	var captured_mouse := InputEventMouseButton.new()
	captured_mouse.button_index = MOUSE_BUTTON_RIGHT
	captured_mouse.pressed = true
	button._input(captured_mouse)
	_assert(manager.get_binding(ACTION_A, 1) is InputEventMouseButton,
		"Reusable button must capture mouse input")
	button.begin_capture()
	var captured_joy := InputEventJoypadButton.new()
	captured_joy.button_index = JOY_BUTTON_A
	captured_joy.pressed = true
	button._input(captured_joy)
	_assert(manager.get_binding(ACTION_A, 1) is InputEventJoypadButton,
		"Reusable button must capture gamepad input")
	button.free()
	manager.restore_defaults_in_input_map()
	manager.free()


func _test_swap_destination_validation() -> void:
	var suffix := "%d_%d" % [OS.get_process_id(), Time.get_ticks_usec()]
	var source_action := StringName("quick_input_swap_source_%s" % suffix)
	var target_action := StringName("quick_input_swap_target_%s" % suffix)
	if InputMap.has_action(source_action) or InputMap.has_action(target_action):
		_assert(false, "Swap regression actions already exist; refusing to overwrite them")
		return
	var mouse := InputEventMouseButton.new()
	mouse.button_index = MOUSE_BUTTON_LEFT
	InputMap.add_action(source_action)
	InputMap.action_add_event(source_action, mouse)
	InputMap.add_action(target_action)
	InputMap.action_add_event(target_action, _key(KEY_SPACE))
	var source_definition := QuickInputActionDefinition.new()
	source_definition.action = source_action
	source_definition.allowed_event_types = QuickInputActionDefinition.KEYBOARD | QuickInputActionDefinition.MOUSE
	var target_definition := QuickInputActionDefinition.new()
	target_definition.action = target_action
	target_definition.allowed_event_types = QuickInputActionDefinition.KEYBOARD
	var settings := QuickInputSettings.new()
	settings.actions.append(source_definition)
	settings.actions.append(target_definition)
	var store := MemoryStore.new()
	var manager := QuickInputManager.new()
	var configured := manager.configure(settings, store)
	_assert(configured == OK, "Swap regression fixture must configure")
	if configured == OK:
		var codec := QuickInputEventCodec.new()
		var original_source := codec.signature(InputMap.action_get_events(source_action)[0])
		var original_target := codec.signature(InputMap.action_get_events(target_action)[0])
		var changed: Array = []
		manager.binding_changed.connect(func(action: StringName, slot: int) -> void: changed.append([action, slot]))
		_assert(manager.rebind(source_action, 0, _key(KEY_SPACE), QuickInputManager.ConflictPolicy.SWAP) == ERR_INVALID_PARAMETER,
			"Swap must reject a mouse displaced into a keyboard-only action")
		_assert(store.save_calls == 0 and changed.is_empty(),
			"Rejected swap must not save or emit binding changes")
		_assert(codec.signature(manager.get_binding(source_action, 0)) == original_source
			and codec.signature(manager.get_binding(target_action, 0)) == original_target,
			"Rejected swap must preserve both in-memory bindings")
		_assert(codec.signature(InputMap.action_get_events(source_action)[0]) == original_source
			and codec.signature(InputMap.action_get_events(target_action)[0]) == original_target,
			"Rejected swap must preserve both InputMap actions")
		# The definitions and settings are live resources. Permit mouse events on the
		# destination, then reserve the displaced default to isolate the other guard.
		target_definition.allowed_event_types |= QuickInputActionDefinition.MOUSE
		settings.reserved_events.append(mouse)
		_assert(manager.rebind(source_action, 0, _key(KEY_SPACE), QuickInputManager.ConflictPolicy.SWAP) == ERR_INVALID_PARAMETER,
			"Swap must reject a reserved displaced event")
		_assert(store.save_calls == 0 and changed.is_empty(),
			"Reserved-event rejection must not save or emit binding changes")
		_assert(codec.signature(manager.get_binding(source_action, 0)) == original_source
			and codec.signature(manager.get_binding(target_action, 0)) == original_target
			and codec.signature(InputMap.action_get_events(source_action)[0]) == original_source
			and codec.signature(InputMap.action_get_events(target_action)[0]) == original_target,
			"Reserved-event rejection must preserve all bindings")
		_assert(manager.clear_binding(source_action, 0) == OK,
			"Source slot must be clearable before testing an empty-slot swap")
		_assert(manager.rebind(source_action, 0, _key(KEY_SPACE), QuickInputManager.ConflictPolicy.SWAP) == OK,
			"Swap with no displaced event must remain valid")
		_assert(manager.get_binding(target_action, 0) == null,
			"Empty-slot swap must clear the occupied destination")
	manager.restore_defaults_in_input_map()
	manager.free()
	InputMap.erase_action(source_action)
	InputMap.erase_action(target_action)


func _test_persistence() -> void:
	var store := QuickInputConfigFileStore.new(_save_path)
	var manager := QuickInputManager.new()
	_assert(manager.configure(_settings(), store) == OK, "File store must start empty")
	var mouse := InputEventMouseButton.new()
	mouse.button_index = MOUSE_BUTTON_LEFT
	_assert(manager.rebind(ACTION_A, 1, mouse) == OK, "File store must save binding")
	manager.restore_defaults_in_input_map()
	manager.free()
	var restored := QuickInputManager.new()
	_assert(restored.configure(_settings(), QuickInputConfigFileStore.new(_save_path)) == OK,
		"File store must reload binding")
	_assert(restored.get_binding(ACTION_A, 1) is InputEventMouseButton,
		"Saved mouse event must be restored")
	restored.restore_defaults_in_input_map()
	restored.free()
	var original := ProjectSettings.globalize_path(_save_path)
	_assert(DirAccess.rename_absolute(original, "%s.previous" % original) == OK,
		"Interrupted-save fixture must move the save aside")
	var recovered := QuickInputManager.new()
	_assert(recovered.configure(_settings(), QuickInputConfigFileStore.new(_save_path)) == OK,
		"Interrupted save must recover from the previous file")
	_assert(recovered.get_binding(ACTION_A, 1) is InputEventMouseButton,
		"Recovered save must retain the binding")
	recovered.restore_defaults_in_input_map()
	recovered.free()
	var corrupt := ConfigFile.new()
	corrupt.set_value("meta", "version", 1)
	corrupt.set_value("input", "bindings", {String(ACTION_A): [{"type": "key", "physical": "bad"}, null]})
	_assert(corrupt.save(_save_path) == OK, "Corrupt fixture must be writable")
	var rejected := QuickInputManager.new()
	_assert(rejected.configure(_settings(), QuickInputConfigFileStore.new(_save_path)) == ERR_INVALID_DATA,
		"Invalid saved events must fail without applying bindings")
	_assert(not rejected.is_configured() and InputMap.action_get_events(ACTION_A).size() == 1,
		"Invalid saved events must leave InputMap untouched")
	rejected.free()


func _assert(condition: bool, message: String) -> void:
	if not condition:
		_failures += 1
		push_error(message)
