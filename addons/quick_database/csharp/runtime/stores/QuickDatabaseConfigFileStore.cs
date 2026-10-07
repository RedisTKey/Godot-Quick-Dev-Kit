using Godot;
using Godot.Collections;

namespace QuickDev.Database;

/// <summary>
/// ConfigFile storage with schema checks, corrupt-file backups, and recoverable
/// replacement transactions. Pending corruption is tracked independently per path.
/// This protocol does not provide an fsync or filesystem-corruption guarantee.
/// </summary>
public partial class QuickDatabaseConfigFileStore : QuickDatabaseStore
{
    public const string DefaultPath = "user://quick_database.cfg";
    public const string SectionMeta = "meta";
    public const string SectionData = "data";
    public const string KeySchemaVersion = "schema_version";
    public const string KeyPayload = "payload";

    private LoadStatus _lastLoadStatus = LoadStatus.Missing;
    private string _lastBackupPath = string.Empty;
    private bool _recoveryPending;
    private Error _lastRecoveryError = Error.Ok;
    private bool _transactionRecoveryPending;
    private Error _lastTransactionRecoveryError = Error.Ok;
    private bool _transactionWasRecovered;
    private readonly System.Collections.Generic.Dictionary<string, Error> _pendingCorruptRecoveries = new();

    public override Dictionary LoadStore(string path, long expectedSchemaVersion)
    {
        string resolved = ResolveAbsolutePath(path);
        BeginOperation(resolved);
        _lastTransactionRecoveryError = RecoverInterruptedTransaction(resolved);
        if (_lastTransactionRecoveryError != Error.Ok)
        {
            _transactionRecoveryPending = true;
            if (!FileAccess.FileExists(resolved))
            {
                _lastLoadStatus = LoadStatus.TransactionRecoveryBlocked;
                return EmptyResult();
            }
        }

        if (!FileAccess.FileExists(resolved))
        {
            ClearCorruptRecovery(resolved);
            _lastLoadStatus = LoadStatus.Missing;
            return EmptyResult();
        }

        using var config = new ConfigFile();
        if (LoadConfig(config, resolved) != Error.Ok)
        {
            AttemptCorruptRecovery(resolved);
            _lastLoadStatus = _recoveryPending
                ? LoadStatus.RecoveryBlockedCorrupt
                : LoadStatus.RecoveredCorrupt;
            return EmptyResult();
        }

        Variant rawSchema = config.GetValue(SectionMeta, KeySchemaVersion);
        if (rawSchema.VariantType != Variant.Type.Int || rawSchema.AsInt64() != expectedSchemaVersion)
        {
            AttemptCorruptRecovery(resolved);
            _lastLoadStatus = _recoveryPending
                ? LoadStatus.RecoveryBlockedUnknownSchema
                : LoadStatus.RecoveredUnknownSchema;
            return EmptyResult();
        }

        ClearCorruptRecovery(resolved);
        _lastLoadStatus = _transactionRecoveryPending
            ? LoadStatus.TransactionRecoveryBlocked
            : _transactionWasRecovered ? LoadStatus.RecoveredInterruptedTransaction : LoadStatus.Loaded;
        Variant rawPayload = config.GetValue(SectionData, KeyPayload, new Dictionary());
        Dictionary payload = rawPayload.VariantType == Variant.Type.Dictionary
            ? rawPayload.AsGodotDictionary().Duplicate(true)
            : new Dictionary();
        return new Dictionary { ["payload"] = payload, ["status"] = (int)_lastLoadStatus };
    }

    public override Error SaveStore(string path, long expectedSchemaVersion, Dictionary payload)
    {
        string resolved = ResolveAbsolutePath(path);
        BeginOperation(resolved);
        _lastTransactionRecoveryError = RecoverInterruptedTransaction(resolved);
        if (_lastTransactionRecoveryError != Error.Ok)
        {
            _transactionRecoveryPending = true;
            return _lastTransactionRecoveryError;
        }

        _transactionRecoveryPending = false;
        if (_recoveryPending)
        {
            AttemptCorruptRecovery(resolved);
            if (_lastRecoveryError != Error.Ok)
                return _lastRecoveryError;
        }

        string absolutePath = ProjectSettings.GlobalizePath(resolved);
        string parentPath = absolutePath.GetBaseDir();
        if (!DirAccess.DirExistsAbsolute(parentPath))
        {
            Error directoryError = DirAccess.MakeDirRecursiveAbsolute(parentPath);
            if (directoryError != Error.Ok)
                return directoryError;
        }

        using var config = new ConfigFile();
        config.SetValue(SectionMeta, KeySchemaVersion, expectedSchemaVersion);
        config.SetValue(SectionData, KeyPayload, payload.Duplicate(true));
        return SaveConfigTransactionally(config, absolutePath);
    }

    public override Error EraseStore(string path)
    {
        string absolutePath = ResolveAbsolutePath(path);
        BeginOperation(absolutePath);
        // Remove rollback data first. Otherwise a cleanup failure after deleting
        // the canonical file could bring an erased save back on the next load.
        Error removeError = RemoveAbsolute(GetPreviousPath(absolutePath));
        if (removeError != Error.Ok)
            return removeError;
        removeError = RemoveAbsolute(absolutePath);
        if (removeError == Error.Ok)
            ClearCorruptRecovery(absolutePath);
        return removeError;
    }

    public override LoadStatus GetLastLoadStatus() => _lastLoadStatus;

    public override string GetLastBackupPath() => _lastBackupPath;

    public override bool IsRecoveryPending() => _recoveryPending;

    public override Error GetLastRecoveryError() => _lastRecoveryError;

    public override bool IsTransactionRecoveryPending() => _transactionRecoveryPending;

    public override Error GetLastTransactionRecoveryError() => _lastTransactionRecoveryError;

    private static string ResolvePath(string path) => string.IsNullOrEmpty(path) ? DefaultPath : path;

    private static string ResolveAbsolutePath(string path) =>
        ProjectSettings.GlobalizePath(ResolvePath(path)).SimplifyPath();

    private Dictionary EmptyResult() => new()
    {
        ["payload"] = new Dictionary(),
        ["status"] = (int)_lastLoadStatus,
    };

    private void BeginOperation(string absolutePath)
    {
        _lastBackupPath = string.Empty;
        _recoveryPending = _pendingCorruptRecoveries.TryGetValue(absolutePath, out Error pendingError);
        _lastRecoveryError = _recoveryPending ? pendingError : Error.Ok;
        _transactionRecoveryPending = false;
        _lastTransactionRecoveryError = Error.Ok;
        _transactionWasRecovered = false;
    }

    private void AttemptCorruptRecovery(string absolutePath)
    {
        _lastRecoveryError = BackupCorruptSave(absolutePath);
        _recoveryPending = _lastRecoveryError != Error.Ok;
        if (_recoveryPending)
            _pendingCorruptRecoveries[absolutePath] = _lastRecoveryError;
        else
            _pendingCorruptRecoveries.Remove(absolutePath);
    }

    private void ClearCorruptRecovery(string absolutePath)
    {
        _pendingCorruptRecoveries.Remove(absolutePath);
        _recoveryPending = false;
        _lastRecoveryError = Error.Ok;
    }

    private Error BackupCorruptSave(string resolvedPath)
    {
        string sourcePath = ProjectSettings.GlobalizePath(resolvedPath);
        if (!FileAccess.FileExists(sourcePath))
            return Error.Ok;

        Dictionary datetime = Time.GetDatetimeDictFromSystem();
        string timestamp = $"{datetime["year"].AsInt32():D4}{datetime["month"].AsInt32():D2}" +
            $"{datetime["day"].AsInt32():D2}_{datetime["hour"].AsInt32():D2}" +
            $"{datetime["minute"].AsInt32():D2}{datetime["second"].AsInt32():D2}_{Time.GetTicksMsec()}";
        // Millisecond timestamps alone can collide during rapid retries and
        // rename can overwrite an older backup, so add a per-backup nonce.
        string backupPath = $"{sourcePath.GetBaseName()}.{timestamp}_{System.Guid.NewGuid():N}.corrupt.cfg";
        Error backupError = RenameAbsolute(sourcePath, backupPath);
        if (backupError == Error.Ok)
        {
            _lastBackupPath = backupPath;
            return Error.Ok;
        }

        GD.PushError($"Could not back up corrupt database {sourcePath} (error {(int)backupError}).");
        return backupError;
    }

    private Error SaveConfigTransactionally(ConfigFile config, string absolutePath)
    {
        string nonce = $"{Time.GetTicksUsec()}_{GD.Randi()}";
        string temporaryPath = $"{absolutePath}.{nonce}.tmp";
        string previousPath = GetPreviousPath(absolutePath);
        Error saveError = SaveConfig(config, temporaryPath);
        if (saveError != Error.Ok)
        {
            // A failed writer may have created a partial file. Preserve its
            // original error even if this best-effort cleanup also fails.
            RemoveAbsolute(temporaryPath);
            return saveError;
        }

        using var verificationConfig = new ConfigFile();
        Error verificationError = LoadConfig(verificationConfig, temporaryPath);
        if (verificationError != Error.Ok)
        {
            RemoveAbsolute(temporaryPath);
            return verificationError;
        }

        if (!FileAccess.FileExists(absolutePath))
        {
            Error promoteNewError = RenameAbsolute(temporaryPath, absolutePath);
            if (promoteNewError != Error.Ok)
                RemoveAbsolute(temporaryPath);
            return promoteNewError;
        }

        Error preserveError = RenameAbsolute(absolutePath, previousPath);
        if (preserveError != Error.Ok)
        {
            RemoveAbsolute(temporaryPath);
            return preserveError;
        }

        Error promoteError = RenameAbsolute(temporaryPath, absolutePath);
        if (promoteError != Error.Ok)
        {
            Error restoreError = RenameAbsolute(previousPath, absolutePath);
            RemoveAbsolute(temporaryPath);
            if (restoreError != Error.Ok)
            {
                _transactionRecoveryPending = true;
                _lastTransactionRecoveryError = restoreError;
                GD.PushError($"Could not restore previous database {absolutePath} after error {(int)promoteError} " +
                    $"(restore error {(int)restoreError}).");
                return restoreError;
            }

            return promoteError;
        }

        Error cleanupError = RemoveAbsolute(previousPath);
        if (cleanupError != Error.Ok)
        {
            GD.PushWarning($"Saved successfully but could not remove previous database {previousPath} " +
                $"(error {(int)cleanupError}).");
        }

        return Error.Ok;
    }

    private Error RecoverInterruptedTransaction(string resolvedPath)
    {
        string absolutePath = ProjectSettings.GlobalizePath(resolvedPath);
        string previousPath = GetPreviousPath(absolutePath);
        if (!FileAccess.FileExists(previousPath))
            return Error.Ok;

        if (FileAccess.FileExists(absolutePath))
        {
            Error cleanupError = RemoveAbsolute(previousPath);
            if (cleanupError != Error.Ok)
            {
                GD.PushError($"Could not remove completed transaction file {previousPath} " +
                    $"(error {(int)cleanupError}).");
                return cleanupError;
            }

            return Error.Ok;
        }

        Error restoreError = RenameAbsolute(previousPath, absolutePath);
        if (restoreError != Error.Ok)
        {
            GD.PushError($"Could not recover interrupted database transaction {previousPath} " +
                $"(error {(int)restoreError}).");
            return restoreError;
        }

        _transactionWasRecovered = true;
        return Error.Ok;
    }

    private static string GetPreviousPath(string absolutePath) => $"{absolutePath}.previous";

    /// <summary>Override to inject storage failures in backend tests.</summary>
    protected virtual Error SaveConfig(ConfigFile config, string path) => config.Save(path);

    /// <summary>Override to inject read or verification failures in backend tests.</summary>
    protected virtual Error LoadConfig(ConfigFile config, string path) => config.Load(path);

    /// <summary>Override to inject backup, promotion, or restoration failures.</summary>
    protected virtual Error RenameAbsolute(string sourcePath, string targetPath) =>
        DirAccess.RenameAbsolute(sourcePath, targetPath);

    /// <summary>Override to inject cleanup or erase failures.</summary>
    protected virtual Error RemoveAbsolute(string path) =>
        FileAccess.FileExists(path) ? DirAccess.RemoveAbsolute(path) : Error.Ok;
}
