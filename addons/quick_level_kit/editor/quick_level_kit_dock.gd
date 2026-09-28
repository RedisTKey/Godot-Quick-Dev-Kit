@tool
class_name QuickLevelKitDock
extends Control

var _catalog: QuickLevelKitCatalog
var _catalog_path := ""
var _level_list: ItemList
var _error_label: Label
var _status_label: Label


func _ready() -> void:
	name = "Quick Level Kit"
	custom_minimum_size = Vector2(280, 320)
	_build_ui()
	refresh()


func set_catalog(catalog: QuickLevelKitCatalog) -> void:
	_catalog = catalog
	refresh()


func get_catalog() -> QuickLevelKitCatalog:
	return _catalog


func refresh() -> void:
	if _level_list == null:
		return
	_level_list.clear()
	if _catalog == null:
		_status_label.text = "No catalog assigned."
		return
	for index in _catalog.get_level_count():
		var definition := _catalog.get_definition_at(index)
		if definition == null:
			_level_list.add_item("<null>")
		else:
			_level_list.add_item("%d: %s" % [index, definition.level_id])
	_status_label.text = "Levels: %d" % _catalog.get_level_count()


func validate_catalog() -> Array[Dictionary]:
	if _catalog == null:
		return [{&"code": &"catalog_missing"}]
	var errors := _catalog.get_validation_errors()
	if _error_label != null:
		_error_label.text = "OK" if errors.is_empty() else str(errors)
	return errors


func add_level(level_id: StringName, scene: PackedScene) -> bool:
	if _catalog == null or scene == null:
		return false
	var definition := QuickLevelKitDefinition.new()
	definition.level_id = level_id
	definition.scene = scene
	_catalog.levels.append(definition)
	refresh()
	return true


func remove_level(index: int) -> bool:
	if _catalog == null or index < 0 or index >= _catalog.get_level_count():
		return false
	_catalog.levels.remove_at(index)
	refresh()
	return true


func move_level(index: int, delta: int) -> bool:
	if _catalog == null:
		return false
	var target := index + delta
	if index < 0 or index >= _catalog.get_level_count():
		return false
	if target < 0 or target >= _catalog.get_level_count():
		return false
	var definition := _catalog.levels[index]
	_catalog.levels.remove_at(index)
	_catalog.levels.insert(target, definition)
	refresh()
	return true


func create_default_flow_for(index: int) -> QuickLevelKitFlow:
	if _catalog == null or index < 0 or index >= _catalog.get_level_count():
		return null
	var flow: QuickLevelKitFlow = QuickLevelKitDefaultFlow.create()
	_catalog.levels[index].flow = flow
	refresh()
	return flow


func _build_ui() -> void:
	var layout := VBoxContainer.new()
	layout.set_anchors_preset(Control.PRESET_FULL_RECT)
	add_child(layout)

	var title := Label.new()
	title.text = "Quick Level Kit Catalog"
	layout.add_child(title)

	_level_list = ItemList.new()
	_level_list.custom_minimum_size = Vector2(0, 160)
	layout.add_child(_level_list)

	var buttons := HBoxContainer.new()
	layout.add_child(buttons)
	buttons.add_child(_make_button("Up", func() -> void:
		var selected := _level_list.get_selected_items()
		if not selected.is_empty():
			move_level(selected[0], -1)
	))
	buttons.add_child(_make_button("Down", func() -> void:
		var selected := _level_list.get_selected_items()
		if not selected.is_empty():
			move_level(selected[0], 1)
	))
	buttons.add_child(_make_button("Remove", func() -> void:
		var selected := _level_list.get_selected_items()
		if not selected.is_empty():
			remove_level(selected[0])
	))
	buttons.add_child(_make_button("Flow", func() -> void:
		var selected := _level_list.get_selected_items()
		if not selected.is_empty():
			create_default_flow_for(selected[0])
	))
	buttons.add_child(_make_button("Validate", func() -> void:
		validate_catalog()
	))

	_status_label = Label.new()
	layout.add_child(_status_label)
	_error_label = Label.new()
	_error_label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	layout.add_child(_error_label)


func _make_button(text: String, callback: Callable) -> Button:
	var button := Button.new()
	button.text = text
	button.pressed.connect(callback)
	return button
