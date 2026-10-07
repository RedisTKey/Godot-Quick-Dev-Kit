using System;
using System.Collections.Generic;
using Godot;
using GDictionary = Godot.Collections.Dictionary;
using GFile = Godot.FileAccess;

namespace QuickDev.Database.Tests;

public partial class DatabaseTests
{
    private static List<string> RecordSignals(QuickDatabaseService service)
    {
        var events = new List<string>();
        service.DatabaseLoaded += (slot, status) => events.Add($"loaded:{slot}:{status}");
        service.DataChanged += (slot, field) => events.Add($"changed:{slot}:{field}");
        service.SaveSucceeded += (slot, path) => events.Add($"saved:{slot}:{path}");
        service.SaveFailed += (slot, path, error) => events.Add($"failed:{slot}:{path}:{error}");
        service.CorruptDatabaseBackedUp += (slot, path) => events.Add($"backup:{slot}:{path}");
        service.DatabaseRecoveryBlocked += (slot, path, error) => events.Add($"blocked:{slot}:{path}:{error}");
        service.DatabaseTransactionRecoveryBlocked += (slot, path, error) => events.Add($"transaction-blocked:{slot}:{path}:{error}");
        service.SlotOpened += slot => events.Add($"opened:{slot}");
        service.SlotClosed += slot => events.Add($"closed:{slot}");
        return events;
    }

    private void RunServiceTests()
    {
        Test("service/open defaults and exact load/open signal order", () =>
        {
            var path = NewPath("service-open");
            var service = NewService();
            var events = RecordSignals(service);
            True(service.OpenSlot("main", () => new TestDocument(), path), "open succeeds");
            False(service.OpenSlot("main", () => throw new Exception("duplicate factory must never run"), path), "duplicate open fails");
            True(service.HasSlot("main"), "slot exists");
            Equal(service.GetSlotNames().Count, 1, "one slot name");
            Equal(service.GetSlotNames()[0].ToString(), "main", "slot name preserved");
            Equal(service.GetSavePath("main"), path, "slot path preserved");
            False(service.IsDirty("main"), "fresh slot clean");
            using var snapshot = Snapshot(service);
            Equal(snapshot.Count, 0, "fresh document defaults");
            Sequence(events, $"loaded:main:{(int)QuickDatabaseStore.LoadStatus.Missing}", "opened:main");
            False(GFile.FileExists(path), "open does not create file");
        });
        Test("service/empty names and null factories rejected without signals", () =>
        {
            var service = NewService();
            var events = RecordSignals(service);
            False(service.OpenSlot("", () => new TestDocument(), NewPath("empty")), "empty name rejected");
            False(service.OpenSlot("main", null, NewPath("null-factory")), "null factory rejected");
            Equal(service.GetSlotNames().Count, 0, "invalid opens create no slot");
            Sequence(events);
        });
        Test("service/missing slot operations are inert and report precise errors", () =>
        {
            var service = NewService();
            var events = RecordSignals(service);
            False(service.HasSlot("missing"), "missing existence");
            Equal(service.GetDocument("missing"), null, "missing snapshot");
            Equal(service.GetSavePath("missing"), "", "missing path");
            False(service.IsDirty("missing"), "missing dirty");
            False(service.CloseSlot("missing"), "missing close");
            False(service.ReloadSlot("missing"), "missing reload");
            False(service.MarkDirty("missing", "field"), "missing mark");
            False(service.UpdateSlot("missing", "field", _ => throw new Exception("must not call missing mutator")), "missing update");
            Equal(service.CommitSlot("missing", "field", _ => throw new Exception("must not call missing mutator")), Error.DoesNotExist, "missing commit");
            Equal(service.FlushSlot("missing"), Error.DoesNotExist, "missing flush");
            Equal(service.EraseSlot("missing"), Error.DoesNotExist, "missing erase");
            Equal(service.FlushAll(), Error.Ok, "empty flush all");
            Sequence(events);
        });
        Test("service/generic factory and isolated snapshot deeply copy nested data", () =>
        {
            var service = NewService();
            True(service.OpenSlot<TestDocument>("main", NewPath("snapshot")), "generic open");
            service.UpdateSlot("main", "nested", document => ((TestDocument)document).Ratios = new GDictionary
            {
                ["nested"] = new GDictionary { ["items"] = new Godot.Collections.Array { 1, 2 } }
            });
            using var snapshot = service.GetDocument<TestDocument>("main");
            True(snapshot != null, "generic get document");
            snapshot!.Count = 99;
            snapshot.Ratios["nested"].AsGodotDictionary()["items"].AsGodotArray()[0] = 99;
            using var second = Snapshot(service);
            Equal(second.Count, 0, "snapshot scalar isolation");
            Equal(second.Ratios["nested"].AsGodotDictionary()["items"].AsGodotArray()[0].AsInt32(), 1, "snapshot nested array isolation");
            var names = service.GetSlotNames();
            names.Clear();
            True(service.HasSlot("main"), "slot name collection is isolated");
        });
        Test("service/update dirty flush signals and disk reload", () =>
        {
            var path = NewPath("service-update");
            var service = NewService();
            service.OpenSlot<TestDocument>("main", path);
            var events = RecordSignals(service);
            True(service.UpdateSlot("main", "count", document => SetCount(document, 5)), "update succeeds");
            True(service.IsDirty("main"), "update dirty");
            False(GFile.FileExists(path), "update is in-memory");
            Sequence(events, "changed:main:count");
            Equal(service.FlushSlot("main"), Error.Ok, "flush succeeds");
            False(service.IsDirty("main"), "flush clears dirty");
            Sequence(events, "changed:main:count", $"saved:main:{path}");
            var reloader = NewService();
            reloader.OpenSlot<TestDocument>("main", path);
            using var loaded = Snapshot(reloader);
            Equal(loaded.Count, 5, "flushed value persists");
        });
        Test("service/clean flush and clean close never save", () =>
        {
            using var store = new MemoryStore();
            var service = NewService();
            service.OpenSlot<TestDocument>("main", "logical-path", store);
            var events = RecordSignals(service);
            Equal(service.FlushSlot("main"), Error.Ok, "clean flush succeeds");
            Equal(service.FlushAll(), Error.Ok, "clean flush all succeeds");
            Equal(store.Saves, 0, "clean flush skips store");
            Sequence(events);
            True(service.CloseSlot("main"), "clean close succeeds");
            Equal(store.Saves, 0, "clean close skips store");
            Sequence(events, "closed:main");
        });
        Test("service/mark dirty carries field payload and persists defaults", () =>
        {
            var path = NewPath("mark-dirty");
            var service = NewService();
            service.OpenSlot<TestDocument>("main", path);
            var events = RecordSignals(service);
            True(service.MarkDirty("main", "settings"), "mark dirty succeeds");
            True(service.IsDirty("main"), "mark dirty sets state");
            Sequence(events, "changed:main:settings");
            Equal(service.FlushSlot("main"), Error.Ok, "mark dirty flush");
            AssertSaved(path, 0);
        });
        Test("service/multi-slot isolation flushes only dirty slots", () =>
        {
            var pathA = NewPath("slot-a");
            var pathB = NewPath("slot-b");
            var service = NewService();
            service.OpenSlot<TestDocument>("a", pathA);
            service.OpenSlot<TestDocument>("b", pathB);
            var events = RecordSignals(service);
            service.UpdateSlot("a", "count", document => SetCount(document, 7));
            True(service.IsDirty("a"), "a dirty");
            False(service.IsDirty("b"), "b clean");
            Equal(service.FlushAll(), Error.Ok, "multi flush");
            False(service.IsDirty("a"), "a clean after flush");
            False(GFile.FileExists(pathB), "clean b never written");
            Sequence(events, "changed:a:count", $"saved:a:{pathA}");
            var reloader = NewService();
            reloader.OpenSlot<TestDocument>("a", pathA);
            reloader.OpenSlot<TestDocument>("b", pathB);
            using var a = Snapshot(reloader, "a");
            using var b = Snapshot(reloader, "b");
            Equal(a.Count, 7, "a persists");
            Equal(b.Count, 0, "b default isolated");
        });
        Test("service/commit success publishes changed then saved", () =>
        {
            var path = NewPath("commit");
            var service = NewService();
            service.OpenSlot<TestDocument>("main", path);
            var events = RecordSignals(service);
            Equal(service.CommitSlot("main", "count", document => SetCount(document, 5)), Error.Ok, "commit succeeds");
            using var snapshot = Snapshot(service);
            Equal(snapshot.Count, 5, "commit replaces live value");
            False(service.IsDirty("main"), "commit is clean");
            Sequence(events, "changed:main:count", $"saved:main:{path}");
            AssertSaved(path, 5);
        });
        Test("service/commit failure retains clean original and emits only failure", () =>
        {
            var path = NewPath("commit-failure");
            using var store = new FaultStore { PromoteFailures = 1 };
            var service = NewService();
            service.OpenSlot<TestDocument>("main", path, store);
            var events = RecordSignals(service);
            Equal(service.CommitSlot("main", "count", document => SetCount(document, 9)), Error.CantCreate, "commit error surfaced");
            using var snapshot = Snapshot(service);
            Equal(snapshot.Count, 0, "failed candidate not installed");
            False(service.IsDirty("main"), "clean original stays clean");
            Sequence(events, $"failed:main:{path}:{Error.CantCreate}");
        });
        Test("service/failed commit retains dirty state and all nested unsaved edits", () =>
        {
            var path = NewPath("dirty-rollback");
            Seed(path, 1);
            using var store = new FaultStore();
            var service = NewService();
            service.OpenSlot<TestDocument>("main", path, store);
            service.UpdateSlot("main", "draft", document =>
            {
                var value = (TestDocument)document;
                value.Count = 2;
                value.Ratios = new GDictionary { ["items"] = new Godot.Collections.Array { 3 } };
            });
            store.PromoteFailures = 1;
            var events = RecordSignals(service);
            Equal(service.CommitSlot("main", "candidate", document =>
            {
                var value = (TestDocument)document;
                value.Count = 99;
                value.Ratios["items"].AsGodotArray()[0] = 99;
            }), Error.CantCreate, "dirty commit failure");
            using var snapshot = Snapshot(service);
            Equal(snapshot.Count, 2, "unsaved live scalar retained");
            Equal(snapshot.Ratios["items"].AsGodotArray()[0].AsInt32(), 3, "unsaved nested value retained");
            True(service.IsDirty("main"), "failed commit retains dirty state");
            AssertSaved(path, 1);
            Sequence(events, $"failed:main:{path}:{Error.CantCreate}");
            Equal(service.FlushSlot("main"), Error.Ok, "dirty state can subsequently flush");
            AssertSaved(path, 2);
        });
        Test("service/flush failure retains dirty live value and retries", () =>
        {
            var path = NewPath("flush-failure");
            Seed(path, 1);
            using var store = new FaultStore { PromoteFailures = 1 };
            var service = NewService();
            service.OpenSlot<TestDocument>("main", path, store);
            service.UpdateSlot("main", "count", document => SetCount(document, 8));
            var events = RecordSignals(service);
            Equal(service.FlushSlot("main"), Error.CantCreate, "flush error surfaced");
            True(service.IsDirty("main"), "failure retains dirty state");
            using var snapshot = Snapshot(service);
            Equal(snapshot.Count, 8, "failure retains live update");
            AssertSaved(path, 1);
            Sequence(events, $"failed:main:{path}:{Error.CantCreate}");
            Equal(service.FlushSlot("main"), Error.Ok, "flush retry succeeds");
            False(service.IsDirty("main"), "retry clears dirty");
            Sequence(events, $"failed:main:{path}:{Error.CantCreate}", $"saved:main:{path}");
            AssertSaved(path, 8);
        });
        Test("service/flush all stops at failure and retains remaining work", () =>
        {
            using var first = new MemoryStore { SaveError = Error.Busy };
            using var second = new MemoryStore();
            var service = NewService();
            service.OpenSlot<TestDocument>("a", "a", first);
            service.OpenSlot<TestDocument>("b", "b", second);
            service.UpdateSlot("a", "count", document => SetCount(document, 1));
            service.UpdateSlot("b", "count", document => SetCount(document, 2));
            Equal(service.FlushAll(), Error.Busy, "first failure surfaced");
            Equal(first.Saves, 1, "first attempted");
            Equal(second.Saves, 0, "remaining save deferred");
            True(service.IsDirty("a") && service.IsDirty("b"), "remaining dirty flags retained");
            first.SaveError = Error.Ok;
            Equal(service.FlushAll(), Error.Ok, "retry flushes both");
            False(service.IsDirty("a") || service.IsDirty("b"), "all flags clear after retry");
            Equal(second.Saved["count"].AsInt32(), 2, "remaining payload preserved");
        });
        Test("service/reload discards unsaved edits and emits only loaded", () =>
        {
            var path = NewPath("reload");
            var service = NewService();
            service.OpenSlot<TestDocument>("main", path);
            service.UpdateSlot("main", "count", document => SetCount(document, 4));
            service.FlushSlot("main");
            service.UpdateSlot("main", "count", document => SetCount(document, 8));
            var events = RecordSignals(service);
            True(service.ReloadSlot("main"), "reload succeeds");
            using var snapshot = Snapshot(service);
            Equal(snapshot.Count, 4, "reload discards unsaved change");
            False(service.IsDirty("main"), "reload clears dirty");
            Sequence(events, $"loaded:main:{(int)QuickDatabaseStore.LoadStatus.Loaded}");
        });
        Test("service/erase resets document and dirty state without spurious signals", () =>
        {
            var path = NewPath("service-erase");
            Seed(path, 4);
            var service = NewService();
            service.OpenSlot<TestDocument>("main", path);
            service.UpdateSlot("main", "count", document => SetCount(document, 8));
            var events = RecordSignals(service);
            Equal(service.EraseSlot("main"), Error.Ok, "erase succeeds");
            False(GFile.FileExists(path), "erase removes file");
            using var snapshot = Snapshot(service);
            Equal(snapshot.Count, 0, "erase resets default");
            False(service.IsDirty("main"), "erase clears dirty");
            True(service.HasSlot("main"), "erase retains open slot");
            Sequence(events);
        });
        Test("service/erase failure retains dirty document and emits no success", () =>
        {
            using var store = new MemoryStore { EraseError = Error.Busy };
            var service = NewService();
            service.OpenSlot<TestDocument>("main", "logical-path", store);
            service.UpdateSlot("main", "count", document => SetCount(document, 8));
            var events = RecordSignals(service);
            Equal(service.EraseSlot("main"), Error.Busy, "erase failure propagated");
            using var snapshot = Snapshot(service);
            Equal(snapshot.Count, 8, "erase failure retains live value");
            True(service.IsDirty("main"), "erase failure retains dirty");
            Equal(store.Loads, 1, "erase failure does not reload");
            Sequence(events);
        });
        Test("service/close flushes before closed signal and can reopen", () =>
        {
            var path = NewPath("close");
            var service = NewService();
            service.OpenSlot<TestDocument>("main", path);
            service.UpdateSlot("main", "count", document => SetCount(document, 6));
            var events = RecordSignals(service);
            True(service.CloseSlot("main"), "close succeeds");
            False(service.HasSlot("main"), "close removes slot");
            Sequence(events, $"saved:main:{path}", "closed:main");
            True(service.OpenSlot<TestDocument>("main", path), "same name can reopen");
            using var snapshot = Snapshot(service);
            Equal(snapshot.Count, 6, "close persists data for reopen");
            False(service.IsDirty("main"), "reopened slot clean");
        });
        Test("service/failed close retains open dirty slot and retries", () =>
        {
            var path = NewPath("close-failure");
            using var store = new FaultStore { PromoteFailures = 1 };
            var service = NewService();
            service.OpenSlot<TestDocument>("main", path, store);
            service.UpdateSlot("main", "count", document => SetCount(document, 6));
            var events = RecordSignals(service);
            False(service.CloseSlot("main"), "failed flush blocks close");
            True(service.HasSlot("main") && service.IsDirty("main"), "failed close retains open dirty slot");
            Sequence(events, $"failed:main:{path}:{Error.CantCreate}");
            True(service.CloseSlot("main"), "close retry succeeds");
            Sequence(events, $"failed:main:{path}:{Error.CantCreate}", $"saved:main:{path}", "closed:main");
            AssertSaved(path, 6);
        });
        Test("service/recovery backup is announced before loaded and opened", () =>
        {
            var path = NewPath("recovery-signals");
            WriteText(path, "[broken");
            using var store = new QuickDatabaseConfigFileStore();
            var service = NewService();
            var events = RecordSignals(service);
            True(service.OpenSlot<TestDocument>("main", path, store), "recovering open succeeds");
            Sequence(events, $"backup:main:{store.GetLastBackupPath()}", $"loaded:main:{(int)QuickDatabaseStore.LoadStatus.RecoveredCorrupt}", "opened:main");
            AssertBackup(store, "[broken");
        });
        Test("service/reentrant load signals keep the original slot diagnostics", () =>
        {
            var pathA = NewPath("shared-store-signal-a");
            var pathB = NewPath("shared-store-signal-b");
            WriteText(pathA, "[corrupt A");
            using var store = new QuickDatabaseConfigFileStore();
            var service = NewService();
            var events = RecordSignals(service);
            var openedB = false;
            var backupA = "";
            service.CorruptDatabaseBackedUp += (slot, backup) =>
            {
                if (slot.ToString() != "a") return;
                backupA = backup;
                openedB = service.OpenSlot<TestDocument>("b", pathB, store);
            };
            True(service.OpenSlot<TestDocument>("a", pathA, store), "A recovers");
            True(openedB, "backup subscriber opened B on shared store");
            True(GFile.FileExists(backupA), "A backup remains available");
            Sequence(events, $"backup:a:{backupA}", $"loaded:b:{(int)QuickDatabaseStore.LoadStatus.Missing}", "opened:b",
                $"loaded:a:{(int)QuickDatabaseStore.LoadStatus.RecoveredCorrupt}", "opened:a");
        });
        Test("service/corrupt recovery blocked signal includes slot path error", () =>
        {
            var path = NewPath("blocked-signals");
            WriteText(path, "[broken");
            using var store = new FaultStore { BackupFailures = 1 };
            var service = NewService();
            var events = RecordSignals(service);
            True(service.OpenSlot<TestDocument>("main", path, store), "blocked recovery still opens defaults");
            Sequence(events, $"blocked:main:{path}:{Error.Busy}", $"loaded:main:{(int)QuickDatabaseStore.LoadStatus.RecoveryBlockedCorrupt}", "opened:main");
        });
        Test("service/transaction recovery blocked signal precedes loaded/opened", () =>
        {
            var path = NewPath("transaction-signals");
            Seed(path, 1);
            Equal(DirAccess.RenameAbsolute(Absolute(path), Absolute(path) + ".previous"), Error.Ok, "interrupt fixture");
            using var store = new FaultStore { RestoreFailures = 1 };
            var service = NewService();
            var events = RecordSignals(service);
            True(service.OpenSlot<TestDocument>("main", path, store), "transaction-blocked open");
            Sequence(events, $"transaction-blocked:main:{path}:{Error.Busy}", $"loaded:main:{(int)QuickDatabaseStore.LoadStatus.TransactionRecoveryBlocked}", "opened:main");
        });
        Test("service/actual exit tree notification flushes dirty slots", () =>
        {
            var path = NewPath("exit-tree");
            var service = NewService();
            AddChild(service);
            True(service.IsInsideTree(), "service participates in real scene tree");
            service.OpenSlot<TestDocument>("main", path);
            service.UpdateSlot("main", "count", document => SetCount(document, 14));
            var events = RecordSignals(service);
            RemoveChild(service);
            False(service.IsInsideTree(), "service exited tree");
            False(service.IsDirty("main"), "exit-tree flush clears dirty");
            Sequence(events, $"saved:main:{path}");
            AssertSaved(path, 14);
        });
        Test("service/repeated tree attachment and service instances stay independent", () =>
        {
            var path = NewPath("repeated-service");
            for (var index = 1; index <= 4; index++)
            {
                var service = NewService();
                AddChild(service);
                True(service.OpenSlot<TestDocument>("main", path), "each instance opens same name");
                using var before = Snapshot(service);
                Equal(before.Count, index - 1, "new instance reads prior exit flush");
                service.UpdateSlot("main", "count", document => SetCount(document, index));
                RemoveChild(service);
                AssertSaved(path, index);
                AddChild(service);
                False(service.IsDirty("main"), "reattached service retains clean state");
                RemoveChild(service);
                service.Free();
            }
        });
        Test("service/exit flush failure keeps dirty state for later retry", () =>
        {
            var path = NewPath("exit-failure");
            using var store = new FaultStore { PromoteFailures = 1 };
            var service = NewService();
            AddChild(service);
            service.OpenSlot<TestDocument>("main", path, store);
            service.UpdateSlot("main", "count", document => SetCount(document, 15));
            var events = RecordSignals(service);
            RemoveChild(service);
            True(service.IsDirty("main"), "exit failure retains dirty");
            Sequence(events, $"failed:main:{path}:{Error.CantCreate}");
            AddChild(service);
            RemoveChild(service);
            False(service.IsDirty("main"), "second exit retries successfully");
            AssertSaved(path, 15);
        });
        Test("service/close refuses to discard same-slot edits made by save subscriber", () =>
        {
            var path = NewPath("close-reentrant-dirty");
            var service = NewService();
            service.OpenSlot<TestDocument>("main", path);
            service.UpdateSlot("main", "count", document => SetCount(document, 1));
            var shouldRedirty = true;
            service.SaveSucceeded += (slot, _) =>
            {
                if (slot.ToString() != "main" || !shouldRedirty) return;
                shouldRedirty = false;
                service.UpdateSlot("main", "subscriber", document => SetCount(document, 2));
            };
            False(service.CloseSlot("main"), "redirtied entry cannot be closed unsaved");
            True(service.HasSlot("main") && service.IsDirty("main"), "subscriber edit remains open and dirty");
            using var snapshot = Snapshot(service);
            Equal(snapshot.Count, 2, "subscriber edit retained");
            AssertSaved(path, 1);
            True(service.CloseSlot("main"), "later close saves subscriber edit");
            AssertSaved(path, 2);
        });
        Test("service/outer close never removes replacement opened by save subscriber", () =>
        {
            var path = NewPath("close-reentrant-original");
            var replacementPath = NewPath("close-reentrant-replacement");
            var service = NewService();
            service.OpenSlot<TestDocument>("main", path);
            service.UpdateSlot("main", "count", document => SetCount(document, 1));
            var shouldReplace = true;
            var subscriberClosed = false;
            var subscriberOpened = false;
            service.SaveSucceeded += (slot, _) =>
            {
                if (slot.ToString() != "main" || !shouldReplace) return;
                shouldReplace = false;
                subscriberClosed = service.CloseSlot("main");
                subscriberOpened = service.OpenSlot<TestDocument>("main", replacementPath);
                service.UpdateSlot("main", "replacement", document => SetCount(document, 8));
            };
            False(service.CloseSlot("main"), "outer close notices replaced entry");
            True(subscriberClosed && subscriberOpened, "subscriber closes original and opens replacement");
            True(service.HasSlot("main"), "replacement remains open");
            Equal(service.GetSavePath("main"), replacementPath, "replacement path retained");
            using var snapshot = Snapshot(service);
            Equal(snapshot.Count, 8, "replacement value retained");
            True(service.IsDirty("main"), "replacement dirty retained");
            AssertSaved(path, 1);
            True(service.CloseSlot("main"), "explicit replacement close succeeds");
            AssertSaved(replacementPath, 8);
        });
        Test("service/flush all tolerates signal-driven close of another slot", () =>
        {
            using var first = new MemoryStore();
            using var second = new MemoryStore();
            var service = NewService();
            service.OpenSlot<TestDocument>("a", "a", first);
            service.OpenSlot<TestDocument>("b", "b", second);
            service.MarkDirty("a", "field");
            service.MarkDirty("b", "field");
            service.SaveSucceeded += (slot, _) => { if (slot.ToString() == "a") service.CloseSlot("b"); };
            Equal(service.FlushAll(), Error.Ok, "mutated slot enumeration remains valid");
            False(service.HasSlot("b"), "subscriber closed second slot");
            Equal(first.Saves, 1, "first saved once");
            Equal(second.Saves, 1, "second close saved exactly once");
        });
    }
}
