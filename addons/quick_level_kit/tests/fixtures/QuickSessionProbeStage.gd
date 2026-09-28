class_name QuickSessionProbeStage
extends QuickLevelKitStage

var entered := 0
var exited := 0
var seen_score: Variant = null
var auto_request: Dictionary = {}


func on_enter(runtime: QuickLevelKitStageRuntime) -> void:
	entered += 1
	seen_score = runtime.session.context.get_value(&"score")
	if not auto_request.is_empty():
		runtime.session.request_transition(auto_request)


func on_exit(_runtime: QuickLevelKitStageRuntime) -> void:
	exited += 1
