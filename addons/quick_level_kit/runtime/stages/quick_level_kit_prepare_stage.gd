class_name QuickLevelKitPrepareStage
extends QuickLevelKitStage


func on_enter(runtime: QuickLevelKitStageRuntime) -> void:
	var level := runtime.session.context.level
	if level == null or not level.has_method(&"begin_preparation"):
		on_preparation_completed(runtime)
		return
	if level.has_signal(&"preparation_completed"):
		var handler := Callable(self, "on_preparation_completed").bind(runtime)
		if not level.is_connected(&"preparation_completed", handler):
			level.connect(&"preparation_completed", handler, CONNECT_ONE_SHOT)
	level.call(&"begin_preparation")


func on_preparation_completed(runtime: QuickLevelKitStageRuntime) -> void:
	runtime.session.request_transition({&"type": &"ready", &"data": {}})
