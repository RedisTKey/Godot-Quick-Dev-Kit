# Quick Audio Manager

Godot 的轻量音频管理系统，提供功能对应的 GDScript / C# 两套实现。原 GDScript 包完整搬到 `gds/`，C# 包位于 `csharp/`；根目录不再注册第三个插件。

```text
addons/quick_audio_manager/
├── gds/       # 原 GDScript、UID、资源、示例、测试与插件入口
├── csharp/    # 独立 C# 实现、Inspector 资源、示例、测试与插件入口
├── README.md
└── VALIDATION.md
```

## 选择实现

- **GDScript**：复制 `gds/`，保持目标路径 `addons/quick_audio_manager/gds/`；启用 **Quick Audio Manager (GDScript)**。详见 [GDScript 文档](gds/README.md)
- **C#**：使用 Godot **.NET** 版和对应 .NET SDK，复制 `csharp/`，保持目标路径 `addons/quick_audio_manager/csharp/`；先构建项目，再启用 **Quick Audio Manager (C#)**。详见 [C# 文档](csharp/README.md)
- 两套目录可以共存，但只启用其中一个。都使用 `/root/QuickAudioManager`；第二次冲突启用会报告错误并保持现有服务不变，需要手动禁用冲突行。插件不会覆盖或删除项目自有的同名 Autoload

## 功能范围

两套都包含音频资产、轨道定义、轨道库、Music / SFX / UI 默认资源、播放/停止、相同流复用、最老 voice 淘汰、音量/静音、暂停处理模式、确定性错误信号、临时播放器清理与 AudioServer bus 恢复。资源可以在 Inspector 编辑；原系统没有独立设置 UI、空间音效、淡入淡出或持久化设置服务，本次保持该范围。

C# 类型位于 `QuickDevKit.QuickAudio` 命名空间。Inspector 注册名为 `CSharpQuickAudioAsset` / `CSharpQuickAudioTrackDefinition` / `CSharpQuickAudioTrackLibrary`，避免与 GDScript 全局类重名；代码中使用对应 `QuickAudio*` 基类即可。

## 从旧目录迁移

1. 在旧版本仍存在时禁用旧插件，备份项目
2. 将旧 `runtime/`、`resources/`、`examples/`、`tests/`、插件入口与配置替换为本包的 `gds/` 布局。原 `.gd.uid` 保持不变；不要同时保留旧、新两套 GDScript 全局类
3. 更新项目外部引用：`res://addons/quick_audio_manager/{runtime,resources,examples,tests}/...` → `res://addons/quick_audio_manager/gds/{runtime,resources,examples,tests}/...`
4. 旧 Autoload 路径、旧 enabled-plugin 路径也需要清理或改为 `gds/` 新路径，再启用选择的实现。仅靠 UID 不能修复无 UID 的旧 `.tres` 路径引用
5. 切换到 C# 时，已有 GDScript `.tres` 不会自动变成 C# 类型。可用 [C# 资源转换器](csharp/README.md#迁移已有-gdscript-资源) 显式复制到新资源文件；不会覆盖旧资源或修改 `user://` 设置

默认轨道 ID、bus 名、voice 上限、暂停模式与 GDScript 数据含义保持一致。C# 方法/属性/信号采用 PascalCase，原 GDScript API 与错误码保留。

## 验证

完整隔离测试，不创建仓库根 Godot 项目，不启动其他插件：

```bash
python3 tests/run_quick_audio.py --godot /path/to/Godot_mono --backend all
python3 tests/run_quick_audio.py --godot /path/to/Godot --backend gds
python3 tests/run_quick_audio.py --godot /path/to/Godot_mono --backend csharp
```

默认测试 SDK 为 `Godot.NET.Sdk/4.7.2`，目标 `net9.0`；可用 `--sdk-version` / `--framework` 指定安装版本。实际覆盖与限制见 [VALIDATION.md](VALIDATION.md)。
