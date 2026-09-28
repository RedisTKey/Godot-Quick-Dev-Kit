@tool
extends EditorPlugin

const AUTOLOAD_NAME := "QuickLevelKit"
const AUTOLOAD_PATH := "res://addons/quick_level_kit/runtime/quick_level_kit_manager.gd"
const DOCK_SCRIPT := preload("res://addons/quick_level_kit/editor/quick_level_kit_dock.gd")

var _dock: Control


func _enter_tree() -> void:
	add_autoload_singleton(AUTOLOAD_NAME, AUTOLOAD_PATH)
	_dock = DOCK_SCRIPT.new()
	add_control_to_dock(DOCK_SLOT_RIGHT_UL, _dock)


func _exit_tree() -> void:
	if _dock != null:
		remove_control_from_docks(_dock)
		_dock.queue_free()
		_dock = null
	remove_autoload_singleton(AUTOLOAD_NAME)
