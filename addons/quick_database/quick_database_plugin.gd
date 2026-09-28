@tool
extends EditorPlugin

const AUTOLOAD_NAME := "QuickDatabase"
const AUTOLOAD_PATH := "res://addons/quick_database/runtime/quick_database_service.gd"

var _registered := false


func _enter_tree() -> void:
	if ProjectSettings.has_setting("autoload/%s" % AUTOLOAD_NAME):
		push_error(
			"Quick Database: autoload name '%s' is already in use; plugin will not override it."
			% AUTOLOAD_NAME
		)
		return
	add_autoload_singleton(AUTOLOAD_NAME, AUTOLOAD_PATH)
	_registered = true


func _exit_tree() -> void:
	if _registered:
		remove_autoload_singleton(AUTOLOAD_NAME)
		_registered = false
