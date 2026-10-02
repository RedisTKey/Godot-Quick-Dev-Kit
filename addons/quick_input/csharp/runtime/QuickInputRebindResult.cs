#nullable disable
using Godot;
using Godot.Collections;

namespace QuickDevKit.QuickInput;

/// <summary>The validation and conflict information for a proposed rebind.</summary>
public partial class QuickInputRebindResult : RefCounted
{
    public enum RebindStatus { Ready, Conflict, Invalid, Unsupported }

    public RebindStatus Status { get; set; } = RebindStatus.Unsupported;
    public string Message { get; set; } = "";
    public Array<QuickInputBinding> Conflicts { get; set; } = new();
    public QuickInputBindingTransaction Transaction { get; set; }
}
