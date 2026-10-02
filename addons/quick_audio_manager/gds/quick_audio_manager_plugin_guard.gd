@tool
extends RefCounted
## Read-only registration decisions shared by the editor entry point and package tests.
## Kept free of editor and project mutations so tests never enable a plugin.

const AUTOLOAD_VALUE := "*res://addons/quick_audio_manager/gds/runtime/quick_audio_manager_service.gd"
const OTHER_PLUGIN := "res://addons/quick_audio_manager/csharp/plugin.cfg"


static func registration_error(
	autoload_exists: bool, autoload_value: Variant, enabled_plugins: PackedStringArray
) -> String:
	if enabled_plugins.has(OTHER_PLUGIN):
		return "Quick Audio Manager (C#) is enabled. Disable it before enabling the GDScript version."
	if autoload_exists and not owns_autoload(autoload_value):
		return ("Autoload 'QuickAudioManager' is already configured with a different path or without "
			+ "singleton access. Its existing value was preserved. Resolve it in Project Settings "
			+ "before enabling this plugin.")
	return ""


static func owns_autoload(autoload_value: Variant) -> bool:
	if not autoload_value is String or not (autoload_value as String).begins_with("*"):
		return false
	var path := (autoload_value as String).trim_prefix("*")
	if path.begins_with("uid://"):
		var uid := ResourceUID.text_to_id(path)
		if not ResourceUID.has_id(uid):
			return false
		path = ResourceUID.get_id_path(uid)
	return path == AUTOLOAD_VALUE.trim_prefix("*")


static func should_remove_autoload(autoload_value: Variant, registration_rejected: bool) -> bool:
	return not registration_rejected and owns_autoload(autoload_value)
