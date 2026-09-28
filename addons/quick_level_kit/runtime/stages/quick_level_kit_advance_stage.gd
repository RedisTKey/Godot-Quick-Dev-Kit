class_name QuickLevelKitAdvanceStage
extends QuickLevelKitStage


func on_enter(runtime: QuickLevelKitStageRuntime) -> void:
	var manager := runtime.session.context.manager
	if manager != null and manager.has_method(&"advance_level") and manager.call(&"advance_level"):
		return
	runtime.session.request_transition({&"type": &"finished", &"data": {}})
