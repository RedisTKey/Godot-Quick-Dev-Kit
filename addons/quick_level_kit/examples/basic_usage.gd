extends Node

@onready var _status: Label = $Status


func _ready() -> void:
	var manager := get_node_or_null("/root/QuickLevelKit") as QuickLevelKitManager
	if manager == null:
		_status.text = "Enable the Quick Level Kit plugin to register the QuickLevelKit Autoload."
		return
	var catalog := QuickLevelKitCatalog.new()
	var definition := QuickLevelKitDefinition.new()
	definition.level_id = &"demo_001"
	definition.scene = load("res://addons/quick_level_kit/tests/fixtures/QuickProbeLevel.tscn")
	catalog.levels = [definition]
	catalog.default_flow = QuickLevelKitDefaultFlow.create()
	manager.set_catalog(catalog)
	manager.load_first_level()
	_status.text = "Loaded: %s @ %s" % [
		manager.get_current_level_id(),
		manager.get_current_stage_id(),
	]
