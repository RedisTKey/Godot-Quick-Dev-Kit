class_name QuickAudioAsset
extends Resource

@export var stream: AudioStream
@export var track: QuickAudioTrackDefinition
@export_range(-80.0, 24.0, 0.1, "suffix:dB") var volume_db := 0.0
@export_range(0.01, 4.0, 0.01) var pitch_scale := 1.0
