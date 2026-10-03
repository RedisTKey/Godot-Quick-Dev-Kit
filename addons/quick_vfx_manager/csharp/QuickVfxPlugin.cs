#if TOOLS
using Godot;
namespace QuickVfx;

[Tool]
public partial class QuickVfxPlugin : EditorPlugin
{
    // GlobalClass provides typed nodes/resources. Deliberately no autoload or project mutation.
}
#endif
