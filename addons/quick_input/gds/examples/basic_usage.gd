extends VBoxContainer

const ACTION := &"quick_input_example_jump"

var _manager: QuickInputManager
var _added_action := false


func _ready() -> void:
	if not InputMap.has_action(ACTION):
		InputMap.add_action(ACTION)
		var space := InputEventKey.new()
		space.physical_keycode = KEY_SPACE
		InputMap.action_add_event(ACTION, space)
		_added_action = true
	var jump := QuickInputActionDefinition.new()
	jump.action = ACTION
	jump.display_name = "Jump"
	jump.binding_slots = 2
	jump.conflict_group = &"gameplay"
	var input_settings := QuickInputSettings.new()
	input_settings.actions.append(jump)
	_manager = QuickInputManager.new()
	add_child(_manager)
	var result := _manager.configure(input_settings, QuickInputConfigFileStore.new())
	if result != OK:
		push_error("Quick Input example could not configure: %d" % result)
		return
	for index: int in 2:
		var button := QuickInputRebindButton.new()
		button.action = ACTION
		button.slot = index
		add_child(button)
		button.set_manager(_manager)
		button.conflict_detected.connect(func(_preview: QuickInputRebindResult) -> void:
			button.confirm_conflict(QuickInputManager.ConflictPolicy.SWAP)
		)


func _exit_tree() -> void:
	if is_instance_valid(_manager):
		_manager.restore_defaults_in_input_map()
	if _added_action:
		InputMap.erase_action(ACTION)
