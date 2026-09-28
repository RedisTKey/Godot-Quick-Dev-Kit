class_name QuickLevelKitProgressStore
extends RefCounted


func load_progress() -> QuickLevelKitProgress:
	return QuickLevelKitProgress.new()


func save_progress(_progress: QuickLevelKitProgress) -> Error:
	return OK


func clear() -> Error:
	return OK
