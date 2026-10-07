@tool
extends EditorPlugin

const AUTOLOAD_NAME := "QuickDatabase"
const AUTOLOAD_PATH := "res://addons/quick_database/gds/runtime/quick_database_service.gd"

var _registered := false


func _enter_tree() -> void:
	if ProjectSettings.has_setting("autoload/%s" % AUTOLOAD_NAME):
		if _owns_autoload():
			_registered = true
			return
		push_warning(
			"Quick Database: autoload name '%s' is already in use; plugin will not override it."
			% AUTOLOAD_NAME
		)
		return
	add_autoload_singleton(AUTOLOAD_NAME, AUTOLOAD_PATH)
	_registered = true


func _exit_tree() -> void:
	if _registered and ProjectSettings.has_setting("autoload/%s" % AUTOLOAD_NAME) and _owns_autoload():
		remove_autoload_singleton(AUTOLOAD_NAME)
		_registered = false


func _owns_autoload() -> bool:
	var path := String(ProjectSettings.get_setting("autoload/%s" % AUTOLOAD_NAME, "")).trim_prefix("*")
	if path.begins_with("uid://"):
		var id := ResourceUID.text_to_id(path)
		if ResourceUID.has_id(id):
			path = ResourceUID.get_id_path(id)
	return path == AUTOLOAD_PATH
