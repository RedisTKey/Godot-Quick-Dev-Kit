@tool
extends EditorPlugin

const AUTOLOAD_NAME := "QuickAudioManager"
const AUTOLOAD_PATH := "res://addons/quick_audio_manager/gds/runtime/quick_audio_manager_service.gd"
const PluginGuard = preload("res://addons/quick_audio_manager/gds/quick_audio_manager_plugin_guard.gd")

var _registration_rejected := false


func _enable_plugin() -> void:
	var setting := "autoload/%s" % AUTOLOAD_NAME
	var current: Variant = ProjectSettings.get_setting(setting, "")
	var enabled := PackedStringArray(ProjectSettings.get_setting("editor_plugins/enabled", PackedStringArray()))
	var reason := PluginGuard.registration_error(ProjectSettings.has_setting(setting), current, enabled)
	_registration_rejected = not reason.is_empty()
	if _registration_rejected:
		push_error("Quick Audio Manager (GDScript): %s" % reason)
		return
	# An existing matching entry is also ours after an editor/script reload.
	if PluginGuard.owns_autoload(current):
		return
	add_autoload_singleton(AUTOLOAD_NAME, AUTOLOAD_PATH)


func _disable_plugin() -> void:
	var current: Variant = ProjectSettings.get_setting("autoload/%s" % AUTOLOAD_NAME, "")
	# A rejected enable must not change pre-existing settings. Otherwise the path,
	# not an in-memory registration flag, establishes ownership after reloads.
	if PluginGuard.should_remove_autoload(current, _registration_rejected):
		remove_autoload_singleton(AUTOLOAD_NAME)
	_registration_rejected = false
