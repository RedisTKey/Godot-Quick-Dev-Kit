#nullable disable
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using TrackArray = Godot.Collections.Array<Godot.StringName>;

namespace QuickDevKit.QuickAudio;

/// <summary>Owns temporary non-spatial players and restores the AudioServer state it borrows.</summary>
public partial class QuickAudioManagerService : Node
{
    [Signal] public delegate void AudioStartedEventHandler(QuickAudioAsset asset, StringName trackId, AudioStreamPlayer player);
    [Signal] public delegate void AudioStoppedEventHandler(StringName trackId, AudioStreamPlayer player);
    [Signal] public delegate void LibraryConfiguredEventHandler(TrackArray trackIds);
    [Signal] public delegate void AudioErrorEventHandler(StringName code, string message);

    public const string ErrorLibraryNull = "library_null";
    public const string ErrorLibraryEmpty = "library_empty";
    public const string ErrorTrackNull = "track_null";
    public const string ErrorTrackIdEmpty = "track_id_empty";
    public const string ErrorTrackIdDuplicate = "track_id_duplicate";
    public const string ErrorBusNameEmpty = "bus_name_empty";
    public const string ErrorBusNameReserved = "bus_name_reserved";
    public const string ErrorBusNameDuplicate = "bus_name_duplicate";
    public const string ErrorMaxVoicesInvalid = "max_voices_invalid";
    public const string ErrorProcessModeInvalid = "process_mode_invalid";
    public const string ErrorParentMissing = "parent_missing";
    public const string ErrorParentCycle = "parent_cycle";
    public const string ErrorAlreadyConfigured = "already_configured";
    public const string ErrorBusCreationFailed = "bus_creation_failed";
    public const string ErrorNotConfigured = "not_configured";
    public const string ErrorAssetNull = "asset_null";
    public const string ErrorStreamNull = "stream_null";
    public const string ErrorAssetTrackNull = "asset_track_null";
    public const string ErrorTrackUnknown = "track_unknown";
    public const string ErrorTrackBusMissing = "track_bus_missing";
    public const string ErrorOperationInProgress = "operation_in_progress";
    public const string ErrorManagerNotInTree = "manager_not_in_tree";
    public const string DefaultTrackLibraryPath = "res://addons/quick_audio_manager/csharp/resources/default_track_library.tres";

    [Export] public QuickAudioTrackLibrary TrackLibrary { get; set; }

    private sealed record TrackState(StringName Id, StringName Bus, StringName Parent, float Volume, bool Muted, int MaxVoices, ProcessModeEnum Mode);
    private sealed record BusState(StringName Send, float Volume, bool Muted);
    private readonly Dictionary<StringName, TrackState> _tracks = new();
    private readonly List<StringName> _trackOrder = new();
    private readonly Dictionary<StringName, List<AudioStreamPlayer>> _players = new();
    private readonly List<StringName> _createdBuses = new();
    private readonly Dictionary<StringName, BusState> _reusedBuses = new();
    private readonly List<StringName> _originalBusOrder = new();
    private readonly Dictionary<StringName, StringName> _adjustedExternalSends = new();
    private bool _busOrderChanged;
    private bool _masterModified;
    private float _originalMasterVolume;
    private bool _configured;
    private ulong _configurationVersion;
    private bool _startingPlayback;
    private bool _exiting;

    public override void _Ready()
    {
        // ConfigureLibrary may have been called before adding the service to the tree.
        if (!_configured) ConfigureLibrary(TrackLibrary ?? GD.Load<QuickAudioTrackLibrary>(DefaultTrackLibraryPath));
    }

    public override void _ExitTree()
    {
        _exiting = true;
        _configurationVersion++;
        StopAll();
        RestoreAudioServer();
        _tracks.Clear();
        _trackOrder.Clear();
        _players.Clear();
        _configured = false;
        _exiting = false;
        RequestReady();
    }

    public override void _Notification(int what)
    {
        // A configured node may be freed before it ever enters a SceneTree.
        if (what == NotificationPredelete && (_configured || _masterModified))
            RestoreAudioServer();
    }

    public bool ConfigureLibrary(QuickAudioTrackLibrary library)
    {
        if (_configured) return Fail(ErrorAlreadyConfigured, "The audio manager has already configured a track library.");
        if (_exiting) return false;
        if (!ValidateLibrary(library, out var tracks, out var ordered)) return false;
        if (!InstallTracks(tracks, ordered))
        {
            RestoreAudioServer();
            return false;
        }
        TrackLibrary = library;
        foreach (var id in ordered)
        {
            _tracks.Add(id, tracks[id]);
            _trackOrder.Add(id);
            _players.Add(id, new());
        }
        _configurationVersion++;
        _configured = true;
        EmitSignal(SignalName.LibraryConfigured, GetTrackIds());
        return true;
    }

    public AudioStreamPlayer Play(QuickAudioAsset asset)
    {
        if (_exiting) return null;
        if (_startingPlayback) { Fail(ErrorOperationInProgress, "Playback cannot be started recursively from a playback replacement callback."); return null; }
        if (!_configured) { Fail(ErrorNotConfigured, "Configure a valid track library before playback."); return null; }
        if (asset == null) { Fail(ErrorAssetNull, "The requested audio asset is null."); return null; }
        if (asset.Stream == null) { Fail(ErrorStreamNull, "The requested audio asset has no stream."); return null; }
        if (asset.Track == null) { Fail(ErrorAssetTrackNull, "The requested audio asset has no track."); return null; }
        var id = asset.Track.TrackId;
        if (!_tracks.TryGetValue(id, out var track)) { Fail(ErrorTrackUnknown, $"The audio asset references an unconfigured track: {id}"); return null; }
        if (AudioServer.GetBusIndex(track.Bus) < 0) { Fail(ErrorTrackBusMissing, $"The configured audio bus is unavailable: {track.Bus}"); return null; }
        if (!IsInsideTree()) { Fail(ErrorManagerNotInTree, "Add the audio manager to the SceneTree before playback."); return null; }
        // User stop callbacks can mutate the request resource while a voice is stolen.
        var stream = asset.Stream;
        var volume = asset.VolumeDb;
        var pitch = asset.PitchScale;
        var version = _configurationVersion;
        AudioStreamPlayer player;
        _startingPlayback = true;
        try
        {
            PrunePlayers(id);
            var players = _players[id];
            while (players.Count >= track.MaxVoices)
            {
                StopPlayerInternal(players[0], id);
                // A stop callback may detach/free the manager. Never create an orphan player.
                if (!IsInstanceValid(this) || !IsInsideTree() || !_configured || version != _configurationVersion) return null;
                PrunePlayers(id);
            }
            player = new AudioStreamPlayer
            {
                ProcessMode = track.Mode,
                Name = $"QuickAudio_{id}_{Time.GetTicksUsec()}",
                Stream = stream,
                Bus = track.Bus,
                VolumeDb = volume,
                PitchScale = pitch
            };
            player.Finished += () => StopPlayerInternal(player, id);
            AddChild(player);
            players.Add(player);
            player.Play();
        }
        finally { _startingPlayback = false; }
        EmitSignal(SignalName.AudioStarted, asset, id, player);
        // Callbacks may explicitly stop or free the new player.
        return IsInstanceValid(player) && !player.IsQueuedForDeletion() ? player : null;
    }

    public AudioStreamPlayer EnsurePlaying(QuickAudioAsset asset)
    {
        if (asset?.Stream == null || asset.Track == null) return Play(asset);
        var id = asset.Track.TrackId;
        if (!_players.TryGetValue(id, out var players)) return Play(asset);
        PrunePlayers(id);
        foreach (var player in players)
            if (player.Stream == asset.Stream && player.Playing) return player;
        return Play(asset);
    }

    public bool StopPlayer(AudioStreamPlayer player)
    {
        if (!IsInstanceValid(player)) return false;
        foreach (var id in _trackOrder.ToArray())
            if (_players.TryGetValue(id, out var players) && players.Contains(player)) return StopPlayerInternal(player, id);
        return false;
    }

    public int StopTrack(StringName trackId)
    {
        if (!_players.TryGetValue(trackId, out var players)) return 0;
        PrunePlayers(trackId);
        var count = 0;
        foreach (var player in players.ToArray()) if (StopPlayerInternal(player, trackId)) count++;
        return count;
    }

    public int StopAll()
    {
        var count = 0;
        foreach (var id in _trackOrder.ToArray()) count += StopTrack(id);
        return count;
    }

    public int GetActivePlayerCount() => GetActivePlayerCount("");
    public int GetActivePlayerCount(StringName trackId)
    {
        if (!string.IsNullOrEmpty(trackId))
        {
            PrunePlayers(trackId);
            return _players.TryGetValue(trackId, out var players) ? players.Count : 0;
        }
        var count = 0;
        foreach (var id in _trackOrder) { PrunePlayers(id); count += _players[id].Count; }
        return count;
    }

    public bool IsConfigured() => _configured;
    public bool HasTrack(StringName trackId) => _tracks.ContainsKey(trackId);
    public TrackArray GetTrackIds() => new(_trackOrder);
    public StringName GetTrackBusName(StringName trackId) => _tracks.TryGetValue(trackId, out var track) ? track.Bus : new StringName("");

    public bool SetTrackVolumeDb(StringName trackId, float volumeDb)
    {
        var index = GetTrackBusIndex(trackId);
        if (index < 0) return false;
        AudioServer.SetBusVolumeDb(index, Mathf.Clamp(volumeDb, -80f, 24f));
        return true;
    }
    public float GetTrackVolumeDb(StringName trackId) => GetTrackBusIndex(trackId) is var index && index >= 0 ? AudioServer.GetBusVolumeDb(index) : -80f;
    public bool SetTrackMuted(StringName trackId, bool muted)
    {
        var index = GetTrackBusIndex(trackId);
        if (index < 0) return false;
        AudioServer.SetBusMute(index, muted);
        return true;
    }
    public bool IsTrackMuted(StringName trackId) => GetTrackBusIndex(trackId) is var index && index >= 0 && AudioServer.IsBusMute(index);
    public bool SetMasterVolumeDb(float volumeDb)
    {
        var index = AudioServer.GetBusIndex("Master");
        if (index < 0) return false;
        if (!_masterModified) { _originalMasterVolume = AudioServer.GetBusVolumeDb(index); _masterModified = true; }
        AudioServer.SetBusVolumeDb(index, Mathf.Clamp(volumeDb, -80f, 0f));
        return true;
    }
    public float GetMasterVolumeDb() => AudioServer.GetBusIndex("Master") is var index && index >= 0 ? AudioServer.GetBusVolumeDb(index) : -80f;

    private bool ValidateLibrary(QuickAudioTrackLibrary library, out Dictionary<StringName, TrackState> tracks, out List<StringName> order)
    {
        tracks = new(); order = new();
        if (library == null) return Fail(ErrorLibraryNull, "The audio track library is null.");
        if (library.Tracks == null || library.Tracks.Count == 0) return Fail(ErrorLibraryEmpty, "The audio track library has no tracks.");
        var buses = new HashSet<StringName>();
        foreach (var track in library.Tracks)
        {
            if (track == null) return Fail(ErrorTrackNull, "The audio track library contains a null track.");
            if (string.IsNullOrEmpty(track.TrackId)) return Fail(ErrorTrackIdEmpty, "A track has an empty TrackId.");
            if (tracks.ContainsKey(track.TrackId)) return Fail(ErrorTrackIdDuplicate, $"Duplicate audio track id: {track.TrackId}");
            if (string.IsNullOrEmpty(track.BusName)) return Fail(ErrorBusNameEmpty, $"Track {track.TrackId} has an empty BusName.");
            if (track.BusName == "Master") return Fail(ErrorBusNameReserved, "Tracks cannot replace the reserved Master bus.");
            if (!buses.Add(track.BusName)) return Fail(ErrorBusNameDuplicate, $"Duplicate audio bus name: {track.BusName}");
            if (track.MaxVoices < 1) return Fail(ErrorMaxVoicesInvalid, $"Track {track.TrackId} must allow at least one voice.");
            if (track.ProcessMode < ProcessModeEnum.Inherit || track.ProcessMode > ProcessModeEnum.Disabled)
                return Fail(ErrorProcessModeInvalid, $"Track {track.TrackId} has an invalid process mode.");
            tracks.Add(track.TrackId, new(track.TrackId, track.BusName, track.ParentTrackId, track.VolumeDb, track.Muted, track.MaxVoices, track.ProcessMode));
            order.Add(track.TrackId);
        }
        foreach (var track in tracks.Values)
            if (!string.IsNullOrEmpty(track.Parent) && !tracks.ContainsKey(track.Parent)) return Fail(ErrorParentMissing, $"Track {track.Id} references a missing parent: {track.Parent}");
        // Iterative walk avoids overflowing the stack for large project-defined hierarchies.
        var complete = new HashSet<StringName>();
        foreach (var id in order)
        {
            var visiting = new HashSet<StringName>();
            var cursor = id;
            while (!string.IsNullOrEmpty(cursor) && !complete.Contains(cursor))
            {
                if (!visiting.Add(cursor)) return Fail(ErrorParentCycle, $"The audio track hierarchy contains a cycle at: {cursor}");
                cursor = tracks[cursor].Parent;
            }
            complete.UnionWith(visiting);
        }
        return true;
    }

    private bool InstallTracks(Dictionary<StringName, TrackState> tracks, List<StringName> order)
    {
        _originalBusOrder.Clear();
        for (var i = 0; i < AudioServer.BusCount; i++) _originalBusOrder.Add(AudioServer.GetBusName(i));
        // Godot mixes buses right-to-left: every child must be after its send target.
        var installOrder = new List<StringName>();
        var installed = new HashSet<StringName>();
        foreach (var id in order)
        {
            var chain = new Stack<StringName>();
            var cursor = id;
            while (!string.IsNullOrEmpty(cursor) && !installed.Contains(cursor)) { chain.Push(cursor); cursor = tracks[cursor].Parent; }
            while (chain.Count > 0) { cursor = chain.Pop(); installed.Add(cursor); installOrder.Add(cursor); }
        }
        foreach (var id in installOrder)
        {
            var track = tracks[id];
            var index = AudioServer.GetBusIndex(track.Bus);
            if (index >= 0) _reusedBuses.Add(track.Bus, new(AudioServer.GetBusSend(index), AudioServer.GetBusVolumeDb(index), AudioServer.IsBusMute(index)));
            else
            {
                AudioServer.AddBus();
                index = AudioServer.BusCount - 1;
                AudioServer.SetBusName(index, track.Bus);
                if (AudioServer.GetBusIndex(track.Bus) < 0)
                {
                    AudioServer.RemoveBus(index);
                    return Fail(ErrorBusCreationFailed, $"Could not create audio bus: {track.Bus}");
                }
                _createdBuses.Add(track.Bus);
            }
        }
        // Preserve existing effective sends from non-managed buses as well. Moving a
        // borrowed child past a project's downstream bus would otherwise break its mix.
        var allBuses = new List<StringName>();
        var managedBuses = new HashSet<StringName>(tracks.Values.Select(track => track.Bus));
        var ineffectiveExternalSends = new Dictionary<StringName, StringName>();
        var sends = new Dictionary<StringName, StringName>();
        for (var i = 0; i < AudioServer.BusCount; i++)
        {
            var bus = AudioServer.GetBusName(i);
            allBuses.Add(bus);
            if (i == 0) { sends[bus] = ""; continue; }
            var send = AudioServer.GetBusSend(i);
            var sendIndex = AudioServer.GetBusIndex(send);
            // Invalid/forward sends already fall back to Master in Godot's mixer.
            sends[bus] = sendIndex >= 0 && sendIndex < i ? send : new StringName("Master");
            if (!managedBuses.Contains(bus) && sendIndex >= i)
                ineffectiveExternalSends[bus] = send;
        }
        foreach (var track in tracks.Values)
            sends[track.Bus] = string.IsNullOrEmpty(track.Parent) ? new StringName("Master") : tracks[track.Parent].Bus;
        var sorted = new List<StringName>();
        var seen = new HashSet<StringName>();
        foreach (var bus in allBuses)
        {
            var chain = new Stack<StringName>();
            var cursor = bus;
            while (!string.IsNullOrEmpty(cursor) && !seen.Contains(cursor)) { chain.Push(cursor); cursor = sends[cursor]; }
            while (chain.Count > 0) { cursor = chain.Pop(); seen.Add(cursor); sorted.Add(cursor); }
        }
        for (var i = 0; i < sorted.Count; i++)
        {
            var index = AudioServer.GetBusIndex(sorted[i]);
            if (index == i) continue;
            AudioServer.MoveBus(index, i);
            _busOrderChanged = true;
        }
        foreach (var (bus, originalSend) in ineffectiveExternalSends)
        {
            var index = AudioServer.GetBusIndex(bus);
            var sendIndex = AudioServer.GetBusIndex(originalSend);
            if (sendIndex < 0 || sendIndex >= index) continue;
            // Reordering must not activate a formerly forward-only external send.
            _adjustedExternalSends[bus] = originalSend;
            AudioServer.SetBusSend(index, "Master");
        }
        foreach (var track in tracks.Values)
        {
            var index = AudioServer.GetBusIndex(track.Bus);
            AudioServer.SetBusSend(index, sends[track.Bus]);
            AudioServer.SetBusVolumeDb(index, track.Volume);
            AudioServer.SetBusMute(index, track.Muted);
        }
        return true;
    }

    private void RestoreAudioServer()
    {
        for (var i = _createdBuses.Count - 1; i >= 0; i--)
        {
            var index = AudioServer.GetBusIndex(_createdBuses[i]);
            if (index >= 0) AudioServer.RemoveBus(index);
        }
        _createdBuses.Clear();
        if (_busOrderChanged)
        {
            var position = 0;
            foreach (var bus in _originalBusOrder)
            {
                var index = AudioServer.GetBusIndex(bus);
                if (index < 0) continue;
                if (index != position) AudioServer.MoveBus(index, position);
                position++;
            }
        }
        _originalBusOrder.Clear(); _busOrderChanged = false;
        foreach (var (bus, state) in _reusedBuses)
        {
            var index = AudioServer.GetBusIndex(bus);
            if (index < 0) continue;
            AudioServer.SetBusSend(index, state.Send);
            AudioServer.SetBusVolumeDb(index, state.Volume);
            AudioServer.SetBusMute(index, state.Muted);
        }
        _reusedBuses.Clear();
        foreach (var (bus, send) in _adjustedExternalSends)
        {
            var index = AudioServer.GetBusIndex(bus);
            if (index >= 0) AudioServer.SetBusSend(index, send);
        }
        _adjustedExternalSends.Clear();
        if (_masterModified)
        {
            var index = AudioServer.GetBusIndex("Master");
            if (index >= 0) AudioServer.SetBusVolumeDb(index, _originalMasterVolume);
            _masterModified = false;
        }
    }

    private int GetTrackBusIndex(StringName id) => _tracks.TryGetValue(id, out var track) ? AudioServer.GetBusIndex(track.Bus) : -1;
    private bool StopPlayerInternal(AudioStreamPlayer player, StringName id)
    {
        if (!IsInstanceValid(player) || !_players.TryGetValue(id, out var players) || !players.Remove(player)) return false;
        player.Stop();
        EmitSignal(SignalName.AudioStopped, id, player);
        // Event listeners may free players immediately; never access a freed native object.
        if (IsInstanceValid(player))
        {
            player.Stream = null;
            if (player.IsInsideTree()) player.QueueFree(); else player.Free();
        }
        return true;
    }
    private void PrunePlayers(StringName id)
    {
        if (_players.TryGetValue(id, out var players)) players.RemoveAll(player => !IsInstanceValid(player) || player.IsQueuedForDeletion());
    }
    private bool Fail(string code, string message) { EmitSignal(SignalName.AudioError, new StringName(code), message); return false; }
}
