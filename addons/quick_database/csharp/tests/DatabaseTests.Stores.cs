using System;
using System.IO;
using Godot;
using GDictionary = Godot.Collections.Dictionary;
using GFile = Godot.FileAccess;

namespace QuickDev.Database.Tests;

public partial class DatabaseTests
{
    private void RunStoreTests()
    {
        Test("store/base extension contract", () =>
        {
            using var store = new QuickDatabaseStore();
            EmptyPayload(store.LoadStore("unused", 1), QuickDatabaseStore.LoadStatus.Missing);
            Equal(store.SaveStore("unused", 1, Payload(1)), Error.Ok, "base save is no-op");
            Equal(store.EraseStore("unused"), Error.Ok, "base erase is no-op");
            Equal(store.GetLastLoadStatus(), QuickDatabaseStore.LoadStatus.Missing, "base status");
            Equal(store.GetLastBackupPath(), "", "base backup");
            False(store.IsRecoveryPending(), "base recovery flag");
            False(store.IsTransactionRecoveryPending(), "base transaction flag");
            Equal(store.GetLastRecoveryError(), Error.Ok, "base recovery error");
            Equal(store.GetLastTransactionRecoveryError(), Error.Ok, "base transaction error");
        });
        Test("store/missing read does not write", () =>
        {
            var path = NewPath("missing");
            using var store = new QuickDatabaseConfigFileStore();
            EmptyPayload(store.LoadStore(path, 7), QuickDatabaseStore.LoadStatus.Missing);
            Equal(store.GetLastLoadStatus(), QuickDatabaseStore.LoadStatus.Missing, "missing diagnostic");
            False(GFile.FileExists(path), "missing read creates no canonical file");
            False(GFile.FileExists(Absolute(path) + ".previous"), "missing read creates no previous file");
        });
        Test("store/nested round trip and file serialization", () =>
        {
            var path = NewPath("round-trip");
            var data = new GDictionary { ["count"] = 3, ["nested"] = new GDictionary { ["flag"] = true, ["items"] = new Godot.Collections.Array { 4, "x" } } };
            using var store = new QuickDatabaseConfigFileStore();
            Equal(store.SaveStore(path, 7, data), Error.Ok, "save succeeds");
            data["nested"].AsGodotDictionary()["flag"] = false;
            var result = store.LoadStore(path, 7);
            Equal(store.GetLastLoadStatus(), QuickDatabaseStore.LoadStatus.Loaded, "loaded diagnostic");
            Equal(result["status"].AsInt32(), (int)QuickDatabaseStore.LoadStatus.Loaded, "loaded result status");
            Equal(Count(result), 3, "scalar round trip");
            True(ResultPayload(result)["nested"].AsGodotDictionary()["flag"].AsBool(), "saved payload independent from caller");
            Equal(ResultPayload(result)["nested"].AsGodotDictionary()["items"].AsGodotArray()[1].AsString(), "x", "array round trip");
            using var config = new ConfigFile();
            Equal(config.Load(path), Error.Ok, "native ConfigFile interoperability");
            Equal(config.GetValue("meta", "schema_version").AsInt32(), 7, "schema layout");
            Equal(config.GetValue("data", "payload").AsGodotDictionary()["count"].AsInt32(), 3, "payload layout");
            NoTemporaryFiles(path);
        });
        Test("store/unknown schema is backed up", () =>
        {
            var path = NewPath("schema");
            Seed(path, 1, 999);
            var bytes = GFile.GetFileAsString(path);
            using var store = new QuickDatabaseConfigFileStore();
            EmptyPayload(store.LoadStore(path, 7), QuickDatabaseStore.LoadStatus.RecoveredUnknownSchema);
            Equal(store.GetLastLoadStatus(), QuickDatabaseStore.LoadStatus.RecoveredUnknownSchema, "schema diagnostic");
            False(GFile.FileExists(path), "unknown schema canonical moved");
            AssertBackup(store, bytes);
        });
        Test("store/noninteger and absent schemas never coerce", () =>
        {
            Variant?[] schemas = { "7", 7.0, true, null };
            foreach (var schema in schemas)
            {
                var path = NewPath("schema-type");
                WriteConfig(path, schema, Payload(6));
                using var store = new QuickDatabaseConfigFileStore();
                EmptyPayload(store.LoadStore(path, 7), QuickDatabaseStore.LoadStatus.RecoveredUnknownSchema);
                AssertBackup(store);
                False(GFile.FileExists(path), "invalid schema source moved");
            }
        });
        Test("store/invalid payload shapes yield empty loaded payload", () =>
        {
            Variant?[] payloads = { "not-a-dictionary", 42, new Godot.Collections.Array { 1 }, null };
            foreach (var payload in payloads)
            {
                var path = NewPath("payload-type");
                WriteConfig(path, 7, payload);
                using var store = new QuickDatabaseConfigFileStore();
                EmptyPayload(store.LoadStore(path, 7), QuickDatabaseStore.LoadStatus.Loaded);
                True(GFile.FileExists(path), "bad payload does not destroy readable schema-matching file");
                Equal(store.GetLastBackupPath(), "", "bad payload alone is not corruption");
            }
        });
        Test("store/malformed configuration is backed up byte for byte", () =>
        {
            var path = NewPath("corrupt");
            const string contents = "[this is not valid";
            WriteText(path, contents);
            using var store = new QuickDatabaseConfigFileStore();
            EmptyPayload(store.LoadStore(path, 7), QuickDatabaseStore.LoadStatus.RecoveredCorrupt);
            False(GFile.FileExists(path), "corrupt canonical moved");
            AssertBackup(store, contents);
            store.LoadStore(NewPath("clean-missing"), 7);
            Equal(store.GetLastBackupPath(), "", "later load clears stale backup diagnostic");
        });
        Test("store/creates missing parent directories", () =>
        {
            var path = NewPath("nested") + "/a/b/save.cfg";
            using var store = new QuickDatabaseConfigFileStore();
            Equal(store.SaveStore(path, 7, Payload(3)), Error.Ok, "recursive parent creation");
            AssertSaved(path, 3);
        });
        Test("store/invalid parent paths fail without clobbering existing bytes", () =>
        {
            var parent = NewPath("parent-is-file");
            WriteText(parent, "keep me");
            using var store = new QuickDatabaseConfigFileStore();
            True(store.SaveStore(parent + "/child.cfg", 7, Payload(4)) != Error.Ok, "file parent cannot become directory");
            Equal(GFile.GetFileAsString(parent), "keep me", "parent file untouched");
            var directory = Absolute(NewPath("target-is-directory"));
            Directory.CreateDirectory(directory);
            True(store.SaveStore(directory, 7, Payload(4)) != Error.Ok, "directory cannot become canonical file");
            True(Directory.Exists(directory), "target directory survives");
        });
        Test("store/schema preserves full GDScript 64-bit integer range", () =>
        {
            var path = NewPath("large-schema");
            const long schema = 4_294_967_303L;
            using var store = new QuickDatabaseConfigFileStore();
            Equal(store.SaveStore(path, schema, Payload(42)), Error.Ok, "large schema save");
            Equal(Count(store.LoadStore(path, schema)), 42, "large schema matches exactly");
            Equal(store.GetLastLoadStatus(), QuickDatabaseStore.LoadStatus.Loaded, "large schema accepted");
        });
        Test("store/rapid corrupt backups never overwrite earlier backups", () =>
        {
            var path = NewPath("rapid-corruption");
            var backups = new System.Collections.Generic.HashSet<string>();
            using var store = new QuickDatabaseConfigFileStore();
            for (var index = 0; index < 16; index++)
            {
                var contents = $"[corrupt iteration {index}";
                WriteText(path, contents);
                store.LoadStore(path, 7);
                AssertBackup(store, contents);
                True(backups.Add(store.GetLastBackupPath()), "each corruption backup is uniquely named");
            }
            foreach (var backup in backups) True(GFile.FileExists(backup), "all earlier backups remain");
        });
        Test("store/partial temporary write failure cleans its orphan file", () =>
        {
            var path = NewPath("partial-write-failure");
            Seed(path, 1);
            using var store = new FaultStore { SaveFailures = 1, WritePartialBeforeSaveFailure = true };
            Equal(store.SaveStore(path, 7, Payload(2)), Error.CantCreate, "partial failure returns original write error");
            AssertSaved(path, 1);
            NoTemporaryFiles(path);
        });
        Test("store/temporary write failure preserves previous canonical", () =>
        {
            var path = NewPath("save-failure");
            Seed(path, 1);
            using var store = new FaultStore { SaveFailures = 1 };
            Equal(store.SaveStore(path, 7, Payload(2)), Error.CantCreate, "save failure propagated");
            AssertSaved(path, 1);
            False(store.Calls.Contains("preserve-canonical"), "write failure never renames canonical");
            NoTemporaryFiles(path);
        });
        Test("store/temporary verification failure preserves canonical and cleans temp", () =>
        {
            var path = NewPath("verify-failure");
            Seed(path, 1);
            using var store = new FaultStore { VerifyFailures = 1 };
            Equal(store.SaveStore(path, 7, Payload(2)), Error.FileCorrupt, "verification failure propagated");
            AssertSaved(path, 1);
            Sequence(store.Calls, "save-temp", "verify-temp", "remove-temp");
            NoTemporaryFiles(path);
        });
        Test("store/first promotion failure leaves no canonical or temp", () =>
        {
            var path = NewPath("new-promote-failure");
            using var store = new FaultStore { PromoteFailures = 1 };
            Equal(store.SaveStore(path, 7, Payload(2)), Error.CantCreate, "new promotion failure propagated");
            False(GFile.FileExists(path), "failed new save absent");
            NoTemporaryFiles(path);
        });
        Test("store/preserve rename failure keeps canonical and cleans temp", () =>
        {
            var path = NewPath("preserve-failure");
            Seed(path, 1);
            using var store = new FaultStore { PreserveFailures = 1 };
            Equal(store.SaveStore(path, 7, Payload(2)), Error.Busy, "preserve failure propagated");
            AssertSaved(path, 1);
            False(GFile.FileExists(Absolute(path) + ".previous"), "no previous produced");
            NoTemporaryFiles(path);
        });
        Test("store/replacement promotion failure rolls back previous", () =>
        {
            var path = NewPath("promote-failure");
            Seed(path, 1);
            using var store = new FaultStore { PromoteFailures = 1 };
            Equal(store.SaveStore(path, 7, Payload(2)), Error.CantCreate, "replace promotion failure propagated");
            AssertSaved(path, 1);
            Sequence(store.Calls, "save-temp", "verify-temp", "preserve-canonical", "promote-temp", "restore-previous", "remove-temp");
            False(GFile.FileExists(Absolute(path) + ".previous"), "rollback consumed previous");
            NoTemporaryFiles(path);
        });
        Test("store/double rename failure recovers on next load", () =>
        {
            var path = NewPath("interrupted");
            Seed(path, 1);
            using var failing = new FaultStore { PromoteFailures = 1, RestoreFailures = 1 };
            Equal(failing.SaveStore(path, 7, Payload(2)), Error.Busy, "restore error takes priority");
            False(GFile.FileExists(path), "interrupted canonical absent");
            True(GFile.FileExists(Absolute(path) + ".previous"), "interrupted previous survives");
            True(failing.IsTransactionRecoveryPending(), "failed restoration pending");
            Equal(failing.GetLastTransactionRecoveryError(), Error.Busy, "failed restoration diagnostic");
            using var recovery = new QuickDatabaseConfigFileStore();
            var result = recovery.LoadStore(path, 7);
            Equal(Count(result), 1, "recovered previous value");
            Equal(recovery.GetLastLoadStatus(), QuickDatabaseStore.LoadStatus.RecoveredInterruptedTransaction, "recovered transaction status");
            False(GFile.FileExists(Absolute(path) + ".previous"), "recovery consumes previous");
            NoTemporaryFiles(path);
        });
        Test("store/cleanup failure reports successful committed save", () =>
        {
            var path = NewPath("cleanup-failure");
            Seed(path, 1);
            using var failing = new FaultStore { PreviousRemoveFailures = 1 };
            Equal(failing.SaveStore(path, 7, Payload(2)), Error.Ok, "committed save succeeds despite cleanup failure");
            True(GFile.FileExists(Absolute(path) + ".previous"), "failed cleanup leaves previous");
            using var reader = new QuickDatabaseConfigFileStore();
            Equal(Count(reader.LoadStore(path, 7)), 2, "canonical wins over stale previous");
            Equal(reader.GetLastLoadStatus(), QuickDatabaseStore.LoadStatus.Loaded, "cleanup-only is loaded");
            False(GFile.FileExists(Absolute(path) + ".previous"), "next read cleans stale previous");
        });
        Test("store/blocked interrupted restore refuses overwrite and retries", () =>
        {
            var path = NewPath("blocked-restore");
            Seed(path, 1);
            Equal(DirAccess.RenameAbsolute(Absolute(path), Absolute(path) + ".previous"), Error.Ok, "interrupt transaction");
            using var store = new FaultStore { RestoreFailures = 2 };
            EmptyPayload(store.LoadStore(path, 7), QuickDatabaseStore.LoadStatus.TransactionRecoveryBlocked);
            True(store.IsTransactionRecoveryPending(), "restoration blocked");
            Equal(store.SaveStore(path, 7, Payload(9)), Error.Busy, "blocked restore prevents save");
            False(GFile.FileExists(path), "save cannot overwrite unrecovered transaction");
            True(GFile.FileExists(Absolute(path) + ".previous"), "previous remains available");
            Equal(store.SaveStore(path, 7, Payload(9)), Error.Ok, "save retries recovery");
            False(store.IsTransactionRecoveryPending(), "retry clears transaction block");
            Equal(store.GetLastTransactionRecoveryError(), Error.Ok, "retry clears transaction error");
            AssertSaved(path, 9);
        });
        Test("store/blocked stale previous cleanup loads canonical but blocks saves", () =>
        {
            var path = NewPath("blocked-cleanup");
            Seed(path, 2);
            Seed(path + ".previous", 1);
            using var store = new FaultStore { PreviousRemoveFailures = 2 };
            Equal(Count(store.LoadStore(path, 7)), 2, "canonical readable during cleanup block");
            Equal(store.GetLastLoadStatus(), QuickDatabaseStore.LoadStatus.TransactionRecoveryBlocked, "cleanup block status");
            True(store.IsTransactionRecoveryPending(), "cleanup block pending");
            Equal(store.SaveStore(path, 7, Payload(3)), Error.Busy, "save cannot skip prior cleanup");
            using var config = new ConfigFile();
            Equal(config.Load(path), Error.Ok, "blocked canonical still readable");
            Equal(config.GetValue("data", "payload").AsGodotDictionary()["count"].AsInt32(), 2, "blocked save preserves canonical");
            Equal(store.SaveStore(path, 7, Payload(3)), Error.Ok, "later cleanup succeeds");
            AssertSaved(path, 3);
        });
        Test("store/blocked corruption preserves bytes until backup succeeds", () =>
        {
            var path = NewPath("blocked-corrupt");
            const string contents = "[still corrupt";
            WriteText(path, contents);
            using var store = new FaultStore { BackupFailures = 2 };
            EmptyPayload(store.LoadStore(path, 7), QuickDatabaseStore.LoadStatus.RecoveryBlockedCorrupt);
            True(store.IsRecoveryPending(), "corruption pending");
            Equal(store.GetLastRecoveryError(), Error.Busy, "corruption error");
            Equal(store.SaveStore(path, 7, Payload(1)), Error.Busy, "blocked save refuses overwrite");
            Equal(GFile.GetFileAsString(path), contents, "corrupt source preserved");
            Equal(store.SaveStore(path, 7, Payload(1)), Error.Ok, "backup retry succeeds");
            False(store.IsRecoveryPending(), "successful retry clears corruption flag");
            Equal(store.GetLastRecoveryError(), Error.Ok, "successful retry clears corruption error");
            AssertBackup(store, contents);
            AssertSaved(path, 1);
        });
        Test("store/blocked unknown schema preserves bytes", () =>
        {
            var path = NewPath("blocked-schema");
            Seed(path, 4, 999);
            var contents = GFile.GetFileAsString(path);
            using var store = new FaultStore { BackupFailures = 2 };
            EmptyPayload(store.LoadStore(path, 7), QuickDatabaseStore.LoadStatus.RecoveryBlockedUnknownSchema);
            Equal(store.SaveStore(path, 7, Payload(5)), Error.Busy, "schema block prevents overwrite");
            Equal(GFile.GetFileAsString(path), contents, "unknown schema preserved");
            Equal(store.SaveStore(path, 7, Payload(5)), Error.Ok, "schema retry");
            AssertBackup(store, contents);
        });
        Test("store/shared instance keeps pending corruption per canonical path", () =>
        {
            var pathA = NewPath("pending-a");
            var pathB = NewPath("independent-b");
            const string contents = "[blocked A";
            WriteText(pathA, contents);
            using var store = new FaultStore { BackupFailures = 2 };
            store.LoadStore(pathA, 7);
            True(store.IsRecoveryPending(), "A initially pending");
            EmptyPayload(store.LoadStore(pathB, 7), QuickDatabaseStore.LoadStatus.Missing);
            Equal(store.SaveStore(pathB, 7, Payload(20)), Error.Ok, "unrelated B save allowed");
            Equal(store.SaveStore(Absolute(pathA), 7, Payload(10)), Error.Busy, "A pending survives B operation and absolute alias");
            Equal(GFile.GetFileAsString(pathA), contents, "A cannot silently overwrite corruption");
            Equal(store.SaveStore(pathA, 7, Payload(10)), Error.Ok, "A eventually retries backup");
            AssertBackup(store, contents);
            AssertSaved(pathA, 10);
            AssertSaved(pathB, 20);
        });
        Test("store/erase removes canonical and previous without resurrection", () =>
        {
            var path = NewPath("erase");
            Seed(path, 2);
            Seed(path + ".previous", 1);
            using var store = new FaultStore();
            Equal(store.EraseStore(path), Error.Ok, "erase succeeds");
            Sequence(store.Calls, "remove-previous", "remove-canonical");
            False(GFile.FileExists(path), "canonical erased");
            False(GFile.FileExists(Absolute(path) + ".previous"), "previous erased");
            EmptyPayload(store.LoadStore(path, 7), QuickDatabaseStore.LoadStatus.Missing);
            Equal(store.EraseStore(path), Error.Ok, "erase missing is idempotent");
        });
        Test("store/erase previous failure preserves canonical", () =>
        {
            var path = NewPath("erase-previous-failure");
            Seed(path, 2);
            Seed(path + ".previous", 1);
            using var store = new FaultStore { PreviousRemoveFailures = 1 };
            Equal(store.EraseStore(path), Error.Busy, "previous erase failure propagated");
            True(GFile.FileExists(path), "canonical retained when previous cannot be removed");
            True(GFile.FileExists(path + ".previous"), "previous retained on removal failure");
            False(store.Calls.Contains("remove-canonical"), "erase aborts before canonical deletion");
            AssertSaved(path, 2);
        });
        Test("store/erase canonical failure does not leave stale previous", () =>
        {
            var path = NewPath("erase-canonical-failure");
            Seed(path, 2);
            Seed(path + ".previous", 1);
            using var store = new FaultStore { CanonicalRemoveFailures = 1 };
            Equal(store.EraseStore(path), Error.Busy, "canonical erase failure propagated");
            True(GFile.FileExists(path), "canonical survives failed delete");
            False(GFile.FileExists(path + ".previous"), "old previous removed first");
            AssertSaved(path, 2);
            Equal(store.EraseStore(path), Error.Ok, "erase retry");
        });
        Test("store/erasing blocked corruption clears only that path", () =>
        {
            var pathA = NewPath("erase-pending-a");
            var pathB = NewPath("erase-pending-b");
            WriteText(pathA, "[bad A");
            WriteText(pathB, "[bad B");
            using var store = new FaultStore { BackupFailures = 3 };
            store.LoadStore(pathA, 7);
            store.LoadStore(pathB, 7);
            Equal(store.EraseStore(pathA), Error.Ok, "explicit erase clears A");
            Equal(store.SaveStore(pathA, 7, Payload(1)), Error.Ok, "A can be saved after erase");
            Equal(store.SaveStore(pathB, 7, Payload(2)), Error.Busy, "B remains pending after A erase");
            Equal(GFile.GetFileAsString(pathB), "[bad B", "B bytes preserved");
        });
    }
}
