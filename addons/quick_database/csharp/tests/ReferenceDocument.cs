using GDictionary = Godot.Collections.Dictionary;

namespace QuickDev.Database.Tests;

/// <summary>Intentionally retains dictionaries, proving isolation at the library boundary.</summary>
public partial class ReferenceDocument : QuickDatabaseDocument
{
    public GDictionary Data { get; set; } = new();
    public override GDictionary ToDictionary() => Data;
    public override void FromDictionary(GDictionary data) => Data = data;
}
