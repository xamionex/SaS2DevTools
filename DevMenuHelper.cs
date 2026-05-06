using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using ProjectMage.player;
using ProjectMage.player.menu;

namespace SaS2DevTools;

public static class DevMenuHelper
{
    private static FieldInfo _levelField;

    public static void AddAndActivateScreen(Player player, LevelDevMenu screen)
    {
        _levelField ??= AccessTools.Field(typeof(PlayerMenu), "level");
        if (_levelField == null)
        {
            SaS2DevTools.Instance.Log.LogError("PlayerMenu.level field not found.");
            return;
        }

        if (_levelField.GetValue(player.menu) is not List<LevelBase> levelList) return;
        
        // Remove any existing inactive DevMenu instances to prevent conflicts
        levelList.RemoveAll(l => l is LevelDevMenu && !l.IsActive());

        // Look for existing active instance
        var existing = levelList.OfType<LevelDevMenu>().FirstOrDefault();
        if (existing != null)
        {
            existing.Activate();
            return;
        }

        levelList.Add(screen);
        screen.Activate();
    }
}