class_name QuickLevelKitFlow
extends Resource

const ERROR_START_STAGE_MISSING := &"start_stage_missing"
const ERROR_STAGE_ID_EMPTY := &"stage_id_empty"
const ERROR_STAGE_ID_DUPLICATE := &"stage_id_duplicate"
const ERROR_ROUTE_TARGET_UNKNOWN := &"route_target_unknown"
const ERROR_ROUTE_TARGET_EMPTY := &"route_target_empty"

@export var start_stage_id: StringName = &"prepare"
@export var stages: Array[QuickLevelKitStage] = []


func get_stage(stage_id: StringName) -> QuickLevelKitStage:
	for stage in stages:
		if stage != null and stage.stage_id == stage_id:
			return stage
	return null


func has_stage(stage_id: StringName) -> bool:
	return get_stage(stage_id) != null


func get_validation_errors() -> Array[Dictionary]:
	var errors: Array[Dictionary] = []
	if not has_stage(start_stage_id):
		errors.append({&"code": ERROR_START_STAGE_MISSING, &"stage_id": start_stage_id})
	var seen: Dictionary = {}
	for index in stages.size():
		var stage := stages[index]
		if stage == null:
			continue
		if stage.stage_id.is_empty():
			errors.append({&"code": ERROR_STAGE_ID_EMPTY, &"index": index})
		elif seen.has(stage.stage_id):
			errors.append({&"code": ERROR_STAGE_ID_DUPLICATE, &"stage_id": stage.stage_id})
		else:
			seen[stage.stage_id] = true
		for route in stage.routes:
			if route == null:
				continue
			if route.target_stage_id.is_empty():
				errors.append({&"code": ERROR_ROUTE_TARGET_EMPTY, &"stage_id": stage.stage_id})
			elif not has_stage(route.target_stage_id):
				errors.append({
					&"code": ERROR_ROUTE_TARGET_UNKNOWN,
					&"stage_id": stage.stage_id,
					&"target_stage_id": route.target_stage_id,
				})
	return errors


func validate() -> bool:
	return get_validation_errors().is_empty()
