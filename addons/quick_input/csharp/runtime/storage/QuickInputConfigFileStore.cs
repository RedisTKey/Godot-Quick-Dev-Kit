using Godot;
using Godot.Collections;

namespace QuickDevKit.QuickInput;

/// <summary>Shares the GDScript v1 format, with verified temporary writes and backup recovery.</summary>
public partial class QuickInputConfigFileStore : QuickInputStore
{
    public const string DefaultPath = "user://quick_input_bindings.cfg";
    public const int Version = 1;
    public string Path { get; set; } = DefaultPath;

    // Godot script instantiation needs a real parameterless constructor.
    public QuickInputConfigFileStore() { }
    public QuickInputConfigFileStore(string path) => Path = path;

    public override Dictionary LoadBindings()
    {
        LoadError = Error.Ok;
        if (!FileAccess.FileExists(Path))
        {
            string backup = ProjectSettings.GlobalizePath(Path) + ".previous";
            if (!FileAccess.FileExists(backup)) return new();
            LoadError = DirAccess.RenameAbsolute(backup, ProjectSettings.GlobalizePath(Path));
            if (LoadError != Error.Ok) return new();
        }
        using var config = new ConfigFile();
        LoadError = config.Load(Path);
        if (LoadError != Error.Ok) return new();
        var version = config.GetValue("meta", "version", -1);
        if (version.VariantType != Variant.Type.Int || version.AsInt64() != Version)
        {
            LoadError = Error.FileUnrecognized;
            return new();
        }
        var data = config.GetValue("input", "bindings");
        if (data.VariantType != Variant.Type.Dictionary)
        {
            LoadError = Error.InvalidData;
            return new();
        }
        return data.AsGodotDictionary().Duplicate(true);
    }

    public override Error SaveBindings(Dictionary bindings)
    {
        if (bindings == null) return Error.InvalidParameter;
        using var config = new ConfigFile();
        config.SetValue("meta", "version", Version);
        config.SetValue("input", "bindings", bindings.Duplicate(true));
        string temporary = Path + ".tmp";
        Error error = config.Save(temporary);
        if (error != Error.Ok) return error;
        using var check = new ConfigFile();
        error = check.Load(temporary);
        var checkedBindings = error == Error.Ok ? check.GetValue("input", "bindings") : default;
        if (error != Error.Ok || checkedBindings.VariantType != Variant.Type.Dictionary ||
            !checkedBindings.AsGodotDictionary().RecursiveEqual(bindings))
        {
            DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(temporary));
            return Error.FileCorrupt;
        }
        string destination = ProjectSettings.GlobalizePath(Path);
        string tempFile = ProjectSettings.GlobalizePath(temporary);
        string backup = destination + ".previous";
        bool hadPrevious = FileAccess.FileExists(Path);
        if (hadPrevious)
        {
            if (FileAccess.FileExists(backup))
            {
                error = DirAccess.RemoveAbsolute(backup);
                if (error != Error.Ok) return error;
            }
            error = DirAccess.RenameAbsolute(destination, backup);
            if (error != Error.Ok) return error;
        }
        error = DirAccess.RenameAbsolute(tempFile, destination);
        if (error != Error.Ok && hadPrevious)
            DirAccess.RenameAbsolute(backup, destination);
        return error;
    }
}
