# 设计师手册

## 1. Definition 是调用者的菜单项

在 VfxPlayback 的 Definitions 数组里创建/拖入 VfxDefinition：

| 字段 | 用途 |
| --- | --- |
| Id | 调用时使用的唯一名称，如 pulse、composite、loop |
| Scene | 单一特效场景，根必须是 VfxEffect |
| Placement | World 留在世界；Attached 跟随指定锚点 |
| Mode | OneShot 自然结束；Loop 等调用者停止 |
| Offset | 位置/旋转/缩放偏移，右乘请求变换 |
| ProcessWhilePaused | 是否在游戏暂停时继续播放整个效果 |
| OneShotTimeout | 自然完成失联时的兜底上限，不是美术总时长 |
| StopTimeout | 请求柔和停止后最长等待时间 |

不要把某角色、武器或关卡的具体逻辑写入 Definition；同一资源可以复用到不同调用者。

## 2. GPU 粒子

添加 GPUParticles3D，按正常 Godot 工作流设计 Process Material、Draw Pass、Amount、Lifetime、颜色/速度/缩放曲线和可见性包围盒。默认关闭 Emitting，交给 VfxParticles 启动。

添加 VfxParticles 并绑定 Target。插件每次新建实例时调用 Restart；OneShot/Loop 由请求模式统一选择，不改共享 ProcessMaterial。LocalCoords 留给美术决定：

- 根的 Attached 决定**发射器**跟随谁
- LocalCoords=true 使**已经发出的粒子**也跟随发射器
- LocalCoords=false 可以做移动发射器留下的世界拖尾

两个设置不是同一件事。Interpolate/FixedFps 是平滑和模拟选项，也不是附着模式。

Godot 的 Finished 只适用于 one-shot。循环停止后没有这个信号，所以 adapter 停止 Emitting，再等待 Lifetime + TrailLifetime（启用时）+ TailSeconds 的模拟时间。自然 one-shot 除等待 Finished，还等待保守的 2×Lifetime + trail + TailSeconds，防止低 SpeedScale 时引擎信号早于视觉尾巴。

TailSeconds 是额外**模拟秒**；SpeedScale 越低，真实等待越久。SpeedScale=0 会停住粒子，最终由定义的 watchdog 以 TimedOut 清理。自定义粒子 shader、sub-emitter、额外延迟可能不遵守标准 lifetime，请增大 TailSeconds/timeout 或写自定义参与者。不要把一次重启发射器伪装成尾巴结束。多个受控发射器各配一个 adapter。

## 3. AnimationPlayer、灯光和网格

VfxAnimation 绑定一个 AnimationPlayer，显式填写 PlayAnimation。OneShot 的 clip 必须非循环。Loop 可以用循环 clip，或有限的 intro 后停在最后姿态，直到停止。

StopAnimation 可为空；有值时必须是非循环 clip，会用 Play 直接切过去，绝不排队在无限循环后。Loop 无 outro 时停止动画并结束这个参与者；OneShot 无 outro 时让正在播放的 clip 正常走完。建议把缩放归零、灯光能量衰减等放到 outro。

AnimationPlayer 可以动画节点位置、缩放、光强等。不建议动画共享 Material 的参数；若确需如此，在资源上启用 Local To Scene 或明确复制资源，防止多个实例互相影响。自动 adapter 不会复制任意外部脚本的资源。

## 4. Shader 网格

VfxShaderParameter 只写 GeometryInstance3D 的 instance shader uniform（默认 vfx_energy），不会修改 ShaderMaterial。Shader 中声明：

```glsl
instance uniform float vfx_energy = 1.0;
```

Target 是 MeshInstance3D 等 GeometryInstance3D。StartValue→EndValue 用 Duration 渐变；Loop 使用平滑往返，Stop 时从当前值用 StopDuration 过渡到 EndValue。实例 shader uniform 需要与实际 shader 名称/类型匹配，请参考例子的 shader。不同实例可安全共享 ShaderMaterial。

## 5. 组合结束与故障排查

- 灯光完成但粒子仍有尾巴：根继续存活，这是正确行为
- 停不下来：检查动画是否循环、StopAnimation 是否非循环、粒子 SpeedScale 是否为 0
- 看不到：检查 Camera、材质、粒子 Draw Pass、Visibility AABB、世界/局部坐标和 light 环境
- 立即 Finished / InvalidConfiguration：检查 root 类型、Target 是否在本 Scene、动画名字和超时数值
- TimedOut：调试真正未完成的参与者；不要只是无限增大超时
- 新效果改了旧效果材质：检查自定义脚本/AnimationPlayer 是否直接写共享 Resource，改用 instance uniform 或本地资源
- 暂停表现不一致：保持所有子节点 ProcessMode 为 Inherit，不要自建默认 process_always=true 的计时器

## 官方依据

- [GPUParticles3D](https://docs.godotengine.org/en/stable/classes/class_gpuparticles3d.html)
- [AnimationPlayer](https://docs.godotengine.org/en/stable/classes/class_animationplayer.html)
- [AnimationMixer 完成信号](https://docs.godotengine.org/en/stable/classes/class_animationmixer.html#signals)
- [暂停和 ProcessMode](https://docs.godotengine.org/en/stable/tutorials/scripting/pausing_games.html)
- [C# 导出属性/集合](https://docs.godotengine.org/en/stable/tutorials/scripting/c_sharp/c_sharp_exports.html)
- [实例 shader uniform](https://docs.godotengine.org/en/stable/tutorials/shaders/shader_reference/shading_language.html#per-instance-uniforms)
