using System;
using System.Collections.Generic;
using Godot;

namespace QuickDev.Database;

/// <summary>Independent named save slots. Install as /root/QuickDatabase or instantiate explicitly.</summary>
public partial class QuickDatabaseService : Node
{
    [Signal] public delegate void DatabaseLoadedEventHandler(StringName slot, int status);
    [Signal] public delegate void DataChangedEventHandler(StringName slot, StringName field);
    [Signal] public delegate void SaveSucceededEventHandler(StringName slot, string path);
    [Signal] public delegate void SaveFailedEventHandler(StringName slot, string path, Error error);
    [Signal] public delegate void CorruptDatabaseBackedUpEventHandler(StringName slot, string path);
    [Signal] public delegate void DatabaseRecoveryBlockedEventHandler(StringName slot, string path, Error error);
    [Signal] public delegate void DatabaseTransactionRecoveryBlockedEventHandler(StringName slot, string path, Error error);
    [Signal] public delegate void SlotOpenedEventHandler(StringName slot);
    [Signal] public delegate void SlotClosedEventHandler(StringName slot);

    private sealed class Slot
    {
        public required StringName Name;
        public required QuickDatabaseRepository Repository;
        public required QuickDatabaseDocument Document;
        public bool Dirty;
    }

    private readonly Dictionary<StringName, Slot> _slots = new();

    public bool OpenSlot<T>(StringName slot, string path = "", QuickDatabaseStore? store = null)
        where T : QuickDatabaseDocument, new() => OpenSlot(slot, () => new T(), path, store);

    public bool OpenSlot(StringName slot, Func<QuickDatabaseDocument>? documentFactory, string path = "", QuickDatabaseStore? store = null)
    {
        if (slot.IsEmpty || documentFactory is null || _slots.ContainsKey(slot)) return false;
        var repository = new QuickDatabaseRepository(documentFactory, path, store);
        var entry = new Slot { Name = slot, Repository = repository, Document = repository.LoadDocument() };
        _slots.Add(slot, entry);
        EmitLoadSignals(slot, repository);
        EmitSignal(SignalName.SlotOpened, slot);
        return true;
    }

    public bool CloseSlot(StringName slot)
    {
        if (!_slots.TryGetValue(slot, out var entry)) return false;
        if (entry.Dirty && SaveEntry(entry) != Error.Ok) return false;
        // SaveSucceeded is synchronous: a listener may edit or replace this slot.
        // Never discard a new dirty document or close a newly opened replacement.
        if (!_slots.TryGetValue(slot, out var current) || !ReferenceEquals(current, entry) || entry.Dirty) return false;
        _slots.Remove(slot);
        EmitSignal(SignalName.SlotClosed, slot);
        return true;
    }

    public bool HasSlot(StringName slot) => _slots.ContainsKey(slot);
    public Godot.Collections.Array<StringName> GetSlotNames() => new(_slots.Keys);
    public string GetSavePath(StringName slot) => _slots.TryGetValue(slot, out var entry) ? entry.Repository.GetSavePath() : "";
    public bool IsDirty(StringName slot) => _slots.TryGetValue(slot, out var entry) && entry.Dirty;
    public QuickDatabaseDocument? GetDocument(StringName slot) => _slots.TryGetValue(slot, out var entry) ? entry.Document.DuplicateDocument() : null;
    public T? GetDocument<T>(StringName slot) where T : QuickDatabaseDocument => GetDocument(slot) as T;

    public bool UpdateSlot(StringName slot, StringName field, Action<QuickDatabaseDocument> mutator)
    {
        if (!_slots.TryGetValue(slot, out var entry)) return false;
        ArgumentNullException.ThrowIfNull(mutator);
        mutator(entry.Document);
        entry.Dirty = true;
        EmitSignal(SignalName.DataChanged, slot, field);
        return true;
    }

    public Error CommitSlot(StringName slot, StringName field, Action<QuickDatabaseDocument> mutator)
    {
        if (!_slots.TryGetValue(slot, out var entry)) return Error.DoesNotExist;
        ArgumentNullException.ThrowIfNull(mutator);
        var candidate = entry.Document.DuplicateDocument();
        mutator(candidate);
        var error = entry.Repository.SaveDocument(candidate);
        if (error != Error.Ok)
        {
            EmitSignal(SignalName.SaveFailed, slot, entry.Repository.GetSavePath(), (int)error);
            return error;
        }
        entry.Document = candidate;
        entry.Dirty = false;
        EmitSignal(SignalName.DataChanged, slot, field);
        EmitSignal(SignalName.SaveSucceeded, slot, entry.Repository.GetSavePath());
        return Error.Ok;
    }

    public Error FlushSlot(StringName slot) => _slots.TryGetValue(slot, out var entry) ? SaveEntry(entry) : Error.DoesNotExist;

    public Error FlushAll()
    {
        // A signal subscriber may open/close a different slot during a save.
        foreach (var name in new List<StringName>(_slots.Keys))
        {
            if (!_slots.TryGetValue(name, out var entry) || !entry.Dirty) continue;
            var error = SaveEntry(entry);
            if (error != Error.Ok) return error;
        }
        return Error.Ok;
    }

    public bool ReloadSlot(StringName slot)
    {
        if (!_slots.TryGetValue(slot, out var entry)) return false;
        entry.Document = entry.Repository.LoadDocument();
        entry.Dirty = false;
        EmitLoadSignals(slot, entry.Repository);
        return true;
    }

    public Error EraseSlot(StringName slot)
    {
        if (!_slots.TryGetValue(slot, out var entry)) return Error.DoesNotExist;
        var error = entry.Repository.GetStore().EraseStore(entry.Repository.GetSavePath());
        if (error != Error.Ok) return error;
        entry.Document = entry.Repository.LoadDocument();
        entry.Dirty = false;
        return Error.Ok;
    }

    public bool MarkDirty(StringName slot, StringName field)
    {
        if (!_slots.TryGetValue(slot, out var entry)) return false;
        entry.Dirty = true;
        EmitSignal(SignalName.DataChanged, slot, field);
        return true;
    }

    private Error SaveEntry(Slot entry)
    {
        if (!entry.Dirty) return Error.Ok;
        var error = entry.Repository.SaveDocument(entry.Document);
        if (error != Error.Ok)
        {
            EmitSignal(SignalName.SaveFailed, entry.Name, entry.Repository.GetSavePath(), (int)error);
            return error;
        }
        entry.Dirty = false;
        EmitSignal(SignalName.SaveSucceeded, entry.Name, entry.Repository.GetSavePath());
        return Error.Ok;
    }

    private void EmitLoadSignals(StringName slot, QuickDatabaseRepository repository)
    {
        var store = repository.GetStore();
        var backupPath = store.GetLastBackupPath();
        var recoveryPending = store.IsRecoveryPending();
        var recoveryError = store.GetLastRecoveryError();
        var transactionPending = store.IsTransactionRecoveryPending();
        var transactionError = store.GetLastTransactionRecoveryError();
        var status = store.GetLastLoadStatus();
        // A listener can load another slot with the same backend. Keep this load's diagnostics.
        if (!string.IsNullOrEmpty(backupPath)) EmitSignal(SignalName.CorruptDatabaseBackedUp, slot, backupPath);
        if (recoveryPending)
            EmitSignal(SignalName.DatabaseRecoveryBlocked, slot, repository.GetSavePath(), (int)recoveryError);
        if (transactionPending)
            EmitSignal(SignalName.DatabaseTransactionRecoveryBlocked, slot, repository.GetSavePath(), (int)transactionError);
        EmitSignal(SignalName.DatabaseLoaded, slot, (int)status);
    }

    public override void _ExitTree() => FlushAll();
}
