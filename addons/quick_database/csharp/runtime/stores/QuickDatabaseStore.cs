using Godot;
using Godot.Collections;

namespace QuickDev.Database;

/// <summary>Replaceable persistence backend for Quick Database documents.</summary>
public partial class QuickDatabaseStore : RefCounted
{
    public enum LoadStatus
    {
        Missing,
        Loaded,
        RecoveredCorrupt,
        RecoveredUnknownSchema,
        RecoveryBlockedCorrupt,
        RecoveryBlockedUnknownSchema,
        RecoveredInterruptedTransaction,
        TransactionRecoveryBlocked,
    }

    public virtual Dictionary LoadStore(string path, long expectedSchemaVersion) => new()
    {
        ["payload"] = new Dictionary(),
        ["status"] = (int)LoadStatus.Missing,
    };

    public virtual Error SaveStore(string path, long expectedSchemaVersion, Dictionary payload) => Error.Ok;

    public virtual Error EraseStore(string path) => Error.Ok;

    public virtual LoadStatus GetLastLoadStatus() => LoadStatus.Missing;

    public virtual string GetLastBackupPath() => string.Empty;

    public virtual bool IsRecoveryPending() => false;

    public virtual Error GetLastRecoveryError() => Error.Ok;

    public virtual bool IsTransactionRecoveryPending() => false;

    public virtual Error GetLastTransactionRecoveryError() => Error.Ok;
}
