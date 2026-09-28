class_name QuickLevelKitManager
extends Node

signal session_started(level_id: StringName)
signal session_stopped(level_id: StringName)
signal stage_changed(previous_id: StringName, current_id: StringName)
signal level_loaded(level: QuickLevelKitLevel)
signal transition_rejected(context: Dictionary)
signal progress_changed(progress: QuickLevelKitProgress)
signal flow_error(code: StringName, message: String)

const ERROR_CATALOG_MISSING := &"catalog_missing"
const ERROR_CATALOG_INVALID := &"catalog_invalid"
const ERROR_LEVEL_UNKNOWN := &"level_unknown"
const ERROR_SCENE_INVALID := &"scene_invalid"
const ERROR_ROOT_NOT_LEVEL := &"root_not_level"
const ERROR_NO_SESSION := &"no_session"

@export var catalog: QuickLevelKitCatalog

var _progress_store: QuickLevelKitProgressStore
var _progress := QuickLevelKitProgress.new()
var _container: Node
var _active_host: Node
var _staging_host: Node
var _session: QuickLevelKitSession
var _current_level: QuickLevelKitLevel
var _current_level_id: StringName = &""


func _ready() -> void:
	if _progress_store == null:
		_progress_store = QuickLevelKitConfigFileProgressStore.new()
	_ensure_default_hosts()
	_progress = _progress_store.load_progress()


func _exit_tree() -> void:
	finish_flow()


func configure_hosts(active_host: Node, staging_host: Node) -> bool:
	if active_host == null or staging_host == null or active_host == staging_host:
		return false
	_active_host = active_host
	_staging_host = staging_host
	return true


func set_catalog(value: QuickLevelKitCatalog) -> void:
	catalog = value


func get_catalog() -> QuickLevelKitCatalog:
	return catalog


func set_progress_store(store: QuickLevelKitProgressStore) -> void:
	if store == null:
		return
	_progress_store = store
	_progress = store.load_progress()


func get_progress_store() -> QuickLevelKitProgressStore:
	return _progress_store


func get_progress() -> QuickLevelKitProgress:
	return _progress


func save_progress() -> Error:
	if _progress_store == null:
		return ERR_UNCONFIGURED
	return _progress_store.save_progress(_progress)


func get_current_level() -> QuickLevelKitLevel:
	return _current_level if is_instance_valid(_current_level) else null


func get_current_level_id() -> StringName:
	return _current_level_id


func get_current_stage_id() -> StringName:
	return _session.get_current_stage_id() if _session != null else &""


func is_flow_active() -> bool:
	return _session != null and _session.is_active()


func is_level_unlocked(level_id: StringName) -> bool:
	if catalog == null:
		return false
	var index := catalog.find_index(level_id)
	var highest := catalog.find_index(_progress.highest_unlocked_level_id)
	return index >= 0 and highest >= 0 and index <= highest


func unlock_level(level_id: StringName) -> bool:
	if catalog == null:
		return false
	var index := catalog.find_index(level_id)
	var highest := catalog.find_index(_progress.highest_unlocked_level_id)
	if index < 0 or (highest >= 0 and index <= highest):
		return false
	_progress.highest_unlocked_level_id = level_id
	save_progress()
	progress_changed.emit(_progress)
	return true


func load_first_level() -> bool:
	if catalog == null or catalog.get_level_count() == 0:
		_emit_flow_error(ERROR_CATALOG_MISSING, "A valid catalog is required.")
		return false
	return load_level(catalog.get_first_level_id())


func continue_game() -> bool:
	if catalog == null:
		_emit_flow_error(ERROR_CATALOG_MISSING, "A valid catalog is required.")
		return false
	var level_id := _progress.highest_unlocked_level_id
	if level_id.is_empty() or catalog.find_index(level_id) < 0:
		level_id = catalog.get_first_level_id()
	return load_level(level_id)


func load_level(level_id: StringName) -> bool:
	if catalog == null or not catalog.validate():
		_emit_flow_error(ERROR_CATALOG_INVALID, "Catalog is missing or invalid.")
		return false
	var definition := catalog.get_definition(level_id)
	if definition == null:
		_emit_flow_error(ERROR_LEVEL_UNKNOWN, "Unknown level id '%s'." % level_id)
		return false

	_teardown_current()
	var level := _instantiate_level(definition)
	if level == null:
		return false

	_active_host.add_child(level)
	_current_level = level
	_current_level_id = definition.level_id
	level.flow_manager = self

	var flow: QuickLevelKitFlow = catalog.get_flow_for(definition.level_id)
	if flow == null or not flow.validate():
		flow = QuickLevelKitDefaultFlow.create()

	var context := QuickLevelKitContext.new()
	context.level_id = definition.level_id
	context.level = level
	context.manager = self
	context.progress = _progress
	_session = QuickLevelKitSession.new(flow, context)
	_session.transition_rejected.connect(_on_transition_rejected)
	_session.session_stopped.connect(_on_session_stopped)

	_progress.current_level_id = definition.level_id
	if _progress.highest_unlocked_level_id.is_empty():
		_progress.highest_unlocked_level_id = definition.level_id
	save_progress()
	progress_changed.emit(_progress)

	level_loaded.emit(level)
	session_started.emit(definition.level_id)
	_session.start()
	stage_changed.emit(&"", _session.get_current_stage_id())
	return true


func request_transition(context: Dictionary) -> bool:
	if _session == null or not _session.is_active():
		_emit_flow_error(ERROR_NO_SESSION, "No active flow session to transition.")
		return false
	var session := _session
	var previous := session.get_current_stage_id()
	var accepted := session.request_transition(context)
	if _session == session:
		var current := session.get_current_stage_id()
		if current != previous:
			stage_changed.emit(previous, current)
	return accepted


func advance_level() -> bool:
	if catalog == null:
		return false
	var resolution := catalog.resolve_next(_current_level_id)
	if resolution.status != QuickLevelKitCatalog.NextStatus.AVAILABLE:
		return false
	var next_id: StringName = resolution.level_id
	unlock_level(next_id)
	return load_level(next_id)


func reload_level() -> bool:
	if _current_level_id.is_empty():
		return false
	return load_level(_current_level_id)


func finish_flow() -> bool:
	_teardown_current()
	return true


func reset_progress() -> bool:
	if catalog == null:
		return false
	_progress = QuickLevelKitProgress.new()
	_progress.highest_unlocked_level_id = catalog.get_first_level_id()
	_progress.current_level_id = catalog.get_first_level_id()
	_progress.level_states = {}
	save_progress()
	progress_changed.emit(_progress)
	return true


func _instantiate_level(definition: QuickLevelKitDefinition) -> QuickLevelKitLevel:
	if definition.scene == null:
		_emit_flow_error(ERROR_SCENE_INVALID, "Level '%s' has no scene." % definition.level_id)
		return null
	var root := definition.scene.instantiate()
	if root == null or not root is QuickLevelKitLevel:
		if root != null:
			root.free()
		_emit_flow_error(ERROR_ROOT_NOT_LEVEL, "Level scene root must extend QuickLevelKitLevel.")
		return null
	var level := root as QuickLevelKitLevel
	if level.level_id.is_empty():
		level.level_id = definition.level_id
	return level


func _teardown_current() -> void:
	if _session != null:
		_session.stop()
		_session = null
	if is_instance_valid(_current_level):
		_current_level.teardown()
		var parent := _current_level.get_parent()
		if parent != null:
			parent.remove_child(_current_level)
		_current_level.queue_free()
	_current_level = null
	_current_level_id = &""


func _on_transition_rejected(context: Dictionary) -> void:
	transition_rejected.emit(context)


func _on_session_stopped() -> void:
	session_stopped.emit(_current_level_id)


func _ensure_default_hosts() -> void:
	if is_instance_valid(_active_host) and is_instance_valid(_staging_host):
		return
	_container = Node.new()
	_container.name = "LevelContainer"
	_active_host = Node.new()
	_active_host.name = "ActiveLevelHost"
	_staging_host = Node.new()
	_staging_host.name = "StagingLevelHost"
	_container.add_child(_active_host)
	_container.add_child(_staging_host)
	add_child(_container)


func _emit_flow_error(code: StringName, message: String) -> void:
	flow_error.emit(code, message)
	push_error(message)
