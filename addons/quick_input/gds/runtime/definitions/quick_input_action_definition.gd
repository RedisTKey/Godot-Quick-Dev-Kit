class_name QuickInputActionDefinition
extends Resource

const KEYBOARD := 1
const MOUSE := 2
const GAMEPAD_BUTTON := 4

@export var action: StringName
@export var display_name := ""
@export_range(1, 8, 1) var binding_slots := 1
@export var conflict_group: StringName
@export_flags("Keyboard", "Mouse", "Gamepad Button") var allowed_event_types := 7
