# Quick Audio Manager (C#)

独立、命名空间化的 C# 实现。需要 Godot .NET 和对应 .NET SDK；本包不会替项目创建或覆盖 `.csproj`。复制到 `addons/quick_audio_manager/csharp/`，构建项目后启用插件。启用前请禁用 GDScript 版本；两者共用 `QuickAudioManager` Autoload。

## Inspector 与资源

在 New Resource 中选择：

- `CSharpQuickAudioAsset`：`Stream`、`Track`、`VolumeDb`、`PitchScale`
- `CSharpQuickAudioTrackDefinition`：`TrackId`、`DisplayName`、`BusName`、`ParentTrackId`、`VolumeDb`、`Muted`、`MaxVoices`、`ProcessMode`
- `CSharpQuickAudioTrackLibrary`：有序 `Tracks` 数组

这些是 `QuickDevKit.QuickAudio` 中相应 `QuickAudio*` 基类的编辑器子类，可赋给 C# 导出字段与方法。只有资源是 `[Tool]`，运行时服务不会在编辑器中开始播放或更改 AudioServer。

默认资源位于 `resources/default_track_library.tres` 与 `resources/tracks/{music,sfx,ui}.tres`。服务导出 `TrackLibrary` 为空时，进入场景树自动加载默认库；也可在加入场景树前调用 `ConfigureLibrary(library)`。播放前服务必须在场景树内。

## 使用

```csharp
using Godot;
using QuickDevKit.QuickAudio;

public partial class MyAudio : Node
{
    [Export] public QuickAudioAsset ClickAsset { get; set; }
    public override void _Ready()
    {
        var audio = GetNode<QuickAudioManagerService>("/root/QuickAudioManager");
        audio.AudioError += (code, message) => GD.PushWarning($"{code}: {message}");
        AudioStreamPlayer player = audio.Play(ClickAsset);
        // Play 返回 null 表示播放未开始或被 AudioStarted 回调立即停止。
    }
}
```

`examples/basic_usage.tscn` 可直接加载；在 Inspector 指定 `AudioAsset` 后按 `ui_accept` 播放。该示例不内置音频文件，也不会在项目未注册 Autoload 时抛异常。

## API

```csharp
bool ConfigureLibrary(QuickAudioTrackLibrary library);
AudioStreamPlayer Play(QuickAudioAsset asset);
AudioStreamPlayer EnsurePlaying(QuickAudioAsset asset);
bool StopPlayer(AudioStreamPlayer player);
int StopTrack(StringName trackId);
int StopAll();
int GetActivePlayerCount();
int GetActivePlayerCount(StringName trackId);
bool IsConfigured();
bool HasTrack(StringName trackId);
Godot.Collections.Array<StringName> GetTrackIds();
StringName GetTrackBusName(StringName trackId);
bool SetTrackVolumeDb(StringName trackId, float volumeDb);
float GetTrackVolumeDb(StringName trackId);
bool SetMasterVolumeDb(float volumeDb);
float GetMasterVolumeDb();
bool SetTrackMuted(StringName trackId, bool muted);
bool IsTrackMuted(StringName trackId);
```

信号：`AudioStarted(asset, trackId, player)`、`AudioStopped(trackId, player)`、`LibraryConfigured(trackIds)`、`AudioError(code, message)`。错误常量名对应原错误码，例如 `ErrorTrackUnknown == "track_unknown"`。另有 `manager_not_in_tree` 和 `operation_in_progress`，分别阻止离树播放与 voice 替换回调递归创建播放器。

轨道音量限幅 `-80..24 dB`，Master `-80..0 dB`。相同流复用按轨道 ID + `AudioStream` 实例判断；暂停中的播放器仍可复用。手动控制单个播放器可用返回值的 Godot `StreamPaused` / `PitchScale` 等属性。

## 生命周期与防护

- 配置校验失败不改动 AudioServer；同一服务只能成功配置一次，退出树后恢复状态，再进入树会重新配置
- 成功配置时快照轨道定义。之后修改原资源不会改变已安装轨道或 voice 上限；需要退出/重建服务后应用新库。这避免运行时把 `MaxVoices` 改成 0 导致淘汰循环不终止
- 公共轨道顺序保持资源数组顺序；实际 bus 以父级优先顺序安装。需要时对 bus 做稳定的父级优先排序，同时保留已有项目 bus 的有效下游关系，使 Godot 右到左混音真正经过父轨道；退出时恢复借用 bus 的顺序、send、音量与静音
- 如果排序会让项目其他 bus 原本无效的“向右 send”变成有效路由，会临时明确 send 到 Master，保持原本实际输出；退出时恢复其原始 send 字符串和顺序
- 只移除自己创建的 bus；只在通过本服务改过 Master 时恢复其原音量。未入树就配置、随后释放的服务也会恢复 bus
- `Finished`、voice 淘汰、停止与服务退出会清理播放器。停止回调允许立即释放播放器；启动回调允许立即停止。voice 淘汰期间不允许回调递归 `Play`；请求的 stream/音量/音高在回调前快照，回调导致服务退出并重入时中止本次替换
- 同一批 bus 应仅由一个活动服务管理；同时从别处重命名、替换或修改这些 bus 的配置会破坏基于 bus 名的所有权约定。效果器由项目管理，不会被服务删除
- 不写入玩家设置，不包含音量设置 UI、淡入淡出或 2D/3D 空间播放器。上述 C# 防护不改变保留版 GDScript 运行时

## 迁移已有 GDScript 资源

先按父目录文档把旧资源路径搬到 `gds/`，确保旧资源能正常加载。然后保持两套文件存在但只启用 C# 插件，用一个 `QuickAudioResourceConverter` 转换同一批库、轨道和资产：

```csharp
var converter = new QuickAudioResourceConverter();
var oldLibrary = GD.Load<Resource>("res://my_audio/old_library.tres");
var library = converter.ConvertLibrary(oldLibrary);
// 先给共享轨道独立文件；目录需提前创建，每次检查返回的 Error。
for (int i = 0; i < library.Tracks.Count; i++)
{
    string path = $"res://my_audio/new_track_{i}.tres";
    Error trackError = ResourceSaver.Save(library.Tracks[i], path);
    if (trackError != Error.Ok) throw new System.IO.IOException(trackError.ToString());
    library.Tracks[i].TakeOverPath(path);
}
Error error = ResourceSaver.Save(library, "res://my_audio/new_library.tres");

var oldAsset = GD.Load<Resource>("res://my_audio/old_asset.tres");
var asset = converter.ConvertAsset(oldAsset);
error = ResourceSaver.Save(asset, "res://my_audio/new_asset.tres");
```

转换器复制明确的原资源字段，保留轨道 ID、父 ID、音量、音高、voice、暂停模式、资源名与原 `AudioStream`；同一转换器会在内存中复用共享轨道对象。若需要保存后仍共用同一个可编辑轨道，先按示例把轨道保存为独立文件，保存成功后用 `TakeOverPath(path)` 为转换后的对象建立外部资源路径，再保存库和资产；否则各文件会内嵌独立子资源，播放仍按 TrackId 匹配，但编辑不会跨文件同步。它不自动修改场景引用、项目 Autoload、自定义派生资源字段或源文件。保存并核对新资源后，在 Inspector 改用新引用；确认项目不再依赖 GDScript 资源后再移除 `gds/`。C# 的 `.tres` 字段名是 PascalCase，不能只把原 `.gd` 路径替换成 `.cs`。

## 测试

仓库根目录运行 `python3 tests/run_quick_audio.py --godot /path/to/Godot_mono --backend all`。详见 [验证记录](../VALIDATION.md)。
