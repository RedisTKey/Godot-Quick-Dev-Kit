# Quick Input (C#)

对应 [GDScript 版](../gds/README.md) 的 Godot 4 .NET 实现；功能边界、冲突规则和 v1 ConfigFile 格式一致。核心命名空间：`QuickDevKit.QuickInput`。运行不依赖第三方库。

## 安装与类型

保持路径 `res://addons/quick_input/csharp`。先使用 Godot .NET 创建并构建项目的 C# 工程，再启用 **Quick Input (C#)**。停用 GDScript 插件后才能切换到 C#；二者使用相同 `/root/QuickInput` Autoload。

C# API 用 `QuickInputSettings` / `QuickInputActionDefinition` / `QuickInputRebindButton`；在 Inspector 的「新建 Resource / 添加节点」中使用独立全局名称：

- `CSharpQuickInputSettings`
- `CSharpQuickInputActionDefinition`
- `CSharpQuickInputRebindButton`

这些是对应 C# 类型的薄子类，避免与同时安装的 GDScript `class_name` 冲突。原 GDScript `.tres` 不会自动变成 C# Resource。

## 配置与绑定

```csharp
using Godot;
using QuickDevKit.QuickInput;

var jump = new QuickInputActionDefinition
{
    Action = "jump", // 先在 InputMap 定义默认事件
    DisplayName = "Jump",
    BindingSlots = 2,
    ConflictGroup = "gameplay",
    AllowedEventTypes = QuickInputActionDefinition.Keyboard | QuickInputActionDefinition.GamepadButton,
};
var settings = new QuickInputSettings();
settings.Actions.Add(jump);
settings.ReservedEvents.Add(new InputEventKey { PhysicalKeycode = Key.Escape });
var input = GetNode<QuickInputManager>("/root/QuickInput");
Error error = input.Configure(settings);

var preview = input.PreviewRebind("jump", 1, new InputEventKey { PhysicalKeycode = Key.J });
if (preview.Transaction != null)
    error = input.ApplyRebind(preview.Transaction, QuickInputManager.ConflictPolicy.Swap);

input.Rebind("jump", 1, new InputEventMouseButton { ButtonIndex = MouseButton.Right });
input.ClearBinding("jump", 1);
input.ResetAction("jump");
input.ResetAll();
```

`Configure(settings, store)` 可注入 `QuickInputStore` 子类；重写 `LoadBindings()` / `SaveBindings(Dictionary)`，加载错误通过 `LoadError` 报告。只有存储成功才更新运行时 InputMap。`QuickInputMapAdapter` 也可替换，以便测试或接入项目服务。

`preview.Status` 的枚举是 `QuickInputRebindResult.RebindStatus`（`Ready/Conflict/Invalid/Unsupported`）；`preview.Conflicts` 提供占用的 `Action/Slot/Event`。`ApplyRebind` 返回 `Error.Busy` 表示快照已经变化，必须重新预览。`Swap` 只接受零或一个冲突槽位。

`GetBinding` 返回副本；`GetBindingText` 返回显示文字或 `Unbound`。默认 `AllowModifierCombinations = false`，保留事件不能用于新绑定；不同 `ConflictGroup` 可共享输入。`BindingChanged` 和 `BindingsSaveFailed` 是 Godot 信号，也可使用 C# 事件订阅。

## 捕获按钮与示例

给 `CSharpQuickInputRebindButton` 配置 `Action` / `Slot`，或通过 `SetManager(manager)` 注入。点击后捕获下一个有效按下事件；忽略释放/键盘连发；Escape 或手柄 `ui_cancel` 取消。

- `ConflictDetected`：默认 Reject 策略遇到冲突时触发，随后调用 `ConfirmConflict(policy)`
- `CaptureFailed`：无效输入、配置或保存错误
- `CaptureCancelled`：用户取消
- `BeginCapture()` / `CancelCapture()` / `RefreshText()` 可从项目 UI 调用

示例场景：`res://addons/quick_input/csharp/examples/basic_usage.tscn`。它使用独立示例存档，退出时恢复映射。临时管理器可以使用 `RestoreDefaultsInInputMap()` 还原其接管的动作，此操作不改写磁盘。

## 测试

从仓库根目录运行 `python3 tests/run_quick_input.py --backend all --godot /path/to/godot-mono`，详见 [主说明](../README.md#仓库自检)。独立安装 C# 时，运行自己的项目构建后也可执行 `res://addons/quick_input/csharp/tests/runtime_tests.tscn`；GDScript 互操作检查在缺少 `gds/` 时跳过。
