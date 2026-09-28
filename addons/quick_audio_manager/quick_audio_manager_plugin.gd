@tool
extends EditorPlugin

const AUTOLOAD_NAME := "QuickAudioManager"
const AUTOLOAD_PATH := "res://addons/quick_audio_manager/runtime/quick_audio_manager_service.gd"
const AUTOLOAD_SETTING := "autoload/%s" % AUTOLOAD_NAME
const AUTOLOAD_VALUE := "*%s" % AUTOLOAD_PATH


func _enable_plugin() -> void:
	if ProjectSettings.has_setting(AUTOLOAD_SETTING):
		var current_path := str(ProjectSettings.get_setting(AUTOLOAD_SETTING, ""))
		if current_path != AUTOLOAD_VALUE:
			push_error(
				"Quick Audio Manager cannot register %s because the name is already in use."
				% AUTOLOAD_NAME
			)
		return
	add_autoload_singleton(AUTOLOAD_NAME, AUTOLOAD_PATH)


func _disable_plugin() -> void:
	if not ProjectSettings.has_setting(AUTOLOAD_SETTING):
		return
	var current_path := str(ProjectSettings.get_setting(AUTOLOAD_SETTING, ""))
	if current_path == AUTOLOAD_VALUE:
		remove_autoload_singleton(AUTOLOAD_NAME)
