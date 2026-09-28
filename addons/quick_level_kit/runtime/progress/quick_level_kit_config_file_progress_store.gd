class_name QuickLevelKitConfigFileProgressStore
extends QuickLevelKitProgressStore

const DEFAULT_PATH := "user://quick_level_kit_progress.cfg"
const SECTION := "quick_level_kit"

var save_path: String


func _init(path: String = DEFAULT_PATH) -> void:
	save_path = path


func load_progress() -> QuickLevelKitProgress:
	var progress := QuickLevelKitProgress.new()
	var config := ConfigFile.new()
	if config.load(save_path) != OK:
		return progress
	var encoded: Variant = config.get_value(SECTION, "progress", {})
	if encoded is Dictionary:
		progress.from_dictionary(encoded as Dictionary)
	return progress


func save_progress(progress: QuickLevelKitProgress) -> Error:
	var config := ConfigFile.new()
	config.set_value(SECTION, "progress", progress.to_dictionary())
	return config.save(save_path)


func clear() -> Error:
	if not FileAccess.file_exists(save_path):
		return OK
	return DirAccess.remove_absolute(ProjectSettings.globalize_path(save_path))
