class_name QuickLevelKitStopStage
extends QuickLevelKitStage


func on_enter(runtime: QuickLevelKitStageRuntime) -> void:
	var manager := runtime.session.context.manager
	if manager != null and manager.has_method(&"finish_flow"):
		manager.call(&"finish_flow")
