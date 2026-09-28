extends SceneTree

const DEFAULT_LIBRARY_PATH := (
	"res://addons/quick_audio_manager/resources/default_track_library.tres"
)
const MUSIC_TRACK_PATH := (
	"res://addons/quick_audio_manager/resources/tracks/music.tres"
)
const SFX_TRACK_PATH := (
	"res://addons/quick_audio_manager/resources/tracks/sfx.tres"
)

var _failures := 0
var _error_codes: Array[StringName] = []
var _stopped_events: Array[StringName] = []


func _init() -> void:
	call_deferred("_run")


func _run() -> void:
	_test_audio_asset_defaults()
	_test_default_library()

	var manager := QuickAudioManagerService.new()
	root.add_child(manager)
	manager.audio_error.connect(_on_audio_error)
	manager.audio_stopped.connect(_on_audio_stopped)
	_test_default_tracks_and_buses(manager)
	_test_parallel_sfx_and_music_voice_limit(manager)
	_test_music_keeps_processing_while_paused(manager)
	_test_track_controls(manager)
	_test_master_volume_lifecycle_restore()
	_test_invalid_assets(manager)
	_test_ensure_playing(manager)
	_test_invalid_library_is_transactional()
	manager.stop_all()
	await _drain_audio_cleanup()
	manager.free()

	if _failures == 0:
		print("Quick Audio Manager tests passed.")
	quit(_failures)


func _test_audio_asset_defaults() -> void:
	var asset := QuickAudioAsset.new()
	_assert_equal(asset.stream, null, "Stream must default to null")
	_assert_equal(asset.track, null, "Track selection must be explicit")
	_assert_equal(asset.volume_db, 0.0, "Volume must default to 0 dB")
	_assert_equal(asset.pitch_scale, 1.0, "Pitch must default to 1")


func _test_default_library() -> void:
	var library := load(DEFAULT_LIBRARY_PATH) as QuickAudioTrackLibrary
	_assert_true(library != null, "Default library must load")
	if library == null:
		return
	_assert_equal(library.tracks.size(), 3, "Default library must contain three tracks")
	var expected := [
		[&"music", &"QuickMusic", 1, Node.PROCESS_MODE_ALWAYS],
		[&"sfx", &"QuickSFX", 24, Node.PROCESS_MODE_INHERIT],
		[&"ui", &"QuickUI", 8, Node.PROCESS_MODE_INHERIT],
	]
	for index: int in expected.size():
		var track := library.tracks[index]
		_assert_equal(track.track_id, expected[index][0], "Unexpected track id")
		_assert_equal(track.bus_name, expected[index][1], "Unexpected bus name")
		_assert_equal(track.max_voices, expected[index][2], "Unexpected voice limit")
		_assert_equal(track.process_mode, expected[index][3], "Unexpected process mode")


func _test_default_tracks_and_buses(manager: QuickAudioManagerService) -> void:
	_assert_true(manager.is_configured(), "Manager must configure its default library")
	_assert_equal(manager.get_track_ids(), [&"music", &"sfx", &"ui"], "Track order must be stable")
	for item: Array in [
		[&"music", &"QuickMusic"],
		[&"sfx", &"QuickSFX"],
		[&"ui", &"QuickUI"],
	]:
		_assert_true(manager.has_track(item[0]), "Track must be registered")
		_assert_equal(manager.get_track_bus_name(item[0]), item[1], "Track must route correctly")
		var bus_index := AudioServer.get_bus_index(item[1])
		_assert_true(bus_index >= 0, "Configured bus must exist")
		if bus_index >= 0:
			_assert_equal(AudioServer.get_bus_send(bus_index), &"Master", "Bus must send to Master")


func _test_track_controls(manager: QuickAudioManagerService) -> void:
	var original_track_volume := manager.get_track_volume_db(&"sfx")
	var original_muted := manager.is_track_muted(&"ui")
	var original_master_volume := manager.get_master_volume_db()
	_assert_true(manager.set_track_volume_db(&"sfx", -12.5), "Track volume must be writable")
	_assert_float_equal(manager.get_track_volume_db(&"sfx"), -12.5, "Track volume must be readable")
	_assert_true(manager.set_master_volume_db(-100.0), "Master volume must clamp")
	_assert_float_equal(manager.get_master_volume_db(), -80.0, "Master lower bound must be -80 dB")
	_assert_true(manager.set_master_volume_db(12.0), "Master volume must clamp")
	_assert_float_equal(manager.get_master_volume_db(), 0.0, "Master upper bound must be 0 dB")
	_assert_true(manager.set_track_muted(&"ui", true), "Track mute must be writable")
	_assert_true(manager.is_track_muted(&"ui"), "Track mute must be readable")
	manager.set_track_volume_db(&"sfx", original_track_volume)
	manager.set_track_muted(&"ui", original_muted)
	AudioServer.set_bus_volume_db(AudioServer.get_bus_index(&"Master"), original_master_volume)
	_assert_false(manager.set_track_volume_db(&"missing", 0.0), "Unknown track must be rejected")
	_assert_false(manager.has_track(&"Master"), "Master must stay outside the track registry")


func _test_parallel_sfx_and_music_voice_limit(
	manager: QuickAudioManagerService
) -> void:
	manager.stop_all()
	var stream := _create_test_stream()
	var sfx_asset := _create_asset(stream, load(SFX_TRACK_PATH) as QuickAudioTrackDefinition)
	var first_sfx := manager.play(sfx_asset)
	var second_sfx := manager.play(sfx_asset)
	_assert_true(first_sfx != null and second_sfx != null, "SFX playback must start")
	_assert_true(first_sfx != second_sfx, "Each play call must own an independent player")
	_assert_equal(first_sfx.bus, &"QuickSFX", "SFX assets must route to the SFX bus")
	_assert_equal(
		manager.get_active_player_count(&"sfx"),
		2,
		"SFX playback must support parallel voices"
	)

	var music_asset := _create_asset(
		stream,
		load(MUSIC_TRACK_PATH) as QuickAudioTrackDefinition
	)
	var first_music := manager.play(music_asset)
	_assert_true(first_music != null, "The first music voice must start")
	var second_music := manager.play(music_asset)
	_assert_true(second_music != null, "The replacement music voice must start")
	_assert_equal(second_music.bus, &"QuickMusic", "Music assets must route to Music")
	_assert_equal(
		manager.get_active_player_count(&"music"),
		1,
		"Music must enforce its single-voice limit"
	)
	_assert_true(
		_stopped_events.has(&"music"),
		"Replacing music must stop the previous music voice"
	)
	_assert_equal(manager.stop_track(&"sfx"), 2, "stop_track must stop all SFX voices")
	_assert_equal(manager.stop_player(second_music), true, "stop_player must stop one voice")
	_assert_equal(manager.get_active_player_count(), 0, "All test voices must be stopped")


func _test_music_keeps_processing_while_paused(
	manager: QuickAudioManagerService
) -> void:
	manager.stop_all()
	var stream := _create_test_stream()
	var music_asset := _create_asset(
		stream,
		load(MUSIC_TRACK_PATH) as QuickAudioTrackDefinition
	)
	var sfx_asset := _create_asset(
		stream,
		load(SFX_TRACK_PATH) as QuickAudioTrackDefinition
	)
	var music_player := manager.play(music_asset)
	var sfx_player := manager.play(sfx_asset)
	_assert_true(music_player != null, "Music playback must start before pausing")
	_assert_true(sfx_player != null, "SFX playback must start before pausing")
	if music_player == null or sfx_player == null:
		manager.stop_all()
		return

	paused = true
	_assert_true(
		music_player.can_process(),
		"Music playback must keep processing while the SceneTree is paused"
	)
	_assert_false(
		sfx_player.can_process(),
		"SFX playback must remain paused with the SceneTree"
	)
	paused = false
	manager.stop_all()


func _test_master_volume_lifecycle_restore() -> void:
	var master_bus_index := AudioServer.get_bus_index(&"Master")
	_assert_true(master_bus_index >= 0, "AudioServer must provide the Master bus")
	if master_bus_index < 0:
		return
	var master_volume_before := AudioServer.get_bus_volume_db(master_bus_index)
	var manager := QuickAudioManagerService.new()
	root.add_child(manager)
	_assert_true(
		manager.set_master_volume_db(-24.0),
		"Standalone managers must be able to modify Master volume"
	)
	_assert_float_equal(
		AudioServer.get_bus_volume_db(master_bus_index),
		-24.0,
		"Master volume writes must reach AudioServer"
	)
	manager.free()
	_assert_float_equal(
		AudioServer.get_bus_volume_db(master_bus_index),
		master_volume_before,
		"Manager teardown must restore its previous Master volume"
	)


func _test_invalid_assets(manager: QuickAudioManagerService) -> void:
	_error_codes.clear()
	_assert_equal(manager.play(null), null, "Null assets must be rejected")
	var streamless := QuickAudioAsset.new()
	_assert_equal(manager.play(streamless), null, "Streamless assets must be rejected")
	var trackless := QuickAudioAsset.new()
	trackless.stream = _create_test_stream()
	trackless.track = null
	_assert_equal(manager.play(trackless), null, "Trackless assets must be rejected")
	var unknown_track := QuickAudioTrackDefinition.new()
	unknown_track.track_id = &"unknown"
	unknown_track.bus_name = &"Unknown"
	var unknown_asset := _create_asset(_create_test_stream(), unknown_track)
	_assert_equal(
		manager.play(unknown_asset),
		null,
		"Assets that reference unconfigured tracks must be rejected"
	)
	_assert_equal(
		_error_codes,
		[
			QuickAudioManagerService.ERROR_ASSET_NULL,
			QuickAudioManagerService.ERROR_STREAM_NULL,
			QuickAudioManagerService.ERROR_ASSET_TRACK_NULL,
			QuickAudioManagerService.ERROR_TRACK_UNKNOWN,
		],
		"Invalid assets must report deterministic errors"
	)


func _test_ensure_playing(manager: QuickAudioManagerService) -> void:
	manager.stop_all()
	var stream := _create_test_stream()
	var track := load(MUSIC_TRACK_PATH) as QuickAudioTrackDefinition
	var asset := _create_asset(stream, track)
	var first := manager.ensure_playing(asset)
	var second := manager.ensure_playing(asset)
	_assert_true(first != null, "ensure_playing must start missing audio")
	_assert_equal(second, first, "ensure_playing must reuse the same active stream")
	_assert_equal(manager.get_active_player_count(&"music"), 1, "Idempotent playback must own one voice")
	manager.stop_all()


func _test_invalid_library_is_transactional() -> void:
	var first := QuickAudioTrackDefinition.new()
	first.track_id = &"duplicate"
	first.bus_name = &"QuickTransactionalA"
	var second := QuickAudioTrackDefinition.new()
	second.track_id = &"duplicate"
	second.bus_name = &"QuickTransactionalB"
	var library := QuickAudioTrackLibrary.new()
	library.tracks = [first, second]
	var bus_count_before := AudioServer.bus_count
	var manager := QuickAudioManagerService.new()
	manager.track_library = library
	root.add_child(manager)
	_assert_false(manager.is_configured(), "Invalid library must not configure")
	_assert_equal(AudioServer.bus_count, bus_count_before, "Invalid library must not mutate AudioServer")
	_assert_equal(
		AudioServer.get_bus_index(&"QuickTransactionalA"),
		-1,
		"Invalid libraries must not create partial buses"
	)
	manager.free()


func _create_test_stream() -> AudioStreamWAV:
	var stream := AudioStreamWAV.new()
	stream.format = AudioStreamWAV.FORMAT_16_BITS
	stream.mix_rate = 22050
	stream.stereo = false
	var data := PackedByteArray()
	data.resize(int(stream.mix_rate * 0.25) * 2)
	stream.data = data
	return stream


func _drain_audio_cleanup() -> void:
	for _index: int in 4:
		await process_frame


func _create_asset(
	stream: AudioStream,
	track: QuickAudioTrackDefinition
) -> QuickAudioAsset:
	var asset := QuickAudioAsset.new()
	asset.stream = stream
	asset.track = track
	return asset


func _on_audio_error(code: StringName, _message: String) -> void:
	_error_codes.append(code)


func _on_audio_stopped(
	track_id: StringName,
	_player: AudioStreamPlayer
) -> void:
	_stopped_events.append(track_id)


func _assert_equal(actual: Variant, expected: Variant, message: String) -> void:
	if actual != expected:
		_fail("%s; expected %s, got %s" % [message, expected, actual])


func _assert_float_equal(actual: float, expected: float, message: String) -> void:
	if not is_equal_approx(actual, expected):
		_fail("%s; expected %s, got %s" % [message, expected, actual])


func _assert_true(value: bool, message: String) -> void:
	if not value:
		_fail(message)


func _assert_false(value: bool, message: String) -> void:
	if value:
		_fail(message)


func _fail(message: String) -> void:
	_failures += 1
	push_error(message)
