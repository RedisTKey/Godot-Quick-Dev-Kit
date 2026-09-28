@tool
extends EditorPlugin

const AUTOLOAD_NAME := "QuickInput"
const AUTOLOAD_PATH := "res://addons/quick_input/runtime/quick_input_manager.gd"

var _registered := false


func _enable_plugin() -> void:
	if ProjectSettings.has_setting("autoload/%s" % AUTOLOAD_NAME):
		push_error("Quick Input: Autoload '%s' is already in use." % AUTOLOAD_NAME)
		return
	add_autoload_singleton(AUTOLOAD_NAME, AUTOLOAD_PATH)
	_registered = true


func _disable_plugin() -> void:
	if not _registered:
		return
	if ProjectSettings.get_setting("autoload/%s" % AUTOLOAD_NAME, "") == "*%s" % AUTOLOAD_PATH:
		remove_autoload_singleton(AUTOLOAD_NAME)
	_registered = false
