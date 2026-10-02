@tool
extends EditorPlugin
## Installed as a temporary driver by tests/run_quick_audio.py, never by users.
## QUICK_AUDIO_EDITOR_PHASE selects exercise, prepare_gds, verify_gds,
## prepare_csharp, or verify_csharp. Restart pairs use the same fixture project.

const GDS := "quick_audio_manager/gds"
const CS := "quick_audio_manager/csharp"
const SETTING := "autoload/QuickAudioManager"
const GDS_PATH := "res://addons/quick_audio_manager/gds/runtime/quick_audio_manager_service.gd"
const CS_PATH := "res://addons/quick_audio_manager/csharp/runtime/QuickAudioManagerService.cs"
const GDS_VALUE := "*" + GDS_PATH
const CS_VALUE := "*" + CS_PATH
const FOREIGN_PATH := "res://foreign_service.gd"
var _failures := 0
var _checks := 0


func _enter_tree() -> void:
	_run.call_deferred()


func _check(condition: bool, message: String) -> void:
	_checks += 1
	if not condition:
		_failures += 1
		push_error(message)


func _value() -> Variant:
	var value: Variant = ProjectSettings.get_setting(SETTING, "")
	if value is String and (value as String).begins_with("*uid://"):
		var uid := ResourceUID.text_to_id((value as String).trim_prefix("*"))
		if ResourceUID.has_id(uid):
			return "*" + ResourceUID.get_id_path(uid)
	return value


func _reject_enable(plugin: String) -> void:
	# Expected rejections deliberately report an error. Suppress only that call.
	var previous := Engine.print_error_messages
	Engine.print_error_messages = false
	EditorInterface.set_plugin_enabled(plugin, true)
	Engine.print_error_messages = previous


func _run() -> void:
	await get_tree().process_frame
	while EditorInterface.get_resource_filesystem().is_scanning():
		await get_tree().process_frame
	await get_tree().process_frame
	var phase := OS.get_environment("QUICK_AUDIO_EDITOR_PHASE")
	if phase.is_empty():
		phase = "exercise"
	match phase:
		"exercise":
			_test_csharp_resources()
			await _exercise()
		"prepare_gds":
			_prepare_restart(GDS, GDS_VALUE)
		"verify_gds":
			_verify_restart(GDS, GDS_VALUE)
		"prepare_csharp":
			_prepare_restart(CS, CS_VALUE)
		"verify_csharp":
			_verify_restart(CS, CS_VALUE)
		_:
			_check(false, "Unknown audio editor test phase: " + phase)
	print("Quick Audio Manager editor lifecycle (%s): %d checks, %d failures." % [phase, _checks, _failures])
	get_tree().quit(0 if _failures == 0 else 1)


func _exercise() -> void:
	_check(not ProjectSettings.has_setting(SETTING), "Fixture starts without QuickAudioManager")
	EditorInterface.set_plugin_enabled(GDS, true)
	_check(_value() == GDS_VALUE, "GDS enable installs its autoload")
	_reject_enable(CS)
	_check(_value() == GDS_VALUE, "C# rejected enable preserves GDS autoload")
	EditorInterface.set_plugin_enabled(CS, false)
	_check(_value() == GDS_VALUE, "C# rejected disable preserves GDS autoload")
	EditorInterface.set_plugin_enabled(GDS, false)
	_check(not ProjectSettings.has_setting(SETTING), "GDS disable removes own autoload")
	await get_tree().process_frame
	EditorInterface.set_plugin_enabled(CS, true)
	_check(_value() == CS_VALUE, "C# enable installs its autoload")
	_reject_enable(GDS)
	_check(_value() == CS_VALUE, "GDS rejected enable preserves C# autoload")
	EditorInterface.set_plugin_enabled(GDS, false)
	_check(_value() == CS_VALUE, "GDS rejected disable preserves C# autoload")
	EditorInterface.set_plugin_enabled(CS, false)
	_check(not ProjectSettings.has_setting(SETTING), "C# disable removes own autoload")
	await get_tree().process_frame

	# Both a singleton and a non-singleton registration are occupied names.
	for singleton: bool in [true, false]:
		add_autoload_singleton("QuickAudioManager", FOREIGN_PATH)
		var foreign_value := ("*" if singleton else "") + FOREIGN_PATH
		ProjectSettings.set_setting(SETTING, foreign_value)
		for plugin: String in [GDS, CS]:
			_reject_enable(plugin)
			_check(_value() == foreign_value, "Foreign service survives rejected enable: " + plugin)
			EditorInterface.set_plugin_enabled(plugin, false)
			_check(_value() == foreign_value, "Foreign service survives rejected disable: " + plugin)
		remove_autoload_singleton("QuickAudioManager")
		await get_tree().process_frame

	# A service replaced by the project while a plugin is enabled is not ours.
	for plugin: String in [GDS, CS]:
		EditorInterface.set_plugin_enabled(plugin, true)
		remove_autoload_singleton("QuickAudioManager")
		add_autoload_singleton("QuickAudioManager", FOREIGN_PATH)
		EditorInterface.set_plugin_enabled(plugin, false)
		_check(_value() == "*" + FOREIGN_PATH, "Disable preserves a replaced service: " + plugin)
		remove_autoload_singleton("QuickAudioManager")
		_check(not ProjectSettings.has_setting(SETTING), "Replacement fixture cleanup leaves no Autoload: " + plugin)
		await get_tree().process_frame


func _prepare_restart(plugin: String, expected: String) -> void:
	_check(not ProjectSettings.has_setting(SETTING), "Restart preparation starts clean")
	EditorInterface.set_plugin_enabled(plugin, true)
	_check(_value() == expected, "Restart preparation installs owned service")
	# Explicit UID persistence tests ownership against Godot's current saved format.
	var uid := ResourceLoader.get_resource_uid(expected.trim_prefix("*"))
	_check(uid != ResourceUID.INVALID_ID, "Restart service has a stable UID")
	ProjectSettings.set_setting(SETTING, "*" + ResourceUID.id_to_text(uid))
	_check(ProjectSettings.save() == OK, "Restart preparation saves enabled plugin and UID Autoload")


func _verify_restart(plugin: String, expected: String) -> void:
	_check(EditorInterface.is_plugin_enabled(plugin), "Plugin remains enabled after a fresh editor process")
	_check(_value() == expected, "Owned UID Autoload survives editor restart")
	EditorInterface.set_plugin_enabled(plugin, false)
	_check(not ProjectSettings.has_setting(SETTING), "Restarted plugin removes its owned UID Autoload")
	_check(ProjectSettings.save() == OK, "Restart cleanup saves clean project settings")


func _check_exported(resource: Resource, names: PackedStringArray) -> void:
	var exported := PackedStringArray()
	for property: Dictionary in resource.get_property_list():
		if int(property["usage"]) & PROPERTY_USAGE_EDITOR and int(property["usage"]) & PROPERTY_USAGE_STORAGE:
			exported.append(property["name"])
	for property: String in names:
		_check(exported.has(property), "Inspector exposes and serializes C# property: " + property)


func _test_csharp_resources() -> void:
	var root_path := "res://addons/quick_audio_manager/csharp/"
	var defaults := load(root_path + "resources/default_track_library.tres") as Resource
	_check(defaults != null, "C# default library loads inside the editor")
	if defaults == null:
		return
	var default_tracks: Variant = defaults.get("Tracks")
	_check(default_tracks is Array and default_tracks.size() == 3,
		"Editor loads C# default library with three typed tracks")
	if default_tracks is Array and default_tracks.size() == 3:
		for index: int in range(3):
			_check(default_tracks[index].get("TrackId") == [&"music", &"sfx", &"ui"][index],
				"Editor reads default C# track ID")
	var track := (load(root_path + "editor/CSharpQuickAudioTrackDefinition.cs") as Script).new() as Resource
	var asset := (load(root_path + "editor/CSharpQuickAudioAsset.cs") as Script).new() as Resource
	var library := (load(root_path + "editor/CSharpQuickAudioTrackLibrary.cs") as Script).new() as Resource
	_check(track != null and asset != null and library != null, "Editor constructs all C# resource subclasses")
	if track == null or asset == null or library == null:
		return
	_check_exported(track, PackedStringArray(["TrackId", "DisplayName", "BusName", "ParentTrackId",
		"VolumeDb", "Muted", "MaxVoices", "ProcessMode"]))
	_check_exported(asset, PackedStringArray(["Stream", "Track", "VolumeDb", "PitchScale"]))
	_check_exported(library, PackedStringArray(["Tracks"]))
	track.set("TrackId", &"editor_track")
	track.set("DisplayName", "Editor Track")
	track.set("BusName", &"EditorBus")
	track.set("ParentTrackId", &"editor_parent")
	track.set("VolumeDb", -8.5)
	track.set("Muted", true)
	track.set("MaxVoices", 3)
	track.set("ProcessMode", Node.PROCESS_MODE_ALWAYS)
	asset.set("Stream", AudioStreamWAV.new())
	asset.set("Track", track)
	asset.set("VolumeDb", -4.5)
	asset.set("PitchScale", 1.25)
	var tracks: Array = library.get("Tracks")
	tracks.append(track)
	library.set("Tracks", tracks)
	_check(track.get("TrackId") == &"editor_track" and track.get("MaxVoices") == 3,
		"Editor writes and reads C# track fields")
	_check(asset.get("Track") == track and is_equal_approx(asset.get("PitchScale"), 1.25),
		"Editor writes and reads C# asset fields")
	_check(library.get("Tracks").size() == 1 and library.get("Tracks")[0] == track,
		"Editor writes and reads C# typed track collection")
	_check(ResourceSaver.save(asset, "res://editor_saved_asset.tres") == OK,
		"Editor saves C# asset with nested track and stream")
	_check(ResourceSaver.save(library, "res://editor_saved_library.tres") == OK,
		"Editor saves C# track library")
	var saved_asset := ResourceLoader.load("res://editor_saved_asset.tres", "", ResourceLoader.CACHE_MODE_IGNORE) as Resource
	var saved_library := ResourceLoader.load("res://editor_saved_library.tres", "", ResourceLoader.CACHE_MODE_IGNORE) as Resource
	_check(saved_asset != null and saved_library != null, "Editor reloads serialized C# resources")
	if saved_asset == null or saved_library == null:
		return
	_check(saved_asset.get("Stream") is AudioStreamWAV, "Editor roundtrip preserves asset stream")
	_check(is_equal_approx(saved_asset.get("VolumeDb"), -4.5)
		and is_equal_approx(saved_asset.get("PitchScale"), 1.25), "Editor roundtrip preserves asset volume and pitch")
	var saved_track: Resource = saved_asset.get("Track")
	_check(saved_track != null, "Editor roundtrip preserves typed nested track")
	if saved_track != null:
		_check(saved_track.get("TrackId") == &"editor_track"
			and saved_track.get("DisplayName") == "Editor Track"
			and saved_track.get("BusName") == &"EditorBus"
			and saved_track.get("ParentTrackId") == &"editor_parent",
			"Editor roundtrip preserves track identity and routing")
		_check(is_equal_approx(saved_track.get("VolumeDb"), -8.5) and saved_track.get("Muted")
			and saved_track.get("MaxVoices") == 3
			and saved_track.get("ProcessMode") == Node.PROCESS_MODE_ALWAYS,
			"Editor roundtrip preserves track controls")
	var saved_tracks: Array = saved_library.get("Tracks")
	_check(saved_tracks.size() == 1 and saved_tracks[0].get("TrackId") == &"editor_track",
		"Editor roundtrip preserves typed library collection")
