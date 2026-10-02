#nullable disable
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace QuickDevKit.QuickAudio.Tests;

public partial class QuickAudioRuntimeTests : Node
{
    private int _checks;
    private int _failures;
    private readonly List<string> _errors = new();

    public override async void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        try
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            QuickAudioPluginGuardTests.Run(Check);
            TestResources();
            TestValidation();
            await TestPlayback();
            await TestBusOwnership();
            await TestMixerAndCompletion();
            TestReentrancy();
            TestDetachedLifecycle();
            TestResourceConversion();
            await Frames(5);
        }
        catch (Exception e) { Check(false, e.ToString()); }
        GD.Print($"Quick Audio C# runtime: {_checks} checks, {_failures} failures.");
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    private void Check(bool condition, string message)
    {
        _checks++;
        if (!condition) { _failures++; GD.PushError(message); }
    }
    private void Equal<T>(T actual, T expected, string message) => Check(EqualityComparer<T>.Default.Equals(actual, expected), $"{message}: expected {expected}, got {actual}");
    private void Near(float actual, float expected, string message) => Check(Mathf.IsEqualApprox(actual, expected), $"{message}: expected {expected}, got {actual}");
    private static QuickAudioTrackDefinition Track(string id = "test", string bus = "QuickTest", int voices = 2, string parent = "") => new() { TrackId = id, BusName = bus, MaxVoices = voices, ParentTrackId = parent };
    private static QuickAudioTrackLibrary Library(params QuickAudioTrackDefinition[] tracks) => new() { Tracks = new(tracks) };
    private static AudioStreamWav Wave(float duration = 1f, bool loop = false)
    {
        var sampleRate = 22050;
        var count = (int)(duration * sampleRate);
        var data = new byte[count * 2];
        for (var i = 0; i < count; i++)
        {
            var sample = (short)(Math.Sin(i * 2 * Math.PI * 440 / sampleRate) * 12000);
            data[i * 2] = (byte)sample; data[i * 2 + 1] = (byte)(sample >> 8);
        }
        return new AudioStreamWav { Format = AudioStreamWav.FormatEnum.Format16Bits, MixRate = sampleRate, Data = data, LoopMode = loop ? AudioStreamWav.LoopModeEnum.Forward : AudioStreamWav.LoopModeEnum.Disabled, LoopEnd = count };
    }
    private static QuickAudioAsset Asset(QuickAudioTrackDefinition track, AudioStream stream = null) => new() { Track = track, Stream = stream ?? Wave() };
    private QuickAudioManagerService Manager(QuickAudioTrackLibrary library = null)
    {
        var manager = new QuickAudioManagerService();
        if (library != null) manager.TrackLibrary = library;
        manager.AudioError += (code, _) => _errors.Add(code);
        GetTree().Root.AddChild(manager);
        return manager;
    }
    private async Task Frames(int count) { for (var i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
    private async Task Delay(double seconds) => await ToSignal(GetTree().CreateTimer(seconds, true), SceneTreeTimer.SignalName.Timeout);

    private void TestResources()
    {
        var asset = new QuickAudioAsset();
        Check(asset.Stream == null && asset.Track == null, "Assets require explicit stream and track");
        Near(asset.VolumeDb, 0, "Default volume"); Near(asset.PitchScale, 1, "Default pitch");
        var library = GD.Load<QuickAudioTrackLibrary>(QuickAudioManagerService.DefaultTrackLibraryPath);
        Check(library != null && library.Tracks.Count == 3, "C# default library loads three typed resources");
        if (library == null) return;
        var expectedIds = new[] { "music", "sfx", "ui" };
        var expectedBuses = new[] { "QuickMusic", "QuickSFX", "QuickUI" };
        var expectedVoices = new[] { 1, 24, 8 };
        for (var i = 0; i < 3; i++)
        {
            Equal(library.Tracks[i].TrackId.ToString(), expectedIds[i], "Default track ID");
            Equal(library.Tracks[i].BusName.ToString(), expectedBuses[i], "Default bus");
            Equal(library.Tracks[i].MaxVoices, expectedVoices[i], "Default voices");
        }
        Equal(library.Tracks[0].ProcessMode, ProcessModeEnum.Always, "Music processes while paused");
        var scene = GD.Load<PackedScene>("res://addons/quick_audio_manager/csharp/examples/basic_usage.tscn");
        var example = scene?.Instantiate<Examples.BasicUsage>();
        Check(example != null, "C# example scene loads");
        if (example != null) { AddChild(example); Check(example.PlayAudio() == null, "Example tolerates unset asset"); example.Free(); }
        var saved = new CSharpQuickAudioAsset { Stream = Wave(.1f), Track = new CSharpQuickAudioTrackDefinition { TrackId = "saved", BusName = "SavedBus", MaxVoices = 7, ParentTrackId = "parent", ProcessMode = ProcessModeEnum.Always }, VolumeDb = -7.5f, PitchScale = 1.25f };
        Equal(ResourceSaver.Save(saved, "user://quick_audio_roundtrip.tres"), Error.Ok, "Asset save");
        var loaded = ResourceLoader.Load<QuickAudioAsset>("user://quick_audio_roundtrip.tres", cacheMode: ResourceLoader.CacheMode.Ignore);
        Check(loaded != null && loaded.Track != null && loaded.Stream is AudioStreamWav, "Saved asset reloads its typed nested resources");
        Equal(loaded.Track.TrackId.ToString(), "saved", "Track ID survives save");
        Equal(loaded.Track.ParentTrackId.ToString(), "parent", "Parent survives save");
        Equal(loaded.Track.MaxVoices, 7, "Voice limit survives save");
        Near(loaded.VolumeDb, -7.5f, "Volume survives save"); Near(loaded.PitchScale, 1.25f, "Pitch survives save");
        foreach (var type in new Resource[] { new CSharpQuickAudioAsset(), new CSharpQuickAudioTrackDefinition(), new CSharpQuickAudioTrackLibrary() })
            Check(type.GetPropertyList().Any(p => p["name"].AsString() is "Stream" or "TrackId" or "Tracks"), "Inspector wrappers export inherited properties");
    }

    private void TestValidation()
    {
        void Invalid(QuickAudioTrackLibrary library, string code)
        {
            var manager = new QuickAudioManagerService();
            string actual = "";
            manager.AudioError += (c, _) => actual = c;
            var count = AudioServer.BusCount;
            Check(!manager.ConfigureLibrary(library), "Invalid library rejected: " + code);
            Equal(actual, code, "Deterministic error"); Equal(AudioServer.BusCount, count, "Validation is transactional");
            manager.Free();
        }
        Invalid(null, "library_null"); Invalid(Library(), "library_empty"); Invalid(Library((QuickAudioTrackDefinition)null), "track_null");
        Invalid(Library(Track("")), "track_id_empty"); Invalid(Library(Track(), Track()), "track_id_duplicate");
        Invalid(Library(Track(bus: "")), "bus_name_empty"); Invalid(Library(Track(bus: "Master")), "bus_name_reserved");
        Invalid(Library(Track(), Track("other")), "bus_name_duplicate"); Invalid(Library(Track(voices: 0)), "max_voices_invalid");
        var invalidMode = Track(); invalidMode.ProcessMode = (ProcessModeEnum)99; Invalid(Library(invalidMode), "process_mode_invalid");
        Invalid(Library(Track(parent: "missing")), "parent_missing"); Invalid(Library(Track(parent: "test")), "parent_cycle");
        Invalid(Library(Track("a", "A", parent: "b"), Track("b", "B", parent: "a")), "parent_cycle");
        var bare = new QuickAudioManagerService();
        bare.AudioError += (c, _) => _errors.Add(c);
        Check(bare.Play(Asset(Track())) == null, "Unconfigured playback rejected"); Equal(_errors.Last(), "not_configured", "Unconfigured error");
        bare.Free();
    }

    private async Task TestPlayback()
    {
        var manager = Manager();
        Check(manager.IsConfigured(), "Default manager auto-configures");
        Equal(string.Join(",", manager.GetTrackIds()), "music,sfx,ui", "Track order stable");
        Check(!manager.ConfigureLibrary(manager.TrackLibrary), "Repeated configuration rejected"); Equal(_errors.Last(), "already_configured", "Repeated configuration error");
        Check(manager.HasTrack("sfx") && !manager.HasTrack("missing"), "Track lookup");
        Equal(manager.GetTrackBusName("sfx").ToString(), "QuickSFX", "Bus lookup");
        Check(manager.Play(null) == null, "Null asset rejected"); Equal(_errors.Last(), "asset_null", "Null asset error");
        Check(manager.Play(new QuickAudioAsset()) == null, "Streamless rejected"); Equal(_errors.Last(), "stream_null", "Streamless error");
        Check(manager.Play(new QuickAudioAsset { Stream = Wave() }) == null, "Trackless rejected"); Equal(_errors.Last(), "asset_track_null", "Trackless error");
        Check(manager.Play(Asset(Track("unknown"))) == null, "Unknown track rejected"); Equal(_errors.Last(), "track_unknown", "Unknown track error");
        var music = Asset(manager.TrackLibrary.Tracks[0], Wave(1, true));
        var sfx = Asset(manager.TrackLibrary.Tracks[1], Wave(1, true)); sfx.VolumeDb = -6; sfx.PitchScale = 1.5f;
        var started = 0; var stopped = 0;
        manager.AudioStarted += (_, _, _) => started++;
        manager.AudioStopped += (_, _) => stopped++;
        var p1 = manager.Play(sfx); var p2 = manager.Play(sfx);
        Check(p1 != null && p2 != null && p1 != p2 && p1.Playing, "Parallel real players start");
        Near(p1.VolumeDb, -6, "Per-asset volume"); Near(p1.PitchScale, 1.5f, "Per-asset pitch");
        Equal(manager.GetActivePlayerCount("sfx"), 2, "Parallel voice count");
        var m1 = manager.Play(music); var m2 = manager.Play(music);
        Check(m1.IsQueuedForDeletion() && !m1.Playing, "Oldest music voice stopped and queued");
        Equal(manager.GetActivePlayerCount("music"), 1, "Music voice limit");
        Equal(manager.EnsurePlaying(music), m2, "EnsurePlaying reuses exact stream");
        Equal(started, 4, "Started signals"); Equal(stopped, 1, "Voice replacement stopped signal");
        GetTree().Paused = true;
        Check(m2.CanProcess() && !p1.CanProcess(), "Music Always vs inherited SFX pause processing");
        Check(!m2.StreamPaused && p1.StreamPaused, "Actual player stream pause flags follow tree");
        GetTree().Paused = false;
        Check(!p1.StreamPaused, "Unpause resumes SFX");
        Check(manager.SetTrackVolumeDb("sfx", 100), "Set track volume"); Near(manager.GetTrackVolumeDb("sfx"), 24, "Track upper clamp");
        manager.SetTrackVolumeDb("sfx", -100); Near(manager.GetTrackVolumeDb("sfx"), -80, "Track lower clamp");
        Check(manager.SetTrackMuted("ui", true) && manager.IsTrackMuted("ui"), "Mute set/get");
        Check(!manager.SetTrackMuted("missing", true) && !manager.SetTrackVolumeDb("missing", 0), "Unknown controls rejected");
        Near(manager.GetTrackVolumeDb("missing"), -80, "Unknown volume sentinel");
        var master = manager.GetMasterVolumeDb();
        manager.SetMasterVolumeDb(5); Near(manager.GetMasterVolumeDb(), 0, "Master upper clamp");
        manager.SetMasterVolumeDb(-100); Near(manager.GetMasterVolumeDb(), -80, "Master lower clamp");
        Equal(manager.StopTrack("sfx"), 2, "StopTrack count");
        Check(manager.StopPlayer(m2) && !manager.StopPlayer(m2), "StopPlayer idempotence");
        Equal(manager.StopAll(), 0, "StopAll empty");
        var foreign = new AudioStreamPlayer(); Check(!manager.StopPlayer(foreign), "Foreign player untouched"); foreign.Free();
        var external = manager.Play(music); external.Free(); Equal(manager.GetActivePlayerCount(), 0, "Externally freed players pruned");
        var removedBus = AudioServer.GetBusIndex("QuickUI"); AudioServer.RemoveBus(removedBus);
        Check(manager.Play(Asset(manager.TrackLibrary.Tracks[2])) == null, "Deleted bus rejected"); Equal(_errors.Last(), "track_bus_missing", "Deleted bus error");
        manager.Free();
        Near(AudioServer.GetBusVolumeDb(0), master, "Master restored on exit");
        Check(AudioServer.GetBusIndex("QuickMusic") < 0 && AudioServer.GetBusIndex("QuickSFX") < 0, "Owned buses removed");
        await Frames(3);
        var mutable = Track(); var library = Library(mutable); manager = Manager(library);
        mutable.MaxVoices = 0; mutable.TrackId = "changed"; library.Tracks.Clear();
        var stableAsset = Asset(Track());
        Check(manager.Play(stableAsset) != null && manager.Play(stableAsset) != null && manager.Play(stableAsset) != null, "Snapshot rejects post-config mutation and avoids voice-limit hang");
        Equal(manager.GetActivePlayerCount(), 2, "Snapshotted cap stays valid"); Equal(string.Join(",", manager.GetTrackIds()), "test", "Snapshot registry order stable");
        manager.Free(); await Frames(3);
    }

    private async Task TestBusOwnership()
    {
        var count = AudioServer.BusCount;
        AudioServer.AddBus(); AudioServer.SetBusName(AudioServer.BusCount - 1, "BorrowChild");
        AudioServer.AddBus(); AudioServer.SetBusName(AudioServer.BusCount - 1, "BorrowDependent");
        AudioServer.SetBusSend(AudioServer.GetBusIndex("BorrowDependent"), "BorrowChild");
        AudioServer.AddBus(); AudioServer.SetBusName(AudioServer.BusCount - 1, "ForeignForward");
        AudioServer.SetBusSend(AudioServer.GetBusIndex("ForeignForward"), "BorrowParent");
        AudioServer.AddBus(); AudioServer.SetBusName(AudioServer.BusCount - 1, "BorrowParent");
        var childIndex = AudioServer.GetBusIndex("BorrowChild");
        AudioServer.SetBusSend(childIndex, "Master");
        AudioServer.SetBusVolumeDb(childIndex, -13); AudioServer.SetBusMute(childIndex, true);
        var child = Track("child", "BorrowChild", parent: "parent");
        var parent = Track("parent", "BorrowParent");
        var manager = Manager(Library(child, parent));
        Check(AudioServer.GetBusIndex("BorrowParent") < AudioServer.GetBusIndex("BorrowChild"), "Reused child bus reordered after its parent");
        Check(AudioServer.GetBusIndex("BorrowChild") < AudioServer.GetBusIndex("BorrowDependent"), "Existing downstream project bus still routes to borrowed child");
        Equal(AudioServer.GetBusSend(AudioServer.GetBusIndex("BorrowDependent")).ToString(), "BorrowChild", "Unmanaged bus send unchanged");
        Equal(AudioServer.GetBusSend(AudioServer.GetBusIndex("BorrowChild")).ToString(), "BorrowParent", "Child routes to declared parent");
        Equal(AudioServer.GetBusSend(AudioServer.GetBusIndex("ForeignForward")).ToString(), "Master", "Previously forward external send keeps effective Master fallback after reorder");
        manager.Free(); await Frames(3);
        Equal(AudioServer.GetBusSend(AudioServer.GetBusIndex("ForeignForward")).ToString(), "BorrowParent", "Original external forward send restored on exit");
        Check(AudioServer.GetBusIndex("ForeignForward") < AudioServer.GetBusIndex("BorrowParent"), "Original external forward ordering restored");
        Equal(AudioServer.GetBusIndex("BorrowChild"), childIndex, "Original borrowed bus order restored");
        Equal(AudioServer.GetBusSend(childIndex).ToString(), "Master", "Borrowed send restored");
        Near(AudioServer.GetBusVolumeDb(childIndex), -13, "Borrowed volume restored");
        Check(AudioServer.IsBusMute(childIndex), "Borrowed mute restored");
        AudioServer.RemoveBus(AudioServer.GetBusIndex("BorrowParent")); AudioServer.RemoveBus(AudioServer.GetBusIndex("ForeignForward")); AudioServer.RemoveBus(AudioServer.GetBusIndex("BorrowDependent")); AudioServer.RemoveBus(AudioServer.GetBusIndex("BorrowChild"));
        Equal(AudioServer.BusCount, count, "No borrowed bus deleted by service");
        manager = Manager(Library(Track("child", "NewChild", parent: "parent"), Track("parent", "NewParent")));
        Check(AudioServer.GetBusIndex("NewParent") < AudioServer.GetBusIndex("NewChild"), "New bus hierarchy installed parent-first despite library order");
        Equal(string.Join(",", manager.GetTrackIds()), "child,parent", "Public library order unaffected by bus install order");
        manager.Free();
    }

    private async Task TestMixerAndCompletion()
    {
        var parent = Track("parent", "CaptureParent"); var child = Track("child", "CaptureChild", parent: "parent");
        var manager = Manager(Library(child, parent));
        var capture = new AudioEffectCapture { BufferLength = 1 };
        AudioServer.AddBusEffect(AudioServer.GetBusIndex("CaptureParent"), capture);
        var stopped = 0; manager.AudioStopped += (_, _) => stopped++;
        var player = manager.Play(Asset(child, Wave(.15f)));
        Check(player.Playing, "WAV starts on real AudioStreamPlayer");
        await Delay(.5);
        var buffer = capture.GetBuffer(capture.GetFramesAvailable());
        Check(buffer.Length > 0, "Dummy driver runs actual mixer and capture produces samples");
        var peak = buffer.Length == 0 ? 0 : buffer.Max(v => Math.Max(Math.Abs(v.X), Math.Abs(v.Y)));
        Check(peak > .01, $"Nonzero sine waveform reaches parent bus (peak {peak})");
        Equal(manager.GetActivePlayerCount(), 0, "Natural Finished clears registry"); Equal(stopped, 1, "Natural Finished emits one stopped signal");
        Check(!IsInstanceValid(player), "Finished player freed after deferred cleanup");
        // Mute the parent and capture Master to verify the declared route affects real output.
        var masterCapture = new AudioEffectCapture { BufferLength = 1 };
        AudioServer.AddBusEffect(0, masterCapture);
        manager.SetTrackMuted("parent", true);
        manager.Play(Asset(child, Wave(.15f)));
        await Delay(.35);
        var masterBuffer = masterCapture.GetBuffer(masterCapture.GetFramesAvailable());
        Check(masterBuffer.Length > 0 && masterBuffer.All(v => v.Length() < .0001f), "Muted parent silences actual Master mix");
        AudioServer.RemoveBusEffect(0, AudioServer.GetBusEffectCount(0) - 1);
        manager.Free(); await Frames(4);
    }

    private void TestReentrancy()
    {
        var track = Track(voices: 1); var manager = Manager(Library(track)); var asset = Asset(track, Wave(1, true));
        var first = manager.Play(asset);
        manager.AudioStopped += (_, player) => { manager.Play(asset); player.Free(); };
        var replacement = manager.Play(asset);
        Check(replacement != null && manager.GetActivePlayerCount() == 1, "Recursive replacement callback cannot spin or leave extra voices");
        Check(_errors.Contains("operation_in_progress"), "Recursive Play is explicitly rejected");
        manager.Free();
        var m2 = Manager(Library(Track("start", "StartBus")));
        m2.AudioStarted += (_, _, player) => m2.StopPlayer(player);
        Check(m2.Play(Asset(Track("start", "StartBus"))) == null && m2.GetActivePlayerCount() == 0, "Start callback may immediately stop safely");
        m2.Free();
        var m3 = Manager(Library(Track("reentrant", "ReentrantBus", voices: 1)));
        var a3 = Asset(Track("reentrant", "ReentrantBus"), Wave(1, true));
        m3.Play(a3);
        var bounced = false;
        m3.AudioStopped += (_, _) =>
        {
            if (bounced) return;
            bounced = true;
            GetTree().Root.RemoveChild(m3); GetTree().Root.AddChild(m3);
        };
        Check(m3.Play(a3) == null, "Playback replacement aborts after callback resets the manager");
        Equal(m3.GetActivePlayerCount(), 0, "Reset callback cannot register to stale player list");
        Check(m3.GetChildren().OfType<AudioStreamPlayer>().All(p => p.IsQueuedForDeletion() || !p.Playing), "No untracked player remains after reset callback");
        Check(m3.Play(a3) != null && m3.GetActivePlayerCount() == 1, "Manager remains usable after callback reset");
        m3.Free();
        var m4 = Manager(Library(Track("mutate", "MutateBus", voices: 1)));
        var a4 = Asset(Track("mutate", "MutateBus"), Wave(1, true));
        var originalStream = a4.Stream;
        m4.Play(a4);
        m4.AudioStopped += (_, _) => { a4.Stream = null; a4.VolumeDb = -80; a4.PitchScale = 2; };
        var p4 = m4.Play(a4);
        Check(p4 != null && p4.Playing && p4.Stream == originalStream, "Playback snapshots requested stream before replacement callbacks");
        Near(p4.VolumeDb, 0, "Replacement callback cannot mutate in-flight volume");
        Near(p4.PitchScale, 1, "Replacement callback cannot mutate in-flight pitch");
        m4.Free();
    }

    private void TestDetachedLifecycle()
    {
        var before = AudioServer.BusCount;
        var manager = new QuickAudioManagerService();
        Check(manager.ConfigureLibrary(Library(Track("detached", "DetachedBus"))), "Configure before entering tree");
        string error = ""; manager.AudioError += (code, _) => error = code;
        Check(manager.Play(Asset(Track("detached", "DetachedBus"))) == null && error == "manager_not_in_tree", "Detached playback rejected clearly");
        manager.Free(); Equal(AudioServer.BusCount, before, "Freeing never-attached configured manager restores buses");
        manager = Manager(Library(Track("reenter", "ReenterBus")));
        GetTree().Root.RemoveChild(manager);
        Check(!manager.IsConfigured() && AudioServer.GetBusIndex("ReenterBus") < 0, "Detaching manager restores state");
        GetTree().Root.AddChild(manager);
        Check(manager.IsConfigured() && AudioServer.GetBusIndex("ReenterBus") >= 0, "Reattaching manager configures again");
        manager.Free();
    }

    private void TestResourceConversion()
    {
        const string path = "res://addons/quick_audio_manager/gds/resources/default_track_library.tres";
        if (!ResourceLoader.Exists(path)) return; // Standalone C# package does not require GDScript files.
        var converter = new QuickAudioResourceConverter();
        var gdsLibrary = GD.Load<Resource>(path);
        var converted = converter.ConvertLibrary(gdsLibrary);
        Equal(converted.Tracks.Count, 3, "GDScript library converts to C#");
        Equal(converted.Tracks[0].TrackId.ToString(), "music", "Conversion preserves track IDs");
        var same = converter.ConvertTrack(GD.Load<Resource>("res://addons/quick_audio_manager/gds/resources/tracks/music.tres"));
        Equal(same, converted.Tracks[0], "Conversion preserves shared track identity");
        var gdsScript = GD.Load<Script>("res://addons/quick_audio_manager/gds/runtime/quick_audio_asset.gd");
        var gdsAsset = gdsScript.Call("new").AsGodotObject() as Resource;
        var stream = Wave(.1f);
        gdsAsset.Set("stream", stream);
        gdsAsset.Set("track", GD.Load<Resource>("res://addons/quick_audio_manager/gds/resources/tracks/music.tres"));
        gdsAsset.Set("volume_db", -8.5f); gdsAsset.Set("pitch_scale", 1.75f);
        var convertedAsset = converter.ConvertAsset(gdsAsset);
        Check(convertedAsset.Stream == stream && convertedAsset.Track == same, "Asset conversion preserves shared stream and track identity");
        Near(convertedAsset.VolumeDb, -8.5f, "Converted asset volume"); Near(convertedAsset.PitchScale, 1.75f, "Converted asset pitch");
        Equal(ResourceSaver.Save(same, "user://converted_audio_music.tres"), Error.Ok, "Shared converted track saves with an external resource path");
        same.TakeOverPath("user://converted_audio_music.tres");
        Equal(ResourceSaver.Save(convertedAsset, "user://converted_audio_asset.tres"), Error.Ok, "Converted asset saves shared external track");
        Equal(ResourceSaver.Save(converted, "user://converted_audio_library.tres"), Error.Ok, "Converted library saves");
        var loaded = ResourceLoader.Load<QuickAudioTrackLibrary>("user://converted_audio_library.tres", cacheMode: ResourceLoader.CacheMode.Ignore);
        Check(loaded != null && loaded.Tracks.Count == 3 && loaded.Tracks[0].MaxVoices == 1, "Converted library reloads without GDScript classes");
        var loadedAsset = ResourceLoader.Load<QuickAudioAsset>("user://converted_audio_asset.tres", cacheMode: ResourceLoader.CacheMode.Ignore);
        Check(loadedAsset.Track == loaded.Tracks[0] && loadedAsset.Track.ResourcePath == "user://converted_audio_music.tres", "Separately saved asset and library retain shared external track after reload");
    }
}
