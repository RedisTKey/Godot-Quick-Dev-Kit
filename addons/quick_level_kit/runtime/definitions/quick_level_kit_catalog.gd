class_name QuickLevelKitCatalog
extends Resource

enum NextStatus {
	AVAILABLE,
	FINAL,
	UNKNOWN,
}

const ERROR_ENTRY_MISSING := &"entry_missing"
const ERROR_LEVEL_ID_EMPTY := &"level_id_empty"
const ERROR_LEVEL_ID_DUPLICATE := &"level_id_duplicate"
const ERROR_SCENE_MISSING := &"scene_missing"
const ERROR_SCENE_ROOT_NOT_LEVEL := &"scene_root_not_level"

@export var levels: Array[QuickLevelKitDefinition] = []
@export var default_flow: QuickLevelKitFlow


func get_level_count() -> int:
	return levels.size()


func get_definition_at(index: int) -> QuickLevelKitDefinition:
	if index < 0 or index >= levels.size():
		return null
	return levels[index]


func find_index(level_id: StringName) -> int:
	for index in levels.size():
		var definition := levels[index]
		if definition != null and definition.level_id == level_id:
			return index
	return -1


func get_definition(level_id: StringName) -> QuickLevelKitDefinition:
	return get_definition_at(find_index(level_id))


func get_first_level_id() -> StringName:
	var definition := get_definition_at(0)
	return definition.level_id if definition != null else &""


func get_last_level_id() -> StringName:
	var definition := get_definition_at(levels.size() - 1)
	return definition.level_id if definition != null else &""


func resolve_next(level_id: StringName) -> Dictionary:
	var index := find_index(level_id)
	if index < 0:
		return _resolution(NextStatus.UNKNOWN)
	if index >= levels.size() - 1:
		return _resolution(NextStatus.FINAL)
	var next_definition := levels[index + 1]
	if next_definition == null:
		return _resolution(NextStatus.UNKNOWN)
	return _resolution(NextStatus.AVAILABLE, next_definition)


func get_flow_for(level_id: StringName) -> QuickLevelKitFlow:
	var definition := get_definition(level_id)
	if definition != null and definition.flow != null:
		return definition.flow
	return default_flow


func get_validation_errors() -> Array[Dictionary]:
	var errors: Array[Dictionary] = []
	var seen: Dictionary = {}
	for index in levels.size():
		var definition := levels[index]
		if definition == null:
			errors.append(_error(ERROR_ENTRY_MISSING, index))
			continue
		if definition.level_id.is_empty():
			errors.append(_error(ERROR_LEVEL_ID_EMPTY, index))
		elif seen.has(definition.level_id):
			errors.append(_error(ERROR_LEVEL_ID_DUPLICATE, index, definition.level_id))
		else:
			seen[definition.level_id] = true
		if definition.scene == null:
			errors.append(_error(ERROR_SCENE_MISSING, index, definition.level_id))
			continue
		var root := definition.scene.instantiate()
		if root == null or not root is QuickLevelKitLevel:
			errors.append(_error(ERROR_SCENE_ROOT_NOT_LEVEL, index, definition.level_id))
		if root != null:
			root.free()
	return errors


func validate() -> bool:
	return get_validation_errors().is_empty()


func _resolution(
	status: NextStatus,
	definition: QuickLevelKitDefinition = null
) -> Dictionary:
	return {
		&"status": status,
		&"level_id": definition.level_id if definition != null else &"",
		&"definition": definition,
	}


func _error(
	code: StringName,
	index: int,
	level_id: StringName = &""
) -> Dictionary:
	return {
		&"code": code,
		&"index": index,
		&"level_id": level_id,
	}
