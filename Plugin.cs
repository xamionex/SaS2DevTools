using System.IO;
using System.Timers;
using BepInEx;
using BepInEx.NET.Common;
using HarmonyLib;

namespace SaS2DevTools;

[BepInPlugin(PluginInfo.PluginGuid, PluginInfo.PluginName, PluginInfo.PluginVersion)]
// ReSharper disable once ClassNeverInstantiated.Global
public class SaS2DevTools : BasePlugin
{
    internal static SaS2DevTools Instance;
    private Harmony _harmony;
    private FileSystemWatcher _configWatcher;
    private Timer _debounceTimer;

    // One cheat-set per player slot. Config keys live in sections
    // "Player 1" and "Player 2" so they stay neatly separated in the file.
    private PlayerCheats Player1 { get; set; }
    private PlayerCheats Player2 { get; set; }

    // Global (non-player) settings: camera, visibility, etc.
    internal GlobalSettings Global { get; private set; }

    /// <summary>Returns the cheat-set for the given player ID (0 is P1, 1 is P2).</summary>
    internal PlayerCheats GetCheats(int playerId) => playerId == 1 ? Player2 : Player1;

    public override void Load()
    {
        Instance = this;

        Player1 = new PlayerCheats(Config, "Player 1");
        Player2 = new PlayerCheats(Config, "Player 2");
        Global = new GlobalSettings(Config);

        var configDirectory = Path.GetDirectoryName(Config.ConfigFilePath);
        var configFileName = Path.GetFileName(Config.ConfigFilePath);
        if (!string.IsNullOrEmpty(configDirectory))
        {
            _configWatcher = new FileSystemWatcher(configDirectory, configFileName)
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
                EnableRaisingEvents = true
            };
            _debounceTimer = new Timer(1000) { AutoReset = false };
            _debounceTimer.Elapsed += (_, _) =>
            {
                Config.Reload();
                Log.LogInfo("Configuration reloaded.");
            };
            _configWatcher.Changed += (_, _) =>
            {
                _debounceTimer.Stop();
                _debounceTimer.Start();
            };
        }

        _harmony = new Harmony(PluginInfo.PluginGuid);
        _harmony.PatchAll();
        Log.LogInfo($"{PluginInfo.PluginName} v{PluginInfo.PluginVersion} loaded.");
    }

    public override bool Unload()
    {
        _configWatcher?.Dispose();
        _debounceTimer?.Dispose();
        _harmony?.UnpatchSelf();
        return base.Unload();
    }
}