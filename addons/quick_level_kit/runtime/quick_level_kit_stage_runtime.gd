class_name QuickLevelKitStageRuntime
extends RefCounted

var stage: QuickLevelKitStage
var session: QuickLevelKitSession
var state: Dictionary = {}


func _init(p_stage: QuickLevelKitStage, p_session: QuickLevelKitSession) -> void:
	stage = p_stage
	session = p_session
