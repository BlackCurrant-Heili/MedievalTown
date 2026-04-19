// FrameDebugPauseHelper.cs 放在任意位置
using UnityEngine;

public static class FrameDebugPauseHelper
{
    // ✅ 全局静态开关，所有脚本共享
    public static bool IsPaused = false;

#if UNITY_EDITOR
    // ✅ Unity 编辑器菜单栏一键暂停
    [UnityEditor.MenuItem("Tools/Frame Debug/Pause Simulation %#p")] // Ctrl+Shift+P 快捷键
    private static void TogglePause()
    {
        IsPaused = !IsPaused;
        UnityEditor.EditorUtility.DisplayDialog(
            "Frame Debug Pause", 
            IsPaused ? "所有模拟已冻结" : "模拟已恢复", 
            "OK"
        );
    }

    // ✅ 运行时菜单实时显示状态
    [UnityEditor.MenuItem("Tools/Frame Debug/Pause Simulation %#p", true)]
    private static bool TogglePauseValidate()
    {
        UnityEditor.Menu.SetChecked("Tools/Frame Debug/Pause Simulation %#p", IsPaused);
        return true;
    }
#endif
}