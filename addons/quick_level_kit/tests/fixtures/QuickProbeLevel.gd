extends QuickLevelKitLevel


var prepared := false


func _prepare_level() -> void:
	prepared = true
	complete_preparation()
