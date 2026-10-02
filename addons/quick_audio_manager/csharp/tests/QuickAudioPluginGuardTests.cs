using System;
using Godot;

namespace QuickDevKit.QuickAudio;

/// <summary>Pure guard coverage invoked by the isolated runtime test scene.</summary>
public static class QuickAudioPluginGuardTests
{
    public static void Run(Action<bool, string> check)
    {
        string own = QuickAudioPluginGuard.AutoloadValue;
        string[] none = Array.Empty<string>();
        string[] peer = { QuickAudioPluginGuard.PeerPluginPath };
        long uid = ResourceLoader.GetResourceUid(own.Substring(1));
        check(uid != ResourceUid.InvalidId, "C# service has an imported UID");
        string uidValue = "*" + ResourceUid.IdToText(uid);
        check(QuickAudioPluginGuard.OwnsAutoload(uidValue), "C# UID ownership resolves to the service");
        check(QuickAudioPluginGuard.ShouldRemoveAutoload(uidValue, false), "C# UID ownership survives editor restart");
        check(QuickAudioPluginGuard.RegistrationError(false, "", none).Length == 0, "C# unoccupied registration succeeds");
        check(QuickAudioPluginGuard.RegistrationError(true, own, none).Length == 0, "C# matching registration is idempotent");
        check(QuickAudioPluginGuard.RegistrationError(true, uidValue, none).Length == 0, "C# matching UID registration is idempotent");
        check(QuickAudioPluginGuard.RegistrationError(false, "", peer).Length != 0, "GDS sibling blocks C# registration");
        check(QuickAudioPluginGuard.RegistrationError(true, own, peer).Length != 0, "Owned C# Autoload cannot bypass mutual exclusion");
        string unknownUid = "*" + ResourceUid.IdToText(ResourceUid.CreateId());
        Variant[] unowned = {
            "*res://addons/quick_audio_manager/gds/runtime/quick_audio_manager_service.gd",
            "*res://project_audio.gd", own.Substring(1), uidValue.Substring(1), unknownUid, "", 42
        };
        foreach (Variant value in unowned)
        {
            check(QuickAudioPluginGuard.RegistrationError(true, value, none).Length != 0,
                "C# occupied or malformed Autoload is rejected: " + value);
            check(!QuickAudioPluginGuard.ShouldRemoveAutoload(value, false),
                "C# disable preserves unowned or malformed Autoload: " + value);
        }
        check(QuickAudioPluginGuard.ShouldRemoveAutoload(own, false), "C# exact-path ownership survives editor restart");
        check(!QuickAudioPluginGuard.ShouldRemoveAutoload(own, true), "C# rejected enable preserves existing own Autoload");
        check(!QuickAudioPluginGuard.ShouldRemoveAutoload(uidValue, true), "C# rejected enable preserves existing own UID Autoload");
        check(!QuickAudioPluginGuard.ShouldRemoveAutoload("", false), "Repeated C# disable is safe");
    }
}
