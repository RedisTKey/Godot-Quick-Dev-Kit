extends SceneTree

const ACTION_A := &"quick_input_test_a"
const ACTION_B := &"quick_input_test_b"
const ACTION_C := &"quick_input_test_c"
const SAVE_PATH := "user://quick_input_test_bindings.cfg"


class MemoryStore:
	extends QuickInputStore

	var bindings: Dictionary = {}
	var failure: Error = OK

	func load_bindings() -> Dictionary:
		load_error = OK
		return bindings.duplicate(true)

	func save_bindings(data: Dictionary) -> Error:
		if failure != OK:
			return failure
		bindings = data.duplicate(true)
		return OK


var _failures := 0


func _init() -> void:
	call_deferred("_run")


func _run() -> void:
	if not load("res://addons/quick_input/runtime/quick_input_manager.gd") is Script:
		push_error("Quick Input runtime script did not load")
		quit(1)
		return
	_setup_actions()
	_test_codec()
	_test_rebinding_and_conflicts()
	_test_persistence()
	_cleanup_actions()
	if _failures == 0:
		print("Quick Input runtime tests passed.")
	quit(_failures)


func _setup_actions() -> void:
	for item: Array in [[ACTION_A, KEY_A], [ACTION_B, KEY_B], [ACTION_C, KEY_C]]:
		InputMap.add_action(item[0])
		InputMap.action_add_event(item[0], _key(item[1]))


func _cleanup_actions() -> void:
	for action: StringName in [ACTION_A, ACTION_B, ACTION_C]:
		InputMap.erase_action(action)
	for suffix: String in ["", ".previous", ".tmp"]:
		var path := ProjectSettings.globalize_path("%s%s" % [SAVE_PATH, suffix])
		if FileAccess.file_exists(path):
			DirAccess.remove_absolute(path)


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


func _test_rebinding_and_conflicts() -> void:
	var store := MemoryStore.new()
	var manager := QuickInputManager.new()
	_assert(manager.configure(_settings(), store) == OK, "Configure failed")
	_assert(manager.get_binding_text(ACTION_A, 1) == "Unbound", "Second slot should start empty")
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


func _test_persistence() -> void:
	var store := QuickInputConfigFileStore.new(SAVE_PATH)
	var manager := QuickInputManager.new()
	_assert(manager.configure(_settings(), store) == OK, "File store must start empty")
	var mouse := InputEventMouseButton.new()
	mouse.button_index = MOUSE_BUTTON_LEFT
	_assert(manager.rebind(ACTION_A, 1, mouse) == OK, "File store must save binding")
	manager.restore_defaults_in_input_map()
	manager.free()
	var restored := QuickInputManager.new()
	_assert(restored.configure(_settings(), QuickInputConfigFileStore.new(SAVE_PATH)) == OK,
		"File store must reload binding")
	_assert(restored.get_binding(ACTION_A, 1) is InputEventMouseButton,
		"Saved mouse event must be restored")
	restored.restore_defaults_in_input_map()
	restored.free()
	var original := ProjectSettings.globalize_path(SAVE_PATH)
	_assert(DirAccess.rename_absolute(original, "%s.previous" % original) == OK,
		"Interrupted-save fixture must move the save aside")
	var recovered := QuickInputManager.new()
	_assert(recovered.configure(_settings(), QuickInputConfigFileStore.new(SAVE_PATH)) == OK,
		"Interrupted save must recover from the previous file")
	_assert(recovered.get_binding(ACTION_A, 1) is InputEventMouseButton,
		"Recovered save must retain the binding")
	recovered.restore_defaults_in_input_map()
	recovered.free()
	var corrupt := ConfigFile.new()
	corrupt.set_value("meta", "version", 1)
	corrupt.set_value("input", "bindings", {String(ACTION_A): [{"type": "key", "physical": "bad"}, null]})
	_assert(corrupt.save(SAVE_PATH) == OK, "Corrupt fixture must be writable")
	var rejected := QuickInputManager.new()
	_assert(rejected.configure(_settings(), QuickInputConfigFileStore.new(SAVE_PATH)) == ERR_INVALID_DATA,
		"Invalid saved events must fail without applying bindings")
	_assert(not rejected.is_configured() and InputMap.action_get_events(ACTION_A).size() == 1,
		"Invalid saved events must leave InputMap untouched")
	rejected.free()


func _assert(condition: bool, message: String) -> void:
	if not condition:
		_failures += 1
		push_error(message)
