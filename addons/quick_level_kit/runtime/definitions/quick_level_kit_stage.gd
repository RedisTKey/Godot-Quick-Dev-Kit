class_name QuickLevelKitStage
extends Resource

@export var stage_id: StringName = &""
@export var routes: Array[QuickLevelKitRoute] = []


func resolve_route(context: Dictionary) -> QuickLevelKitRoute:
	for route in routes:
		if route != null and route.matches(context):
			return route
	return null


func on_enter(_runtime: QuickLevelKitStageRuntime) -> void:
	pass


func on_exit(_runtime: QuickLevelKitStageRuntime) -> void:
	pass


func on_preparation_completed(_runtime: QuickLevelKitStageRuntime) -> void:
	pass
