#if TOOLS
using Godot;

namespace QuickDev.Database;

[Tool]
public partial class QuickDatabasePlugin : EditorPlugin
{
    public const string AutoloadName = "QuickDatabase";
    public const string AutoloadPath = "res://addons/quick_database/csharp/runtime/QuickDatabaseService.cs";
    private const string Setting = "autoload/" + AutoloadName;
    private bool _registered;

    public override void _EnterTree()
    {
        if (ProjectSettings.HasSetting(Setting))
        {
            // Editor shutdown preserves project settings. Reclaim our existing registration
            // without replacing a user's singleton or the GDScript variant.
            if (OwnsAutoload())
                _registered = true;
            else
                GD.PushWarning("Quick Database: QuickDatabase autoload is already owned by another plugin. Disable the other variant first.");
            return;
        }
        AddAutoloadSingleton(AutoloadName, AutoloadPath);
        _registered = true;
    }

    public override void _ExitTree()
    {
        // _ExitTree also runs when the editor closes. Only remove an owned registration.
        if (_registered && ProjectSettings.HasSetting(Setting)
            && OwnsAutoload())
            RemoveAutoloadSingleton(AutoloadName);
        _registered = false;
    }

    private static bool OwnsAutoload()
    {
        var path = ProjectSettings.GetSetting(Setting, "").AsString().TrimStart('*');
        // Newer Godot editors store autoload scripts as uid:// references.
        if (path.StartsWith("uid://", System.StringComparison.Ordinal))
        {
            var id = ResourceUid.TextToId(path);
            path = ResourceUid.HasId(id) ? ResourceUid.GetIdPath(id) : path;
        }
        return path == AutoloadPath;
    }
}
#endif
