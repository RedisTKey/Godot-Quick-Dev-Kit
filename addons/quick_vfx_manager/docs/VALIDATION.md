# 验证记录

日期：2026-10-03。基线：Godot 4.7.2 stable .NET (`ed1daf0bf`)，.NET SDK 9.0.318，Linux。

## 已完成

- Debug、Release 编译：0 warning / 0 error
- 只复制 `addons/quick_vfx_manager` 的临时独立工程：编辑器导入 + 自动回归通过，无其他 Quick 模块
- 自动回归：20 组、174 个断言通过，退出码 0；详见 `csharp/tests/README.md`
- 真实 AnimationPlayer intro/loop/outro 与真实 GPUParticles3D Finished 均纳入测试，未注入伪造粒子完成信号
- GPU 粒子正常结束、低 SpeedScale 不提前回收、SpeedScale=0 明确超时、循环柔和停止排空
- 真实 authored WorldPulse 与 CompositeBurst 均以 Completed 结束
- 节点/Resource 序列化往返、类型约束、节点路径和数组配置验证
- 独立代码审查覆盖启动重入、取消回调异常、被删除节点、句柄身份和目标所有权
- `git diff --check` 通过

## 原生编辑器和图形实测

在云端 Linux 原生 Godot .NET 编辑器打开根项目，执行 C# Build，Inspector 正确显示 VfxPlayback 的三个 typed Definition 资源和全部公开字段。

在 Inspector 将 WorldPulse 的 OneShotTimeout 从 5.0 修改到 5.25 并保存，核对 `.tres` 的实际持久化值；再恢复为 5.0 并保存。Godot 同时正常保存主场景与资源 UID。自动测试额外验证重新加载后的字段一致。

实际运行使用 **OpenGL Compatibility / Mesa llvmpipe 软件图形渲染**，不是 headless dummy：

- 可见青色世界脉冲，移动的白色来源离开后脉冲留在原地
- 可见橙色粒子、Shader 环、网格/灯光动画组合，完成状态为 Completed
- 可见紫色循环粒子/环跟随白色锚点移动
- 点击柔和停止后显示 Stopping，粒子尾巴和 outro 淡出后显示 Stopped
- 重新开始后点击立即停止，效果立即消失并显示 Cancelled
- 多次自动循环和手动触发后，WorldPulse / CompositeBurst 都显示 Completed，无插件运行时错误

图形环境启动时存在不支持 V-Sync、无音频设备（自动降级 Dummy）、Vulkan surface 不可用的环境诊断；实际项目走 OpenGL 并成功渲染。没有以这些环境诊断冒充插件成功/失败。未在实体 GPU、Windows/macOS 或其他 Godot 版本上验证。

## 在测试中发现并修正的关键问题

1. 启动参与者时的同步完成/Stop 重入：使用启动屏障，柔和停止延迟到全部参与者已启动
2. `_Ready` 中删除/停止：树插入前完成终结回调和 token 准备
3. 循环模式的有限 intro：保留参与者等待 Stop，再播放 outro
4. authored TopLevel：附着播放强制关掉 TopLevel
5. 取消回调抛错或释放 root：保护清理和信号后的有效性
6. **Restart 后重复设置 Emitting=true 会取消 Finished**：实际渲染测试发现；查证 Godot 4.7.2 源码后移除重复 setter，并新增真实粒子回归。该行为与是否 headless 无关

## 复现

```bash
GODOT=/path/to/Godot_v4.7.2-stable_mono_linux.x86_64 tests/quick_vfx_manager/run.sh
```

自动测试会故意注册一个抛错的 cancellation callback，出现一次 `EXPECTED test cancellation exception` 警告是预期覆盖；其余检查应正常通过。失败测试返回非零退出码。

GitHub Actions 工作流 `Quick VFX checks` 在本插件相关文件 push/PR 时只执行 C# Debug/Release 编译，不安装或启动 Godot。完整独立引擎检查只在 `workflow_dispatch` 手动触发时执行，或自行运行上面的脚本。GitHub 网页上的手动运行按钮要求工作流已存在于默认分支；合并前仍可直接运行脚本，本次未合并默认分支。

此前提交 `2e4d65ff38d89190838b2cabaf8c5a5e60f74c16` 的完整独立构建/引擎测试已在 [GitHub Actions run 37129077329](https://github.com/RedisTKey/Godot-Quick-Dev-Kit/actions/runs/37129077329) 通过。随后调整只改变 CI 触发边界，测试和运行时代码保持不变。准确新提交的自动编译结果以 GitHub 页面为准。
