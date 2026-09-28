class_name QuickAudioTrackDefinition
extends Resource

@export var track_id: StringName
@export var display_name := ""
@export var bus_name: StringName
@export var parent_track_id: StringName
@export_range(-80.0, 24.0, 0.1, "suffix:dB") var volume_db := 0.0
@export var muted := false
@export_range(1, 128, 1) var max_voices := 8
@export_enum("Inherit:0", "Pausable:1", "When Paused:2", "Always:3", "Disabled:4") var process_mode: int = Node.PROCESS_MODE_INHERIT
