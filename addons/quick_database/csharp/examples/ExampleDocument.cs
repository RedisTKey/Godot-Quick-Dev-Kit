using Godot;
using Godot.Collections;

namespace QuickDev.Database.Examples;

/// <summary>Same schema and field validation as the original GDScript example.</summary>
public partial class ExampleDocument : QuickDatabaseDocument
{
    public long Count { get; set; }
    public string Title { get; set; } = "";
    public override Dictionary ToDictionary() => new() { ["count"] = Count, ["title"] = Title };
    public override void FromDictionary(Dictionary data)
    {
        Count = data.TryGetValue("count", out var count) && count.VariantType == Variant.Type.Int ? count.AsInt64() : 0;
        Title = data.TryGetValue("title", out var title) && title.VariantType == Variant.Type.String ? title.AsString() : "";
    }
}
