extends Node

@export var audio_asset: QuickAudioAsset


func _unhandled_input(event: InputEvent) -> void:
	if event.is_action_pressed("ui_accept"):
		play_audio()


func play_audio() -> AudioStreamPlayer:
	if audio_asset == null:
		return null
	var manager := get_node_or_null("/root/QuickAudioManager") as QuickAudioManagerService
	if manager == null:
		return null
	return manager.play(audio_asset)
