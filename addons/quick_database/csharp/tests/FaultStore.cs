using System;
using System.Collections.Generic;
using Godot;

namespace QuickDev.Database.Tests;

/// <summary>Injects failures at actual ConfigFile/filesystem boundaries, without mocking Godot.</summary>
public partial class FaultStore : QuickDatabaseConfigFileStore
{
    public int SaveFailures;
    public bool WritePartialBeforeSaveFailure;
    public int VerifyFailures;
    public int PreserveFailures;
    public int PromoteFailures;
    public int RestoreFailures;
    public int BackupFailures;
    public int PreviousRemoveFailures;
    public int CanonicalRemoveFailures;
    public readonly List<string> Calls = new();

    protected override Error SaveConfig(ConfigFile config, string path)
    {
        Calls.Add("save-temp");
        if (SaveFailures > 0)
        {
            SaveFailures--;
            if (WritePartialBeforeSaveFailure)
            {
                using var partial = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Write);
                partial?.StoreString("[partially written");
            }
            return Error.CantCreate;
        }
        return base.SaveConfig(config, path);
    }
    protected override Error LoadConfig(ConfigFile config, string path)
    {
        if (path.EndsWith(".tmp", StringComparison.Ordinal))
        {
            Calls.Add("verify-temp");
            if (VerifyFailures > 0) { VerifyFailures--; return Error.FileCorrupt; }
        }
        return base.LoadConfig(config, path);
    }
    protected override Error RenameAbsolute(string sourcePath, string targetPath)
    {
        if (targetPath.EndsWith(".corrupt.cfg", StringComparison.Ordinal))
        {
            Calls.Add("backup-corrupt");
            if (BackupFailures > 0) { BackupFailures--; return Error.Busy; }
        }
        else if (sourcePath.EndsWith(".tmp", StringComparison.Ordinal))
        {
            Calls.Add("promote-temp");
            if (PromoteFailures > 0) { PromoteFailures--; return Error.CantCreate; }
        }
        else if (sourcePath.EndsWith(".previous", StringComparison.Ordinal))
        {
            Calls.Add("restore-previous");
            if (RestoreFailures > 0) { RestoreFailures--; return Error.Busy; }
        }
        else if (targetPath.EndsWith(".previous", StringComparison.Ordinal))
        {
            Calls.Add("preserve-canonical");
            if (PreserveFailures > 0) { PreserveFailures--; return Error.Busy; }
        }
        return base.RenameAbsolute(sourcePath, targetPath);
    }
    protected override Error RemoveAbsolute(string path)
    {
        if (path.EndsWith(".previous", StringComparison.Ordinal))
        {
            Calls.Add("remove-previous");
            if (PreviousRemoveFailures > 0) { PreviousRemoveFailures--; return Error.Busy; }
        }
        else if (path.EndsWith(".tmp", StringComparison.Ordinal)) Calls.Add("remove-temp");
        else
        {
            Calls.Add("remove-canonical");
            if (CanonicalRemoveFailures > 0) { CanonicalRemoveFailures--; return Error.Busy; }
        }
        return base.RemoveAbsolute(path);
    }
}
