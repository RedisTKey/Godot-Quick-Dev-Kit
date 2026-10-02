# Quick Audio Manager (GDScript)

一个面向 Godot 4.x 的轻量音频管理插件：用 `Resource` 描述音频资产与音轨，由 `QuickAudioManagerService` 统一管理 `AudioServer` bus 与临时 `AudioStreamPlayer`。

## 功能

- `QuickAudioAsset`：封装 `AudioStream`、目标轨道、局部音量与音高。
- `QuickAudioTrackDefinition`：声明轨道 ID、bus、父子 send、音量、静音、voice 上限和处理模式（暂停时是否继续播放）。
- `QuickAudioTrackLibrary`：有序的轨道定义集合。
- `QuickAudioManagerService`：校验资源、创建/复用/恢复 AudioServer bus、播放、停止、并发 voice 限制、音量与静音控制。
- 内置可替换的 Music / SFX / UI 默认轨道库。
- 通过 `EditorPlugin` 在使用者启用插件时自动注册 `QuickAudioManager` Autoload，禁用时自动移除。

## 安装

1. 将 `addons/quick_audio_manager/gds` 目录按原路径复制到目标 Godot 4.x 项目；GDScript 版不依赖 C# 文件或 .NET。
2. 打开 **Project → Project Settings → Plugins**。
3. 在 “Quick Audio Manager (GDScript)” 一行点击 **Enable**。

启用后插件会注册名为 `QuickAudioManager` 的 Autoload。GDScript 与 C# 两版只能启用其一；切换语言前先禁用当前版本。已有同名 Autoload 的路径或 singleton 标志不匹配时，插件会保留原值并报告错误。禁用时只移除本版拥有的 Autoload，支持编辑器重启后的清理及 Godot 保存的 `uid://` 路径；失败的启用不会在随后禁用时删除原有配置。

## 从旧目录迁移

GDScript 公共类名、信号、方法和已有 `.uid` 保持不变，文件移动到了 `gds/` 子目录。先禁用旧版插件，再替换目录；更新项目中手写的 `res://addons/quick_audio_manager/runtime/`、`resources/`、`examples/` 路径以加入 `gds/`。若项目仍有旧路径的 `QuickAudioManager` Autoload，在 Project Settings 中移除旧条目后再启用新插件。旧版用户资源不需要转换为 C# 资源。

## 快速开始

1. 在文件系统面板右键 → **New Resource**，选择 `QuickAudioTrackDefinition`，或直接复用内置资源：
   - `res://addons/quick_audio_manager/gds/resources/tracks/music.tres`
   - `res://addons/quick_audio_manager/gds/resources/tracks/sfx.tres`
   - `res://addons/quick_audio_manager/gds/resources/tracks/ui.tres`
2. 新建 `QuickAudioAsset` 资源，设置：
   - `stream`：你的 `AudioStream`。
   - `track`：上一步选择的轨道资源。
3. 在代码中播放：

```gdscript
@export var click_asset: QuickAudioAsset

func _ready() -> void:
	var manager := get_node("/root/QuickAudioManager")
	var player := manager.play(click_asset)
	if player == null:
		push_warning("播放失败，请检查 asset 与轨道配置。")
```

也可以调用 `manager.ensure_playing(asset)`：若同一轨道上已有相同 `AudioStream` 正在播放，则复用它，否则新建播放。

## 创建自定义轨道库

1. 为每个轨道创建 `QuickAudioTrackDefinition` 资源，至少设置 `track_id`、`bus_name`、`max_voices`。
2. 新建 `QuickAudioTrackLibrary` 资源，将轨道按顺序加入 `tracks`。
3. 在某个 `QuickAudioManagerService` 节点（或你的 Autoload 配置）上，将 `track_library` 指向该库；也可以直接调用 `configure_library(library)`。

轨道 `bus_name` 不要使用保留名 `Master`。多个轨道不能共享同一个 `bus_name`。`parent_track_id` 用于把轨道 send 到另一条轨道，必须指向已存在的轨道且不能成环。

## 运行时 API

```gdscript
configure_library(library: QuickAudioTrackLibrary) -> bool
play(asset: QuickAudioAsset) -> AudioStreamPlayer
ensure_playing(asset: QuickAudioAsset) -> AudioStreamPlayer
stop_player(player: AudioStreamPlayer) -> bool
stop_track(track_id: StringName) -> int
stop_all() -> int
get_active_player_count(track_id: StringName = &"") -> int
is_configured() -> bool
has_track(track_id: StringName) -> bool
get_track_ids() -> Array[StringName]
get_track_bus_name(track_id: StringName) -> StringName
```

失败时播放 API 返回 `null` 或 `false`，并通过 `audio_error(code, message)` 信号报告确定性错误码；播放与停止会发出 `audio_started` / `audio_stopped` 信号。

## 音量与静音

```gdscript
set_track_volume_db(track_id: StringName, volume_db: float) -> bool
get_track_volume_db(track_id: StringName) -> float
set_master_volume_db(volume_db: float) -> bool
get_master_volume_db() -> float
set_track_muted(track_id: StringName, muted: bool) -> bool
is_track_muted(track_id: StringName) -> bool
```

- 轨道音量范围 `-80..24 dB`；Master 音量范围 `-80..0 dB`。
- 音量以 dB 表示；若 UI 使用线性 `0.0..1.0`，请在项目侧自行用 `linear_to_db()` / `db_to_linear()` 转换。
- 轨道处理模式由 `QuickAudioTrackDefinition.process_mode` 决定（如 Music 用 `Always` 以便暂停时继续播放）。

## 持久化边界

插件只操作当前运行时，**不写 `user://`**，也不包含设置界面。若项目需要保存音量/静音：由项目自行把线性音量或 dB 写入自己的存档，并在启动后调用上述 API 恢复。

## AudioServer bus 所有权

- 插件只会创建库中声明且当前不存在的 bus，并在服务退出时删除自己创建的 bus。
- 对已存在的同名 bus，插件会先记录其 send、音量与静音状态，退出时恢复，不会删除它。
- 只有通过 `set_master_volume_db()` 修改过的 Master 音量才会在退出时恢复原值。
- 因此多个系统共用 bus 时，请避免同时在别处修改同一 bus 的 send / 音量 / 静音，否则退出恢复可能互相覆盖。

## 测试

插件自带 headless 测试（需带音频驱动参数）：

```bash
godot --headless --audio-driver Dummy --path <project> \
  --script res://addons/quick_audio_manager/gds/tests/test_quick_audio_manager.gd
godot --headless --audio-driver Dummy --path <project> \
  --script res://addons/quick_audio_manager/gds/tests/test_quick_audio_manager_package.gd
```

## Godot 4.x 兼容策略

插件只使用 Godot 4.x 共有 API，不依赖 4.7 专属能力，目标是尽可能兼容主流 Godot 4.x。原版本曾在 **Godot 4.7.1** 上验证；本次双语言版本的实测范围见上级目录的验证记录。其他 4.x 小版本尚未逐一实测。
