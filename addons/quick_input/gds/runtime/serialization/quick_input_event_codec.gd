class_name QuickInputEventCodec
extends RefCounted


func encode(event: InputEvent) -> Dictionary:
	if event is InputEventKey:
		var key := event as InputEventKey
		if (
			(key.physical_keycode == KEY_NONE and key.keycode == KEY_NONE)
			or not _valid_keycode(int(key.physical_keycode))
			or not _valid_keycode(int(key.keycode))
			or int(key.location) < 0 or int(key.location) > 2
		):
			return {}
		return {
			"type": "key", "physical": int(key.physical_keycode),
			"keycode": int(key.keycode), "location": int(key.location),
			"shift": key.shift_pressed, "ctrl": key.ctrl_pressed,
			"alt": key.alt_pressed, "meta": key.meta_pressed,
		}
	if event is InputEventMouseButton:
		var mouse := event as InputEventMouseButton
		if mouse.button_index < 1 or mouse.button_index > 9:
			return {}
		return {
			"type": "mouse", "button": int(mouse.button_index),
			"shift": mouse.shift_pressed, "ctrl": mouse.ctrl_pressed,
			"alt": mouse.alt_pressed, "meta": mouse.meta_pressed,
		}
	if event is InputEventJoypadButton:
		var joypad := event as InputEventJoypadButton
		if joypad.button_index < 0 or joypad.button_index > 127:
			return {}
		return {"type": "joypad_button", "button": joypad.button_index}
	return {}


func decode(data: Dictionary) -> InputEvent:
	var kind: Variant = data.get("type", null)
	if kind == "key":
		if not _has_integers(data, ["physical", "keycode", "location"]) or not _has_modifiers(data):
			return null
		var physical := int(data["physical"])
		var keycode := int(data["keycode"])
		var location := int(data["location"])
		if (
			(physical == 0 and keycode == 0)
			or not _valid_keycode(physical)
			or not _valid_keycode(keycode)
			or location < 0 or location > 2
		):
			return null
		var key := InputEventKey.new()
		key.physical_keycode = physical
		key.keycode = keycode
		key.location = location
		_apply_modifiers(key, data)
		return key
	if kind == "mouse":
		if not _has_integers(data, ["button"]) or not _has_modifiers(data):
			return null
		var index := int(data["button"])
		if index < 1 or index > 9:
			return null
		var mouse := InputEventMouseButton.new()
		mouse.button_index = index
		_apply_modifiers(mouse, data)
		return mouse
	if kind == "joypad_button":
		if not _has_integers(data, ["button"]):
			return null
		var index := int(data["button"])
		if index < 0 or index > 127:
			return null
		var joypad := InputEventJoypadButton.new()
		joypad.button_index = index
		joypad.device = -1
		return joypad
	return null


func signature(event: InputEvent) -> String:
	var data := encode(event)
	if data.is_empty():
		return ""
	if data.get("type") == "key" and int(data["physical"]) != 0:
		data["keycode"] = 0
	return var_to_str(data)


func _has_integers(data: Dictionary, fields: Array[String]) -> bool:
	for field: String in fields:
		if typeof(data.get(field)) != TYPE_INT:
			return false
	return true


func _valid_keycode(code: int) -> bool:
	return code >= 0 and code <= int(KEY_CODE_MASK) and code != int(KEY_UNKNOWN)


func _has_modifiers(data: Dictionary) -> bool:
	for field: String in ["shift", "ctrl", "alt", "meta"]:
		if typeof(data.get(field)) != TYPE_BOOL:
			return false
	return true


func _apply_modifiers(event: InputEventWithModifiers, data: Dictionary) -> void:
	event.shift_pressed = data["shift"]
	event.ctrl_pressed = data["ctrl"]
	event.alt_pressed = data["alt"]
	event.meta_pressed = data["meta"]
