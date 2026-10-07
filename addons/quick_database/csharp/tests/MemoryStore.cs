using Godot;
using GDictionary = Godot.Collections.Dictionary;

namespace QuickDev.Database.Tests;

public partial class MemoryStore : QuickDatabaseStore
{
    public GDictionary Result { get; set; } = new() { ["payload"] = new GDictionary(), ["status"] = (int)LoadStatus.Loaded };
    public string LastPath = "";
    public long LastSchema;
    public int Loads;
    public int Saves;
    public int Erases;
    public GDictionary Saved { get; private set; } = new();
    public Error SaveError = Error.Ok;
    public Error EraseError = Error.Ok;
    public override GDictionary LoadStore(string path, long expectedSchemaVersion)
    {
        LastPath = path;
        LastSchema = expectedSchemaVersion;
        Loads++;
        return Result;
    }
    public override Error SaveStore(string path, long schemaVersion, GDictionary payload)
    {
        LastPath = path;
        LastSchema = schemaVersion;
        Saves++;
        if (SaveError == Error.Ok) Saved = payload.Duplicate(true);
        return SaveError;
    }
    public override Error EraseStore(string path)
    {
        LastPath = path;
        Erases++;
        return EraseError;
    }
    public override LoadStatus GetLastLoadStatus() => LoadStatus.Loaded;
}
