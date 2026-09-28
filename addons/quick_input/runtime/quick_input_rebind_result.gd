class_name QuickInputRebindResult
extends RefCounted

enum Status { READY, CONFLICT, INVALID, UNSUPPORTED }

var status := Status.UNSUPPORTED
var message := ""
var conflicts: Array[QuickInputBinding] = []
var transaction: QuickInputBindingTransaction
