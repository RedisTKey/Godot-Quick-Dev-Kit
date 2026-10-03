# Quick VFX Manager

Godot **4.7.2 .NET / C#** 的独立 3D VFX 插件。每个特效是一个场景，每个调用者挂自己的 `VfxPlayback` 组件，在 Inspector 注册 `VfxDefinition`，通过 ID 请求播放。

本版不依赖 Quick Input、Quick Audio 或任何游戏项目；不注册 Autoload、不处理碰撞/伤害/武器逻辑、不包含池化。没有空壳池化参数。

## 打开独立工程

用 Godot **.NET 版本**打开仓库根目录 `project.godot`，安装 .NET 9 SDK，先 Build，再运行。项目只编译这个插件的 C# 文件，默认场景为三站式 VFX Lab。

- 青色 Pulse：世界坐标播放，发起者移动后效果留在原位置
- 橙色 Composite Burst：粒子、Shader 网格、动画和灯光组合，各自完成后根节点才结束
- 紫色 Attached Loop：随锚点移动的循环效果，可以柔和停止或立即取消

`csharp/examples/definitions/*.tres` 与场景都是可编辑资源；不用先运行脚本生成素材。

## 安装到现有项目

1. 复制完整 `addons/quick_vfx_manager` 到项目相同位置
2. 使用 Godot .NET 项目并编译 C#；当前验证基线为 Godot 4.7.2 + .NET 9
3. 可在 Project Settings → Plugins 启用 `Quick VFX Manager (C#)`，它不改 Autoload；运行时本身无需启用插件
4. 在发起者下添加 `VfxPlayback` 节点。给 `Definitions` 数组添加 `VfxDefinition` 资源，填写不重复的 ID 和 Scene
5. 每个效果场景的根节点用 `VfxEffect`，在其后代添加对应参与者，将 Target 拖到同一效果里的实际节点

目录采用 `csharp/`，为将来语言扩展留出位置；本版只提供 C# 实现。

## 调用者 API

```csharp
using Godot;
using QuickVfx;

var vfx = GetNode<VfxPlayback>("VfxPlayback");
var hit = vfx.PlayAt("world_pulse", new Transform3D(Basis.Identity, globalPoint));
var loop = vfx.PlayAttached("attached_loop", GetNode<Node3D>("Anchor"));
loop?.Stop(VfxStopMode.Graceful);  // 停止新增内容，等所有尾巴
hit?.Stop(VfxStopMode.Immediate); // 取消并立即隐藏，QueueFree
```

`Play(id)` 使用 Definition 默认方式与模式。世界位置缺省取 `DefaultAnchor`，否则调用者的 Node3D 父节点；没有锚点时为世界原点。附着播放必须有仍在树中的锚点。

`Play(id, VfxPlayOptions)` 可逐次覆盖 Placement、Mode、Anchor、Transform，附带 `Dictionary<StringName, Variant>` 参数。Transform 对 World 是全局变换，对 Attached 是局部变换，最终右乘 Definition.Offset。请求不会修改共享 Definition。

`Register(definition, replace: false)`、`Unregister(id)` 管理当前组件的运行时注册；`ReloadDefinitions()` 原子加载 Inspector 数组，若有空 ID、空 Scene、重复 ID 或非法超时则保留旧表。运行时注册不会反写 Inspector 数组。移除定义不会停止已播放实例。

### Handle 与信号

- `Play*` 在查找/实例化失败时返回 null，并发出 `PlaybackFailed(id, message)`
- 场景实例已生成但内容无效时，返回已经 Finished 的 handle，查看 `EndReason` 和 `Error`
- Handle：`Generation`、`DefinitionId`、`State`、`IsActive`、`EndReason`、`Error`、`Stop()`、`GetEffect()`
- 组件信号：`PlaybackStarted(handle)`、`PlaybackEnded(handle, reason)`、`PlaybackFailed(id, message)`
- Handle 信号：`Ended(reason)`；根节点信号：`Started(generation)`、`StopRequested()`、`Finished(reason)`
- 完成前先移除活动登记并使 handle 失效，再发结束信号。结束回调内可以播放新效果，旧 handle 无法操作新实例
- 同步完成的场景可能在 `Play` 返回前结束，因此调用后先检查 `State/EndReason`，不要只等待未来的信号

结束原因明确区分 Completed、Stopped、Cancelled、TimedOut、InvalidConfiguration、ParticipantLost、RootExited。超时是兜底故障，不会伪装成正常播放完成。

## 给美术：一个 Scene 就是一个特效

详细逐项配置见 [设计师手册](docs/DESIGNER_GUIDE.md)。推荐结构：

```text
CompositeBurst (VfxEffect / Node3D)
├── Sparks (GPUParticles3D)
├── Ring (MeshInstance3D, ShaderMaterial)
├── Flash (OmniLight3D)
├── AnimationPlayer
├── ParticleTail (VfxParticles, Target=Sparks)
├── RingFade (VfxShaderParameter, Target=Ring)
└── FlashAnimation (VfxAnimation, Target=AnimationPlayer)
```

VfxEffect 等待所有参与者，不依赖单个粒子、任意一个 AnimationPlayer 的完成事件或猜测固定总时长。普通装饰节点无需参与者；任何会决定存活时间的内容都应由参与者管理。禁止嵌套 VfxEffect 根、禁止粒子/动画参与者引用场景外节点。

## 生命周期约定

- World 根采用 TopLevel，后续调用者移动/旋转/缩放不影响效果；它仍归调用者组件所有，调用者删除会取消它
- Attached 根成为指定锚点的子节点，使用局部变换并显式取消 authored TopLevel；锚点离树即结束。调用者删除也会取消附着在别处的效果
- 每次播放重新实例化 Scene；本版不重用、不池化
- OneShot 必须完成所有参与者；Loop 必须由调用者停止，参与者提前完成也不会自动销毁根
- Graceful：停止新增发射、播放配置的 outro / shader 淡出，等所有参与者完成；重复请求不重置尾巴
- Immediate：取消 token、停止受控播放、隐藏根、使 handle 失效、排队释放；可打断 Graceful
- OneShotTimeout / StopTimeout 是有限、正数的安全上限；循环正常播放没有强制超时
- `ProcessWhilePaused=false` 默认跟随 SceneTree 暂停。设为 true 则特效整体继续播放。根设置统一的进程模式，全部后代继承同一时钟；不要在运行时单独覆盖子节点 ProcessMode
- Timeout 和内置 adapter 使用 `_Process(delta)`，不使用忽略暂停的裸 SceneTreeTimer
- 场景切换、调用者退出、锚点删除、参与者被移除都会终止/清理；节点引用须用 Godot 有效性检查

## 扩展接口

继承 `VfxParticipant`，实现 `StartEffect(VfxContext)`、`StopEffect()`，必要时覆盖 `CancelEffect()`。完成调用 `Complete(capturedGeneration)`；失败调用 `Fail(message)`。

自定义异步代码应捕获本次 `context.Generation`，观察 `context.Cancellation`，在 Godot 主线程恢复后检查取消/节点有效性，再完成。不得从后台线程访问 Godot Node。`Complete` 本身仍会检查 generation、取消、节点是否在树中。CancelEffect 要幂等且短小，清理自己创建的 Tween/连接。所有参数和扩展都保持通用；游戏项目自己解释物理命中和业务事件。

## 验证

```bash
GODOT=/path/to/Godot_mono DOTNET=dotnet tests/quick_vfx_manager/run.sh
```

脚本将**仅本插件**复制到临时独立工程，进行 Debug/Release build、编辑器导入和生命周期测试。无需其他 Quick 插件。实际 GPU 视觉仍需图形渲染器，headless dummy 不能代替粒子视觉验证。

自动 push/PR CI 仅编译 C# Debug/Release，不启动 Godot。完整 Godot 检查保留在同一工作流的 `workflow_dispatch` 手动任务和上面的本地脚本中；默认分支尚未包含工作流时，先使用脚本。

见 [实现计划与决策](docs/PLAN.md) 和 [验证记录](docs/VALIDATION.md)。
