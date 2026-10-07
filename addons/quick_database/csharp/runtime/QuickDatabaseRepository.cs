using System;
using Godot;

namespace QuickDev.Database;

/// <summary>Combines a document factory, a storage backend, and a save path.</summary>
public partial class QuickDatabaseRepository : RefCounted
{
    private readonly Func<QuickDatabaseDocument> _documentFactory;
    private readonly QuickDatabaseStore _store;
    private readonly string _path;

    public QuickDatabaseRepository(Func<QuickDatabaseDocument> documentFactory, string path = "", QuickDatabaseStore? store = null)
    {
        _documentFactory = documentFactory ?? throw new ArgumentNullException(nameof(documentFactory));
        _path = path ?? "";
        _store = store ?? new QuickDatabaseConfigFileStore();
    }

    public Func<QuickDatabaseDocument> GetDocumentFactory() => _documentFactory;
    public QuickDatabaseStore GetStore() => _store;
    public string GetSavePath() => _path;

    public QuickDatabaseDocument LoadDocument()
    {
        var document = _documentFactory() ?? throw new InvalidOperationException("The document factory returned null.");
        var result = _store.LoadStore(_path, document.GetSchemaVersion());
        if (result.TryGetValue("payload", out var payload) && payload.VariantType == Variant.Type.Dictionary)
            document.FromDictionary(payload.AsGodotDictionary().Duplicate(true));
        return document;
    }

    public Error SaveDocument(QuickDatabaseDocument? document) => document is null
        ? Error.InvalidData : _store.SaveStore(_path, document.GetSchemaVersion(), document.ToDictionary());

    public QuickDatabaseStore.LoadStatus GetLastLoadStatus() => _store.GetLastLoadStatus();
    public string GetLastBackupPath() => _store.GetLastBackupPath();
    public bool IsRecoveryPending() => _store.IsRecoveryPending();
    public Error GetLastRecoveryError() => _store.GetLastRecoveryError();
    public bool IsTransactionRecoveryPending() => _store.IsTransactionRecoveryPending();
    public Error GetLastTransactionRecoveryError() => _store.GetLastTransactionRecoveryError();
}
