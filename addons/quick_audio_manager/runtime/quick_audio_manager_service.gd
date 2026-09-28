class_name QuickAudioManagerService
extends Node

signal audio_started(
	asset: QuickAudioAsset,
	track_id: StringName,
	player: AudioStreamPlayer
)
signal audio_stopped(track_id: StringName, player: AudioStreamPlayer)
signal library_configured(track_ids: Array[StringName])
signal audio_error(code: StringName, message: String)

const ERROR_LIBRARY_NULL := &"library_null"
const ERROR_LIBRARY_EMPTY := &"library_empty"
const ERROR_TRACK_NULL := &"track_null"
const ERROR_TRACK_ID_EMPTY := &"track_id_empty"
const ERROR_TRACK_ID_DUPLICATE := &"track_id_duplicate"
const ERROR_BUS_NAME_EMPTY := &"bus_name_empty"
const ERROR_BUS_NAME_RESERVED := &"bus_name_reserved"
const ERROR_BUS_NAME_DUPLICATE := &"bus_name_duplicate"
const ERROR_MAX_VOICES_INVALID := &"max_voices_invalid"
const ERROR_PROCESS_MODE_INVALID := &"process_mode_invalid"
const ERROR_PARENT_MISSING := &"parent_missing"
const ERROR_PARENT_CYCLE := &"parent_cycle"
const ERROR_ALREADY_CONFIGURED := &"already_configured"
const ERROR_BUS_CREATION_FAILED := &"bus_creation_failed"
const ERROR_NOT_CONFIGURED := &"not_configured"
const ERROR_ASSET_NULL := &"asset_null"
const ERROR_STREAM_NULL := &"stream_null"
const ERROR_ASSET_TRACK_NULL := &"asset_track_null"
const ERROR_TRACK_UNKNOWN := &"track_unknown"
const ERROR_TRACK_BUS_MISSING := &"track_bus_missing"

const DEFAULT_TRACK_LIBRARY: QuickAudioTrackLibrary = preload(
	"res://addons/quick_audio_manager/resources/default_track_library.tres"
)

@export var track_library: QuickAudioTrackLibrary = DEFAULT_TRACK_LIBRARY

var _tracks_by_id: Dictionary = {}
var _bus_names_by_track_id: Dictionary = {}
var _active_players: Dictionary = {}
var _created_bus_names: Array[StringName] = []
var _reused_bus_states: Dictionary = {}
var _master_volume_was_modified := false
var _master_volume_db_before_modification := 0.0
var _configured := false


func _ready() -> void:
	configure_library(track_library)


func _exit_tree() -> void:
	stop_all()
	_restore_audio_server()
	_tracks_by_id.clear()
	_bus_names_by_track_id.clear()
	_active_players.clear()
	_configured = false


func configure_library(library: QuickAudioTrackLibrary) -> bool:
	if _configured:
		_emit_error(
			ERROR_ALREADY_CONFIGURED,
			"The audio manager has already configured a track library."
		)
		return false
	if not _validate_library(library):
		return false

	var pending_tracks: Dictionary = {}
	var pending_bus_names: Dictionary = {}
	for track: QuickAudioTrackDefinition in library.tracks:
		pending_tracks[track.track_id] = track
		pending_bus_names[track.track_id] = track.bus_name

	if not _install_audio_server_tracks(library, pending_bus_names):
		_restore_audio_server()
		return false

	track_library = library
	_tracks_by_id = pending_tracks
	_bus_names_by_track_id = pending_bus_names
	for track_id: StringName in _tracks_by_id.keys():
		_active_players[track_id] = []
	_configured = true
	library_configured.emit(get_track_ids())
	return true


func play(asset: QuickAudioAsset) -> AudioStreamPlayer:
	if not _configured:
		_emit_error(ERROR_NOT_CONFIGURED, "Configure a valid track library before playback.")
		return null
	if asset == null:
		_emit_error(ERROR_ASSET_NULL, "The requested audio asset is null.")
		return null
	if asset.stream == null:
		_emit_error(ERROR_STREAM_NULL, "The requested audio asset has no stream.")
		return null
	if asset.track == null:
		_emit_error(ERROR_ASSET_TRACK_NULL, "The requested audio asset has no track.")
		return null

	var track_id := asset.track.track_id
	if not _tracks_by_id.has(track_id):
		_emit_error(
			ERROR_TRACK_UNKNOWN,
			"The audio asset references an unconfigured track: %s" % track_id
		)
		return null

	var bus_name := _bus_names_by_track_id[track_id] as StringName
	if AudioServer.get_bus_index(bus_name) < 0:
		_emit_error(
			ERROR_TRACK_BUS_MISSING,
			"The configured audio bus is unavailable: %s" % bus_name
		)
		return null

	_prune_track_players(track_id)
	var track := _tracks_by_id[track_id] as QuickAudioTrackDefinition
	var players := _active_players[track_id] as Array
	while players.size() >= track.max_voices:
		_stop_player_internal(players.front() as AudioStreamPlayer, track_id)
		players = _active_players[track_id] as Array

	var player := AudioStreamPlayer.new()
	player.process_mode = track.process_mode as Node.ProcessMode
	player.name = "QuickAudio_%s_%d" % [track_id, Time.get_ticks_usec()]
	player.stream = asset.stream
	player.bus = bus_name
	player.volume_db = asset.volume_db
	player.pitch_scale = asset.pitch_scale
	player.finished.connect(
		_on_player_finished.bind(player, track_id),
		CONNECT_ONE_SHOT
	)
	add_child(player)
	players.append(player)
	player.play()
	audio_started.emit(asset, track_id, player)
	return player


func ensure_playing(asset: QuickAudioAsset) -> AudioStreamPlayer:
	if (
		asset == null
		or asset.stream == null
		or asset.track == null
	):
		return play(asset)
	var track_id := asset.track.track_id
	if not _active_players.has(track_id):
		return play(asset)
	_prune_track_players(track_id)
	for player: AudioStreamPlayer in _active_players[track_id]:
		if (
			is_instance_valid(player)
			and player.stream == asset.stream
			and player.playing
		):
			return player
	return play(asset)


func stop_player(player: AudioStreamPlayer) -> bool:
	if not is_instance_valid(player):
		return false
	for track_id: StringName in _active_players.keys():
		var players := _active_players[track_id] as Array
		if players.has(player):
			return _stop_player_internal(player, track_id)
	return false


func stop_track(track_id: StringName) -> int:
	if not _active_players.has(track_id):
		return 0
	_prune_track_players(track_id)
	var players := (_active_players[track_id] as Array).duplicate()
	var stopped_count := 0
	for player: AudioStreamPlayer in players:
		if _stop_player_internal(player, track_id):
			stopped_count += 1
	return stopped_count


func stop_all() -> int:
	var stopped_count := 0
	var track_ids: Array[StringName] = []
	track_ids.assign(_active_players.keys())
	for track_id: StringName in track_ids:
		stopped_count += stop_track(track_id)
	return stopped_count


func get_active_player_count(track_id: StringName = &"") -> int:
	if track_id != &"":
		if not _active_players.has(track_id):
			return 0
		_prune_track_players(track_id)
		return (_active_players[track_id] as Array).size()

	var count := 0
	for configured_track_id: StringName in _active_players.keys():
		_prune_track_players(configured_track_id)
		count += (_active_players[configured_track_id] as Array).size()
	return count


func is_configured() -> bool:
	return _configured


func has_track(track_id: StringName) -> bool:
	return _tracks_by_id.has(track_id)


func get_track_ids() -> Array[StringName]:
	var track_ids: Array[StringName] = []
	if track_library == null:
		return track_ids
	for track: QuickAudioTrackDefinition in track_library.tracks:
		if track != null and _tracks_by_id.has(track.track_id):
			track_ids.append(track.track_id)
	return track_ids


func get_track_bus_name(track_id: StringName) -> StringName:
	return _bus_names_by_track_id.get(track_id, &"") as StringName


func set_track_volume_db(track_id: StringName, volume_db: float) -> bool:
	var bus_index := _get_track_bus_index(track_id)
	if bus_index < 0:
		return false
	AudioServer.set_bus_volume_db(bus_index, clampf(volume_db, -80.0, 24.0))
	return true


func get_track_volume_db(track_id: StringName) -> float:
	var bus_index := _get_track_bus_index(track_id)
	if bus_index < 0:
		return -80.0
	return AudioServer.get_bus_volume_db(bus_index)


func set_master_volume_db(volume_db: float) -> bool:
	var master_bus_index := AudioServer.get_bus_index(&"Master")
	if master_bus_index < 0:
		return false
	if not _master_volume_was_modified:
		_master_volume_db_before_modification = AudioServer.get_bus_volume_db(master_bus_index)
		_master_volume_was_modified = true
	AudioServer.set_bus_volume_db(master_bus_index, clampf(volume_db, -80.0, 0.0))
	return true


func get_master_volume_db() -> float:
	var master_bus_index := AudioServer.get_bus_index(&"Master")
	if master_bus_index < 0:
		return -80.0
	return AudioServer.get_bus_volume_db(master_bus_index)


func set_track_muted(track_id: StringName, muted: bool) -> bool:
	var bus_index := _get_track_bus_index(track_id)
	if bus_index < 0:
		return false
	AudioServer.set_bus_mute(bus_index, muted)
	return true


func is_track_muted(track_id: StringName) -> bool:
	var bus_index := _get_track_bus_index(track_id)
	if bus_index < 0:
		return false
	return AudioServer.is_bus_mute(bus_index)


func _validate_library(library: QuickAudioTrackLibrary) -> bool:
	if library == null:
		_emit_error(ERROR_LIBRARY_NULL, "The audio track library is null.")
		return false
	if library.tracks.is_empty():
		_emit_error(ERROR_LIBRARY_EMPTY, "The audio track library has no tracks.")
		return false

	var definitions: Dictionary = {}
	var bus_names: Dictionary = {}
	for index: int in library.tracks.size():
		var track := library.tracks[index]
		if track == null:
			_emit_error(ERROR_TRACK_NULL, "Track %d is null." % index)
			return false
		if track.track_id == &"":
			_emit_error(ERROR_TRACK_ID_EMPTY, "Track %d has an empty track_id." % index)
			return false
		if definitions.has(track.track_id):
			_emit_error(
				ERROR_TRACK_ID_DUPLICATE,
				"Duplicate audio track id: %s" % track.track_id
			)
			return false
		if track.bus_name == &"":
			_emit_error(
				ERROR_BUS_NAME_EMPTY,
				"Track %s has an empty bus_name." % track.track_id
			)
			return false
		if track.bus_name == &"Master":
			_emit_error(
				ERROR_BUS_NAME_RESERVED,
				"Track %s cannot replace the reserved Master bus." % track.track_id
			)
			return false
		if bus_names.has(track.bus_name):
			_emit_error(
				ERROR_BUS_NAME_DUPLICATE,
				"Duplicate audio bus name: %s" % track.bus_name
			)
			return false
		if track.max_voices < 1:
			_emit_error(
				ERROR_MAX_VOICES_INVALID,
				"Track %s must allow at least one voice." % track.track_id
			)
			return false
		if (
			track.process_mode < Node.PROCESS_MODE_INHERIT
			or track.process_mode > Node.PROCESS_MODE_DISABLED
		):
			_emit_error(
				ERROR_PROCESS_MODE_INVALID,
				"Track %s has an invalid process mode." % track.track_id
			)
			return false
		definitions[track.track_id] = track
		bus_names[track.bus_name] = true

	for track: QuickAudioTrackDefinition in library.tracks:
		if (
			track.parent_track_id != &""
			and not definitions.has(track.parent_track_id)
		):
			_emit_error(
				ERROR_PARENT_MISSING,
				"Track %s references a missing parent track: %s"
				% [track.track_id, track.parent_track_id]
			)
			return false

	var visit_states: Dictionary = {}
	for track_id: StringName in definitions.keys():
		if _visit_track_parent(track_id, definitions, visit_states):
			_emit_error(
				ERROR_PARENT_CYCLE,
				"The audio track hierarchy contains a cycle at: %s" % track_id
			)
			return false
	return true


func _visit_track_parent(
	track_id: StringName,
	definitions: Dictionary,
	visit_states: Dictionary
) -> bool:
	var state := int(visit_states.get(track_id, 0))
	if state == 1:
		return true
	if state == 2:
		return false

	visit_states[track_id] = 1
	var track := definitions[track_id] as QuickAudioTrackDefinition
	if (
		track.parent_track_id != &""
		and _visit_track_parent(track.parent_track_id, definitions, visit_states)
	):
		return true
	visit_states[track_id] = 2
	return false


func _install_audio_server_tracks(
	library: QuickAudioTrackLibrary,
	pending_bus_names: Dictionary
) -> bool:
	for track: QuickAudioTrackDefinition in library.tracks:
		var bus_index := AudioServer.get_bus_index(track.bus_name)
		if bus_index >= 0:
			_reused_bus_states[track.bus_name] = {
				"send": AudioServer.get_bus_send(bus_index),
				"volume_db": AudioServer.get_bus_volume_db(bus_index),
				"muted": AudioServer.is_bus_mute(bus_index),
			}
			continue

		AudioServer.add_bus()
		bus_index = AudioServer.bus_count - 1
		AudioServer.set_bus_name(bus_index, track.bus_name)
		if AudioServer.get_bus_index(track.bus_name) < 0:
			_emit_error(
				ERROR_BUS_CREATION_FAILED,
				"Could not create audio bus: %s" % track.bus_name
			)
			return false
		_created_bus_names.append(track.bus_name)

	for track: QuickAudioTrackDefinition in library.tracks:
		var bus_index := AudioServer.get_bus_index(track.bus_name)
		if bus_index < 0:
			_emit_error(
				ERROR_BUS_CREATION_FAILED,
				"Configured audio bus is unavailable: %s" % track.bus_name
			)
			return false
		var send_bus := &"Master"
		if track.parent_track_id != &"":
			send_bus = pending_bus_names[track.parent_track_id] as StringName
		AudioServer.set_bus_send(bus_index, send_bus)
		AudioServer.set_bus_volume_db(bus_index, track.volume_db)
		AudioServer.set_bus_mute(bus_index, track.muted)
	return true


func _restore_audio_server() -> void:
	for index: int in range(_created_bus_names.size() - 1, -1, -1):
		var bus_name := _created_bus_names[index]
		var bus_index := AudioServer.get_bus_index(bus_name)
		if bus_index >= 0:
			AudioServer.remove_bus(bus_index)
	_created_bus_names.clear()

	for bus_name: StringName in _reused_bus_states.keys():
		var bus_index := AudioServer.get_bus_index(bus_name)
		if bus_index < 0:
			continue
		var state := _reused_bus_states[bus_name] as Dictionary
		AudioServer.set_bus_send(bus_index, state.get("send", &"Master") as StringName)
		AudioServer.set_bus_volume_db(bus_index, float(state.get("volume_db", 0.0)))
		AudioServer.set_bus_mute(bus_index, bool(state.get("muted", false)))
	_reused_bus_states.clear()

	if _master_volume_was_modified:
		var master_bus_index := AudioServer.get_bus_index(&"Master")
		if master_bus_index >= 0:
			AudioServer.set_bus_volume_db(
				master_bus_index,
				_master_volume_db_before_modification
			)
		_master_volume_was_modified = false


func _get_track_bus_index(track_id: StringName) -> int:
	if not _bus_names_by_track_id.has(track_id):
		return -1
	var bus_name := _bus_names_by_track_id[track_id] as StringName
	return AudioServer.get_bus_index(bus_name)


func _stop_player_internal(
	player: AudioStreamPlayer,
	track_id: StringName
) -> bool:
	if not is_instance_valid(player) or not _active_players.has(track_id):
		return false
	var players := _active_players[track_id] as Array
	if not players.has(player):
		return false

	players.erase(player)
	player.stop()
	audio_stopped.emit(track_id, player)
	player.stream = null
	if player.is_inside_tree():
		player.queue_free()
	else:
		player.free()
	return true


func _on_player_finished(
	player: AudioStreamPlayer,
	track_id: StringName
) -> void:
	_stop_player_internal(player, track_id)


func _prune_track_players(track_id: StringName) -> void:
	if not _active_players.has(track_id):
		return
	var players := _active_players[track_id] as Array
	for index: int in range(players.size() - 1, -1, -1):
		var player := players[index] as AudioStreamPlayer
		if not is_instance_valid(player) or player.is_queued_for_deletion():
			players.remove_at(index)


func _emit_error(code: StringName, message: String) -> void:
	audio_error.emit(code, message)
