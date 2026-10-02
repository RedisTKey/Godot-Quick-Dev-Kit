# Quick Input

Godot 输入绑定插件，提供功能对应的 **GDScript** 与 **C#** 两套实现。支持键盘、鼠标按钮/滚轮、手柄按钮，多槽位、冲突预览、四种冲突策略、恢复默认、安全文件持久化及可复用捕获按钮。

## 目录与安装

```text
addons/quick_input/
├── README.md
├── gds/                   # 原有 GDScript 实现，保留 class_name 和 .uid
│   ├── plugin.cfg         # Quick Input (GDScript)
│   ├── runtime/  ui/
│   └── examples/  tests/
└── csharp/                # namespace QuickDevKit.QuickInput
    ├── plugin.cfg         # Quick Input (C#)
    ├── runtime/  ui/  editor/
    └── examples/  tests/
```

复制整个 `addons/quick_input`，或只保留其中需要的语言目录；**不要改变 `gds` / `csharp` 的目录层级**。

- GDScript：启用 **Quick Input (GDScript)**，入口为 `gds/plugin.cfg`。详见 [GDScript API](gds/README.md)
- C#：使用 Godot **.NET 版**，安装适用的 .NET SDK，先创建/构建项目的 `.csproj`，然后启用 **Quick Input (C#)**，入口为 `csharp/plugin.cfg`。详见 [C# API](csharp/README.md)
- 两者均注册 `/root/QuickInput`，**一次只启用一个**。第二次启用会报错并保持现有服务，不会覆盖它；出现此错误后取消第二个插件的启用勾选，再切换版本
- 停用只移除路径属于该实现的 Autoload，编辑器重启后也能正确清理。手工注册的同名服务不会被覆盖/删除
- 两套源文件可以同时存在。GDScript 保留原有 `QuickInput*` 全局类；C# 核心使用命名空间，只有 `CSharpQuickInputSettings`、`CSharpQuickInputActionDefinition`、`CSharpQuickInputRebindButton` 三个独立名称注册到 Inspector

## 从旧版迁移

1. 备份项目，在移动文件前停用旧 **Quick Input** 插件
2. 用新目录替换旧 `addons/quick_input`。原 `.gd` 和 `.gd.uid` 一起迁入 `gds/`，不要保留重复旧脚本
3. 将旧显式路径 `res://addons/quick_input/runtime/...`、`ui/...`、`examples/...`、`tests/...` 改为 `res://addons/quick_input/gds/...`；已有 GDScript 类名与方法名不变
4. 检查「自动加载」：若旧路径仍残留，先移除旧 QuickInput 项，再启用选定语言的新插件
5. GDScript 与 C# 的 v1 `user://quick_input_bindings.cfg` 格式相同，可在切换语言后读取已有绑定；Resource `.tres` / 场景脚本类型不会自动翻译，需要为 C# 创建对应资源

## 两套实现的能力对应

| 能力 | GDScript | C# |
| --- | --- | --- |
| 配置 / 默认 InputMap 快照 / 多槽位 | `configure` | `Configure` |
| 键盘（物理/逻辑/位置/修饰键）、鼠标/滚轮、手柄按钮 | `QuickInputEventCodec` | 同名命名空间类型 |
| 预览 / 拒绝过期事务 | `preview_rebind` / `apply_rebind` | `PreviewRebind` / `ApplyRebind` |
| 允许、拒绝、替换、交换 | `ALLOW/REJECT/REPLACE/SWAP` | `Allow/Reject/Replace/Swap` |
| 清空 / 恢复单动作 / 恢复全部 | `clear_binding/reset_action/reset_all` | `ClearBinding/ResetAction/ResetAll` |
| 保存失败保持原 InputMap / 只保存偏离默认的动作 | 支持 | 支持 |
| ConfigFile 临时校验 / `.previous` 恢复 | v1 | 同一 v1 格式 |
| 自定义存储 / InputMap 适配器 | 可继承 | 可重写虚方法 |
| 捕获按钮 / 冲突确认 / 取消 / 错误信号 | 支持 | 支持 |
| 独立示例 / 自动测试 | `gds/examples`、`gds/tests` | `csharp/examples`、`csharp/tests` |

运行时游戏输入仍使用 Godot `Input` / `InputEvent`：本插件管理绑定，不发布动作按下/松开/轴值事件，也不负责输入上下文优先级或路由。`BindingChanged` 仅表示绑定配置变化。

模拟摇杆轴、手柄配置档、云存档、编辑器 Dock 不在当前范围内。输入 Action 必须先在 InputMap 中定义；插件不会扫描或接管游戏动作。不要让两个运行时管理器同时控制相同动作。

## 仓库自检

仓库本身是插件集合，不需要添加或启用其他插件。测试助手自动创建临时 Godot 项目，单独复制 Quick Input，并隔离缓存与 `user://`：

```sh
# Godot 普通版：仅验证 GDScript
python3 tests/run_quick_input.py --backend gds --godot /path/to/godot
# Godot .NET + .NET 8 SDK：编译 C#，验证两套运行时和文件互操作
python3 tests/run_quick_input.py --backend all --godot /path/to/godot-mono --dotnet /path/to/dotnet
```

C# 测试工程使用 `Godot.NET.Sdk/4.6.3` 和 `net8.0`。实际验证范围与结果见 [验证记录](VALIDATION.md)。
