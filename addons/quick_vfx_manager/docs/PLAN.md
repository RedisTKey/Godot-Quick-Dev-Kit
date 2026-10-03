# 实现计划与决策记录

## 已确认的范围

2026-10-03：按用户确认的方案独立开发 quick_vfx_manager，测试后发布 Godot-Quick-Dev-Kit 的新功能分支。单个 VFX=单个 Scene；每个播放者挂播放组件并注册 Definition Resource；世界/附着、一次/循环、柔和/立即停止。只做通用接口，暂不池化。不修改或集成任何现有游戏项目。

## 顺序

1. 核实 main 当前目录和独立插件惯例
2. 实现 Definition、播放组件、单次 handle、场景根及参与者
3. 实现 GPU 粒子、AnimationPlayer、实例 shader adapter
4. 加入可视化独立工程与可编辑资源样例
5. 独立代码审查、回归测试、Debug/Release 构建、编辑器与图形运行检查
6. 发布 feat/quick-vfx-manager，确认远端准确 commit 及 CI

## 关键决策

- 组件是唯一主入口，无全局注册中心。World 用自身子节点 TopLevel，Attached 用锚点子节点并由原组件持有生命周期；不需要全局 world service
- 所有播放都 Instantiate，不提供任何虚假池参数；后续池化必须重新设计 generation/reset 契约
- .NET 实现在 csharp 子目录；本次不增加需求外的 GDScript 移植
- 根等待所有 VfxParticipant，初始化期屏障避免同步完成/重入提前回收
- 进入场景树之前就绑定终结回调，处理 _Ready 内的立即删除或 Stop
- Handle 每次生成且带身份校验；终结先撤销登记，再发用户信号
- 定义数据只读，逐次 override 单独存储。shader 动画使用 instance uniform 避免共享材质污染
- 标准粒子生命周期保守计算，额外延迟交给 TailSeconds/自定义 adapter；超时使用独立结果，不伪报成功
- 单一暂停时钟，各子节点继承根；动画 outro 直接 Play，不能 Queue 在循环后
- root project.godot 可直接打开。测试脚本只复制这一个插件到新临时工程，证明无其他 Quick 模块依赖
- 自动 CI 只做 C# Debug/Release 编译；Godot 编辑器导入/运行时测试需 workflow_dispatch 手动触发，或自行运行独立测试脚本
- 无现有仓库许可证，未擅自添加许可证

## 未纳入本版

池化、网络同步、2D 粒子、武器/激光玩法、碰撞检测、伤害判定、项目特化材质流水线、跨场景持久播放、可视化特效编辑器。接口可以扩展，自定义实现仍须遵守取消与完成契约。
