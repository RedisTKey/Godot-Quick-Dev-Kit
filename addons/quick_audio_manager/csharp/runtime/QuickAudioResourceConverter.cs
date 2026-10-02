#nullable disable
using Godot;
using System.Collections.Generic;

namespace QuickDevKit.QuickAudio;

/// <summary>Explicitly copies GDScript-authored audio resources into C# resources.
/// Source files are never modified. Keep one converter per batch to preserve shared track references.</summary>
public sealed class QuickAudioResourceConverter
{
    private readonly Dictionary<ulong, QuickAudioTrackDefinition> _tracks = new();

    public QuickAudioTrackDefinition ConvertTrack(Resource source)
    {
        if (source == null) return null;
        if (source is QuickAudioTrackDefinition native) return native;
        if (_tracks.TryGetValue(source.GetInstanceId(), out var existing)) return existing;
        if (!Has(source, "track_id") || !Has(source, "bus_name"))
            throw new System.ArgumentException("Expected a QuickAudioTrackDefinition resource.", nameof(source));
        var track = new CSharpQuickAudioTrackDefinition
        {
            TrackId = source.Get("track_id").AsStringName(),
            DisplayName = source.Get("display_name").AsString(),
            BusName = source.Get("bus_name").AsStringName(),
            ParentTrackId = source.Get("parent_track_id").AsStringName(),
            VolumeDb = source.Get("volume_db").AsSingle(),
            Muted = source.Get("muted").AsBool(),
            MaxVoices = source.Get("max_voices").AsInt32(),
            ProcessMode = (Node.ProcessModeEnum)source.Get("process_mode").AsInt32(),
            ResourceName = source.ResourceName
        };
        _tracks.Add(source.GetInstanceId(), track);
        return track;
    }

    public QuickAudioAsset ConvertAsset(Resource source)
    {
        if (source == null) return null;
        if (source is QuickAudioAsset native) return native;
        if (!Has(source, "stream") || !Has(source, "track"))
            throw new System.ArgumentException("Expected a QuickAudioAsset resource.", nameof(source));
        return new CSharpQuickAudioAsset
        {
            Stream = source.Get("stream").AsGodotObject() as AudioStream,
            Track = ConvertTrack(source.Get("track").AsGodotObject() as Resource),
            VolumeDb = source.Get("volume_db").AsSingle(),
            PitchScale = source.Get("pitch_scale").AsSingle(),
            ResourceName = source.ResourceName
        };
    }

    public QuickAudioTrackLibrary ConvertLibrary(Resource source)
    {
        if (source == null) return null;
        if (source is QuickAudioTrackLibrary native) return native;
        if (!Has(source, "tracks")) throw new System.ArgumentException("Expected a QuickAudioTrackLibrary resource.", nameof(source));
        var library = new CSharpQuickAudioTrackLibrary { ResourceName = source.ResourceName };
        foreach (var item in source.Get("tracks").AsGodotArray())
            library.Tracks.Add(ConvertTrack(item.AsGodotObject() as Resource));
        return library;
    }

    private static bool Has(Resource source, string name)
    {
        foreach (var property in source.GetPropertyList())
            if (property["name"].AsString() == name) return true;
        return false;
    }
}
