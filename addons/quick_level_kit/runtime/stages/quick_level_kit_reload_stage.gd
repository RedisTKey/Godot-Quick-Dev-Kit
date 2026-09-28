class_name QuickLevelKitReloadStage
extends QuickLevelKitStage


func on_enter(runtime: QuickLevelKitStageRuntime) -> void:
	var manager := runtime.session.context.manager
	if manager != null and manager.has_method(&"reload_level") and manager.call(&"reload_level"):
		return
	runtime.session.request_transition({&"type": &"finished", &"data": {}})
