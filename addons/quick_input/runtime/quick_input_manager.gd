class_name QuickInputManager
extends Node

enum ConflictPolicy { ALLOW, REJECT, REPLACE, SWAP }

signal binding_changed(action: StringName, slot: int)
signal bindings_save_failed(error: Error)

var settings: QuickInputSettings
var store: QuickInputStore
var input_map_adapter: QuickInputMapAdapter = QuickInputMapAdapter.new()

var _codec := QuickInputEventCodec.new()
var _definitions: Dictionary = {}
var _defaults: Dictionary = {}
var _current: Dictionary = {}
var _configured := false


func configure(p_settings: QuickInputSettings, p_store: QuickInputStore = null) -> Error:
	if _configured:
		return ERR_ALREADY_IN_USE
	if p_settings == null or p_settings.actions.is_empty() or input_map_adapter == null:
		return ERR_INVALID_PARAMETER
	var definitions: Dictionary = {}
	var defaults: Dictionary = {}
	for definition: QuickInputActionDefinition in p_settings.actions:
		if definition == null or definition.action.is_empty() or definitions.has(definition.action):
			return ERR_INVALID_DATA
		if definition.binding_slots < 1 or not input_map_adapter.has_action(definition.action):
			return ERR_INVALID_DATA
		if definition.allowed_event_types < 1 or definition.allowed_event_types > 7:
			return ERR_INVALID_DATA
		var events := input_map_adapter.get_events(definition.action)
		if events.size() > definition.binding_slots:
			return ERR_INVALID_DATA
		var slots: Array[InputEvent] = []
		for event: InputEvent in events:
			if not _is_allowed(event, definition, p_settings):
				return ERR_INVALID_DATA
			slots.append(event.duplicate())
		while slots.size() < definition.binding_slots:
			slots.append(null)
		definitions[definition.action] = definition
		defaults[definition.action] = slots
	for reserved: InputEvent in p_settings.reserved_events:
		if _codec.signature(reserved).is_empty():
			return ERR_INVALID_DATA
	var candidate_store := p_store if p_store != null else QuickInputConfigFileStore.new()
	var saved := candidate_store.load_bindings()
	if candidate_store.load_error != OK:
		return candidate_store.load_error
	var restored := _copy_map(defaults)
	for raw_action: Variant in saved:
		if typeof(raw_action) != TYPE_STRING and typeof(raw_action) != TYPE_STRING_NAME:
			return ERR_INVALID_DATA
		var action := StringName(raw_action)
		if not definitions.has(action):
			continue
		var raw_slots: Variant = saved[raw_action]
		if not raw_slots is Array or (raw_slots as Array).size() != (restored[action] as Array).size():
			return ERR_INVALID_DATA
		var slots: Array[InputEvent] = []
		for encoded: Variant in raw_slots:
			if encoded == null:
				slots.append(null)
				continue
			if not encoded is Dictionary:
				return ERR_INVALID_DATA
			var event := _codec.decode(encoded)
			if event == null or not _is_allowed(event, definitions[action], p_settings) or _is_reserved(event, p_settings):
				return ERR_INVALID_DATA
			slots.append(event)
		restored[action] = slots
	settings = p_settings
	store = candidate_store
	_definitions = definitions
	_defaults = defaults
	_current = restored
	_configured = true
	_apply_map(restored)
	return OK


func is_configured() -> bool:
	return _configured


func get_binding(action: StringName, slot: int) -> InputEvent:
	if not _is_valid_slot(action, slot):
		return null
	var event := (_current[action] as Array)[slot] as InputEvent
	return event.duplicate() if event != null else null


func get_binding_text(action: StringName, slot: int) -> String:
	var event := get_binding(action, slot)
	return event.as_text() if event != null else "Unbound"


func preview_rebind(action: StringName, slot: int, event: InputEvent) -> QuickInputRebindResult:
	var result := QuickInputRebindResult.new()
	if not _is_valid_slot(action, slot):
		result.status = QuickInputRebindResult.Status.INVALID
		result.message = "Unknown action or slot"
		return result
	if event != null and (
		not _is_allowed(event, _definitions[action], settings)
		or _is_reserved(event, settings)
	):
		result.status = QuickInputRebindResult.Status.INVALID
		result.message = "Unsupported or reserved event"
		return result
	var transaction := QuickInputBindingTransaction.new()
	transaction.action = action
	transaction.slot = slot
	transaction.before = _flatten(_current)
	transaction.after.append(QuickInputBinding.new(action, slot, event))
	result.transaction = transaction
	if event != null:
		var requested := _codec.signature(event)
		var group: StringName = _definitions[action].conflict_group
		for other_action: StringName in _definitions:
			if _definitions[other_action].conflict_group != group:
				continue
			var slots := _current[other_action] as Array
			for other_slot: int in slots.size():
				if other_action == action and other_slot == slot:
					continue
				var other_event := slots[other_slot] as InputEvent
				if other_event != null and _codec.signature(other_event) == requested:
					result.conflicts.append(QuickInputBinding.new(other_action, other_slot, other_event))
	result.status = (
		QuickInputRebindResult.Status.CONFLICT
		if not result.conflicts.is_empty() else QuickInputRebindResult.Status.READY
	)
	return result


func apply_rebind(
	transaction: QuickInputBindingTransaction,
	policy: ConflictPolicy = ConflictPolicy.REJECT
) -> Error:
	if not _configured or transaction == null or transaction.after.size() != 1:
		return ERR_INVALID_PARAMETER
	if policy < ConflictPolicy.ALLOW or policy > ConflictPolicy.SWAP:
		return ERR_INVALID_PARAMETER
	if not _snapshots_match(transaction.before, _flatten(_current)):
		return ERR_BUSY
	var requested := transaction.after[0]
	if requested.action != transaction.action or requested.slot != transaction.slot:
		return ERR_INVALID_PARAMETER
	var result := preview_rebind(requested.action, requested.slot, requested.event)
	if result.status == QuickInputRebindResult.Status.INVALID:
		return ERR_INVALID_PARAMETER
	if not result.conflicts.is_empty() and policy == ConflictPolicy.REJECT:
		return ERR_ALREADY_IN_USE
	if policy == ConflictPolicy.SWAP and result.conflicts.size() > 1:
		return ERR_INVALID_DATA
	var candidate := _copy_map(_current)
	var displaced := (_current[requested.action] as Array)[requested.slot] as InputEvent
	(candidate[requested.action] as Array)[requested.slot] = _normalize_event(requested.event)
	if policy == ConflictPolicy.REPLACE:
		for conflict: QuickInputBinding in result.conflicts:
			(candidate[conflict.action] as Array)[conflict.slot] = null
	elif policy == ConflictPolicy.SWAP and not result.conflicts.is_empty():
		var conflict := result.conflicts[0]
		(candidate[conflict.action] as Array)[conflict.slot] = _copy_event(displaced)
	return _commit(candidate)


func clear_binding(action: StringName, slot: int) -> Error:
	return rebind(action, slot, null)


func rebind(
	action: StringName, slot: int, event: InputEvent,
	policy: ConflictPolicy = ConflictPolicy.REJECT
) -> Error:
	var result := preview_rebind(action, slot, event)
	if result.transaction == null:
		return ERR_INVALID_PARAMETER
	return apply_rebind(result.transaction, policy)


func reset_action(action: StringName) -> Error:
	if not _configured or not _definitions.has(action):
		return ERR_INVALID_PARAMETER
	var candidate := _copy_map(_current)
	candidate[action] = _copy_slots(_defaults[action])
	return _commit(candidate)


func reset_all() -> Error:
	if not _configured:
		return ERR_UNCONFIGURED
	return _commit(_copy_map(_defaults))


func restore_defaults_in_input_map() -> void:
	if not _configured:
		return
	_apply_map(_defaults)
	_configured = false
	_definitions.clear()
	_defaults.clear()
	_current.clear()
	settings = null
	store = null


func _commit(candidate: Dictionary) -> Error:
	if _snapshots_match(_flatten(candidate), _flatten(_current)):
		return OK
	var overrides: Dictionary = {}
	for action: StringName in _definitions:
		var slots := candidate[action] as Array
		var original := _defaults[action] as Array
		var differs := false
		var encoded: Array = []
		for index: int in slots.size():
			var event := slots[index] as InputEvent
			if _signature(event) != _signature(original[index]):
				differs = true
			encoded.append(_codec.encode(event) if event != null else null)
		if differs:
			overrides[String(action)] = encoded
	var error := store.save_bindings(overrides)
	if error != OK:
		bindings_save_failed.emit(error)
		return error
	var previous := _current
	_current = candidate
	_apply_map(candidate)
	for action: StringName in _definitions:
		var slots := candidate[action] as Array
		var old_slots := previous[action] as Array
		for index: int in slots.size():
			if _signature(slots[index]) != _signature(old_slots[index]):
				binding_changed.emit(action, index)
	return OK


func _apply_map(bindings: Dictionary) -> void:
	for action: StringName in bindings:
		var events: Array[InputEvent] = []
		for event: InputEvent in bindings[action]:
			if event != null:
				events.append(event)
		input_map_adapter.set_events(action, events)


func _is_valid_slot(action: StringName, slot: int) -> bool:
	return _configured and _definitions.has(action) and slot >= 0 and slot < (_current[action] as Array).size()


func _is_reserved(event: InputEvent, input_settings: QuickInputSettings) -> bool:
	var signature := _codec.signature(event)
	for reserved: InputEvent in input_settings.reserved_events:
		if _codec.signature(reserved) == signature:
			return true
	return false


func _is_allowed(
	event: InputEvent,
	definition: QuickInputActionDefinition,
	input_settings: QuickInputSettings
) -> bool:
	var encoded := _codec.encode(event)
	if encoded.is_empty():
		return false
	if event is InputEventWithModifiers and not input_settings.allow_modifier_combinations:
		var modifiers := event as InputEventWithModifiers
		if modifiers.shift_pressed or modifiers.ctrl_pressed or modifiers.alt_pressed or modifiers.meta_pressed:
			return false
	match encoded["type"]:
		"key":
			return (definition.allowed_event_types & QuickInputActionDefinition.KEYBOARD) != 0
		"mouse":
			return (definition.allowed_event_types & QuickInputActionDefinition.MOUSE) != 0
		"joypad_button":
			return (definition.allowed_event_types & QuickInputActionDefinition.GAMEPAD_BUTTON) != 0
	return false


func _signature(event: InputEvent) -> String:
	return _codec.signature(event) if event != null else ""


func _copy_event(event: InputEvent) -> InputEvent:
	return event.duplicate() if event != null else null


func _normalize_event(event: InputEvent) -> InputEvent:
	return _codec.decode(_codec.encode(event)) if event != null else null


func _copy_slots(source: Array) -> Array[InputEvent]:
	var copy: Array[InputEvent] = []
	for event: InputEvent in source:
		copy.append(_copy_event(event))
	return copy


func _copy_map(source: Dictionary) -> Dictionary:
	var copy: Dictionary = {}
	for action: StringName in source:
		copy[action] = _copy_slots(source[action])
	return copy


func _flatten(source: Dictionary) -> Array[QuickInputBinding]:
	var bindings: Array[QuickInputBinding] = []
	for action: StringName in source:
		var slots := source[action] as Array
		for index: int in slots.size():
			bindings.append(QuickInputBinding.new(action, index, slots[index]))
	return bindings


func _snapshots_match(left: Array[QuickInputBinding], right: Array[QuickInputBinding]) -> bool:
	if left.size() != right.size():
		return false
	for index: int in left.size():
		if left[index].action != right[index].action or left[index].slot != right[index].slot:
			return false
		if _signature(left[index].event) != _signature(right[index].event):
			return false
	return true
