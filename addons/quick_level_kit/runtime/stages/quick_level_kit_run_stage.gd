class_name QuickLevelKitRunStage
extends QuickLevelKitStage


func on_enter(runtime: QuickLevelKitStageRuntime) -> void:
	var level := runtime.session.context.level
	if level != null and level.has_method(&"begin_running"):
		level.call(&"begin_running")
