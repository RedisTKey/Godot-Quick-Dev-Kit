using System;
using Godot;
using GDictionary = Godot.Collections.Dictionary;

namespace QuickDev.Database.Tests;

public partial class DatabaseTests
{
    private void RunDocumentTests()
    {
        Test("document/base defaults", () =>
        {
            using var document = new QuickDatabaseDocument();
            Equal(document.GetSchemaVersion(), 1, "default schema");
            Equal(document.ToDictionary().Count, 0, "default serialization");
            document.FromDictionary(Payload(9));
            Equal(document.ToDictionary().Count, 0, "default deserialization is no-op");
            using var copy = document.DuplicateDocument();
            True(copy != null && copy.GetType() == typeof(QuickDatabaseDocument), "base duplication");
        });
        Test("document/round trip and overridden schema", () =>
        {
            using var original = new TestDocument { Count = 3, Label = "hello", Ratios = new GDictionary { ["a"] = 0.5 } };
            using var restored = new TestDocument();
            restored.FromDictionary(original.ToDictionary());
            Equal(original.GetSchemaVersion(), 7, "overridden schema");
            Equal(restored.Count, 3, "count round trip");
            Equal(restored.Label, "hello", "label round trip");
            Equal(restored.Ratios["a"].AsDouble(), 0.5, "dictionary round trip");
        });
        Test("document/duplicate preserves exact runtime type and nested isolation", () =>
        {
            using var original = new TestDocument { Count = 1, Ratios = new GDictionary { ["a"] = 0.5 } };
            using var copy = original.DuplicateDocument();
            True(copy.GetType() == typeof(TestDocument), "duplicate exact derived type");
            var typed = (TestDocument)copy;
            Equal(typed.Count, 1, "duplicate scalar");
            typed.Count = 99;
            typed.Ratios["a"] = 9.0;
            Equal(original.Count, 1, "original scalar isolation");
            Equal(original.Ratios["a"].AsDouble(), 0.5, "original nested isolation");
        });
        Test("document/library isolates dictionaries even with reference-retaining serializers", () =>
        {
            using var original = new ReferenceDocument
            {
                Data = new GDictionary { ["nested"] = new GDictionary { ["items"] = new Godot.Collections.Array { 1, 2 } } }
            };
            using var copy = (ReferenceDocument)original.DuplicateDocument();
            copy.Data["nested"].AsGodotDictionary()["items"].AsGodotArray()[0] = 99;
            Equal(original.Data["nested"].AsGodotDictionary()["items"].AsGodotArray()[0].AsInt32(), 1, "array within nested dictionary isolated");
        });
        Test("document/invalid and missing fields fall back individually", () =>
        {
            using var document = new TestDocument();
            document.FromDictionary(new GDictionary { ["count"] = "not-an-int", ["label"] = 42, ["ratios"] = "bad" });
            Equal(document.Count, 0, "invalid int fallback");
            Equal(document.Label, "untitled", "invalid string fallback");
            Equal(document.Ratios.Count, 0, "invalid dictionary fallback");
            document.FromDictionary(Payload(5));
            Equal(document.Count, 5, "valid partial int");
            Equal(document.Label, "untitled", "missing label fallback");
            document.FromDictionary(new GDictionary { ["count"] = 2.5, ["label"] = "valid" });
            Equal(document.Count, 0, "float is not int");
            Equal(document.Label, "valid", "valid field survives bad neighbor");
        });
    }

    private void RunRepositoryTests()
    {
        Test("repository/missing file uses defaults without writing", () =>
        {
            var path = NewPath("repository-missing");
            using var repository = new QuickDatabaseRepository(() => new TestDocument(), path);
            using var document = (TestDocument)repository.LoadDocument();
            Equal(document.Count, 0, "missing count");
            Equal(document.Label, "untitled", "missing label");
            Equal(repository.GetLastLoadStatus(), QuickDatabaseStore.LoadStatus.Missing, "missing status");
            False(Godot.FileAccess.FileExists(path), "missing load does not write");
        });
        Test("repository/round trip factory identity path and default store", () =>
        {
            var path = NewPath("repository-roundtrip");
            Func<QuickDatabaseDocument> factory = () => new TestDocument();
            using var repository = new QuickDatabaseRepository(factory, path);
            using var original = new TestDocument { Count = 4, Label = "saved" };
            Equal(repository.SaveDocument(original), Error.Ok, "repository save");
            using var reloader = new QuickDatabaseRepository(factory, path);
            using var loaded = (TestDocument)reloader.LoadDocument();
            Equal(loaded.Count, 4, "saved count");
            Equal(loaded.Label, "saved", "saved label");
            Equal(reloader.GetLastLoadStatus(), QuickDatabaseStore.LoadStatus.Loaded, "loaded status");
            True(ReferenceEquals(reloader.GetDocumentFactory(), factory), "retained exact factory");
            Equal(repository.GetSavePath(), path, "retained path");
            True(repository.GetStore() is QuickDatabaseConfigFileStore, "default store type");
            using var defaults = new QuickDatabaseRepository(factory);
            Equal(defaults.GetSavePath(), "", "empty path left for store to resolve");
        });
        Test("repository/unknown schema returns defaults and backup diagnostics", () =>
        {
            var path = NewPath("repository-schema");
            Seed(path, 8, 99);
            using var repository = new QuickDatabaseRepository(() => new TestDocument(), path);
            using var document = (TestDocument)repository.LoadDocument();
            Equal(document.Count, 0, "unknown schema defaults");
            Equal(repository.GetLastLoadStatus(), QuickDatabaseStore.LoadStatus.RecoveredUnknownSchema, "unknown schema status");
            True(repository.GetLastBackupPath().EndsWith(".corrupt.cfg", StringComparison.Ordinal), "backup forwarded");
            True(Godot.FileAccess.FileExists(repository.GetLastBackupPath()), "backup exists");
            False(repository.IsRecoveryPending(), "successful recovery not pending");
            Equal(repository.GetLastRecoveryError(), Error.Ok, "recovery succeeded");
        });
        Test("repository/custom store receives path schema and payload", () =>
        {
            using var store = new MemoryStore { Result = new GDictionary { ["payload"] = Payload(3) } };
            using var repository = new QuickDatabaseRepository(() => new TestDocument(), "custom://slot", store);
            True(ReferenceEquals(repository.GetStore(), store), "custom store identity");
            using var document = (TestDocument)repository.LoadDocument();
            Equal(document.Count, 3, "custom payload loaded");
            Equal(store.LastSchema, 7, "factory schema passed to load");
            Equal(store.LastPath, "custom://slot", "path passed to load");
            document.Count = 12;
            Equal(repository.SaveDocument(document), Error.Ok, "custom save");
            Equal(store.LastSchema, 7, "schema passed to save");
            Equal(store.Saved["count"].AsInt32(), 12, "save payload passed");
            Equal(repository.SaveDocument(null!), Error.InvalidData, "null document rejected");
            Equal(store.Saves, 1, "null save never reaches store");
        });
        Test("repository/custom malformed result payloads preserve defaults", () =>
        {
            using var store = new MemoryStore();
            using var repository = new QuickDatabaseRepository(() => new TestDocument(), "", store);
            foreach (var result in new[] { new GDictionary(), new GDictionary { ["payload"] = 42 }, new GDictionary { ["payload"] = new Godot.Collections.Array() } })
            {
                store.Result = result;
                using var document = (TestDocument)repository.LoadDocument();
                Equal(document.Count, 0, "malformed payload count defaults");
                Equal(document.Label, "untitled", "malformed payload label defaults");
            }
        });
        Test("repository/deep hydration isolation from custom store result", () =>
        {
            var raw = new GDictionary { ["nested"] = new GDictionary { ["count"] = 3 } };
            using var store = new MemoryStore { Result = new GDictionary { ["payload"] = raw } };
            using var repository = new QuickDatabaseRepository(() => new ReferenceDocument(), "", store);
            using var loaded = (ReferenceDocument)repository.LoadDocument();
            loaded.Data["nested"].AsGodotDictionary()["count"] = 99;
            Equal(raw["nested"].AsGodotDictionary()["count"].AsInt32(), 3, "repository isolates caller-owned payload");
        });
        Test("repository/blocked corruption and transaction diagnostics forwarded", () =>
        {
            var path = NewPath("repository-blocked");
            WriteText(path, "[invalid");
            using var store = new FaultStore { BackupFailures = 1 };
            using var repository = new QuickDatabaseRepository(() => new TestDocument(), path, store);
            using var defaults = repository.LoadDocument();
            True(repository.IsRecoveryPending(), "corruption pending forwarded");
            Equal(repository.GetLastRecoveryError(), Error.Busy, "corruption error forwarded");
            Equal(repository.GetLastLoadStatus(), QuickDatabaseStore.LoadStatus.RecoveryBlockedCorrupt, "corruption status forwarded");
            var transactionPath = NewPath("repository-transaction");
            Seed(transactionPath, 2);
            Equal(DirAccess.RenameAbsolute(Absolute(transactionPath), Absolute(transactionPath) + ".previous"), Error.Ok, "create interrupted fixture");
            using var transactionStore = new FaultStore { RestoreFailures = 1 };
            using var transactionRepository = new QuickDatabaseRepository(() => new TestDocument(), transactionPath, transactionStore);
            using var transactionDefaults = transactionRepository.LoadDocument();
            True(transactionRepository.IsTransactionRecoveryPending(), "transaction pending forwarded");
            Equal(transactionRepository.GetLastTransactionRecoveryError(), Error.Busy, "transaction error forwarded");
        });
    }
}
