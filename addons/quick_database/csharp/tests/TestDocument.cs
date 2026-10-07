using Godot;
using GDictionary = Godot.Collections.Dictionary;

namespace QuickDev.Database.Tests;

/// <summary>Strict deserializer deliberately mirrors the original GDScript probe.</summary>
public partial class TestDocument : QuickDatabaseDocument
{
    public int Count { get; set; }
    public string Label { get; set; } = "untitled";
    public GDictionary Ratios { get; set; } = new();
    public override long GetSchemaVersion() => 7;
    public override GDictionary ToDictionary() => new()
    {
        ["count"] = Count, ["label"] = Label, ["ratios"] = Ratios.Duplicate(true)
    };
    public override void FromDictionary(GDictionary data)
    {
        Count = data.TryGetValue("count", out var count) && count.VariantType == Variant.Type.Int ? count.AsInt32() : 0;
        Label = data.TryGetValue("label", out var label) && label.VariantType == Variant.Type.String ? label.AsString() : "untitled";
        Ratios = data.TryGetValue("ratios", out var ratios) && ratios.VariantType == Variant.Type.Dictionary
            ? ratios.AsGodotDictionary().Duplicate(true) : new GDictionary();
    }
}
