class_name QuickInputConfigFileStore
extends QuickInputStore

const DEFAULT_PATH := "user://quick_input_bindings.cfg"
const VERSION := 1

var path := DEFAULT_PATH


func _init(p_path: String = DEFAULT_PATH) -> void:
	path = p_path


func load_bindings() -> Dictionary:
	load_error = OK
	if not FileAccess.file_exists(path):
		var backup := "%s.previous" % ProjectSettings.globalize_path(path)
		if FileAccess.file_exists(backup):
			load_error = DirAccess.rename_absolute(backup, ProjectSettings.globalize_path(path))
			if load_error != OK:
				return {}
		else:
			return {}
	var config := ConfigFile.new()
	load_error = config.load(path)
	if load_error != OK:
		return {}
	if config.get_value("meta", "version", -1) != VERSION:
		load_error = ERR_FILE_UNRECOGNIZED
		return {}
	var data: Variant = config.get_value("input", "bindings", null)
	if not data is Dictionary:
		load_error = ERR_INVALID_DATA
		return {}
	return (data as Dictionary).duplicate(true)


func save_bindings(bindings: Dictionary) -> Error:
	var config := ConfigFile.new()
	config.set_value("meta", "version", VERSION)
	config.set_value("input", "bindings", bindings.duplicate(true))
	var temporary := "%s.tmp" % path
	var error := config.save(temporary)
	if error != OK:
		return error
	var check := ConfigFile.new()
	error = check.load(temporary)
	if error != OK or check.get_value("input", "bindings", null) != bindings:
		DirAccess.remove_absolute(ProjectSettings.globalize_path(temporary))
		return ERR_FILE_CORRUPT
	var destination := ProjectSettings.globalize_path(path)
	var temp_file := ProjectSettings.globalize_path(temporary)
	var backup := "%s.previous" % destination
	var had_previous := FileAccess.file_exists(path)
	if had_previous:
		if FileAccess.file_exists(backup):
			error = DirAccess.remove_absolute(backup)
			if error != OK:
				return error
		error = DirAccess.rename_absolute(destination, backup)
		if error != OK:
			return error
	error = DirAccess.rename_absolute(temp_file, destination)
	if error != OK and had_previous:
		DirAccess.rename_absolute(backup, destination)
	return error
