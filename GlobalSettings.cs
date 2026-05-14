using BepInEx.Configuration;
using Common;

namespace SaS2DevTools;

/// <summary>
/// Global (non-player-specific) settings.
/// Currently covers the Camera section and Entity Visibility section.
/// Runtime state (offsets, active flag) is kept in plain fields and resets each session, persisted defaults live in ConfigEntries.
/// </summary>
public class GlobalSettings
{
    // Labels for the eight monster-slot types
    public static readonly string[] MonsterTypeLabels =
        ["NPC", "Monster", "Chest", "Switch", "Trap", "Harvest", "Critter", "Travel"];

    /// Per-type visibility flags indexed by MonsterDef.type (0-7).
    public readonly ConfigEntry<bool>[] ShowMonsterType = new ConfigEntry<bool>[8];

    /// Whether free-cam is currently active.
    public bool FreeCamActive;
    public bool ActivateFreecam;
    public float LastActivateTime;
    public float LastToggleHudTime;
    public float LastSpeedDownTime;
    public float LastSpeedUpTime;
    public const float ThrottleInterval = 500f;
    public const float SpeedThrottleInterval = 100f;
    
    /// Current camera scroll position when free-cam is on (world-space center).
    public float CamOffsetX;

    public float CamOffsetY;

    /// Current zoom level used by free-cam (positive = zoomed in, mapped to ScrollManager.zoom = -CamZoom to match the game's sign convention).
    public float CamZoom;

    /// How fast the camera pans per input tick when using keyboard/d-pad.
    public float CamSpeed;

    // Persisted defaults so the user's preferred zoom/speed survive restarts.
    private readonly ConfigEntry<float> _defaultCamSpeed;
    private readonly ConfigEntry<float> _defaultCamZoom;

    /// Block player movement/actions while freecam is active.
    public readonly ConfigEntry<bool> BlockInputInFreecam;

    public readonly ConfigEntry<float> CamZoomNonFreecam;

    // Visibility
    public readonly ConfigEntry<bool> ShowHud;
    public readonly ConfigEntry<bool> ShowDebugHud;
    public readonly ConfigEntry<bool> ShowPlayer;

    public GlobalSettings(ConfigFile cfg)
    {
        const string camSection = "Camera";
        const string visSection = "Visibility";

        _defaultCamSpeed = cfg.Bind(camSection, "DefaultCamSpeed", 10f,
            "Default camera pan speed (units per tick). Saved between sessions.");
        _defaultCamZoom = cfg.Bind(camSection, "DefaultCamZoom", 10f,
            "Default free-cam zoom level. Saved between sessions.");
        BlockInputInFreecam = cfg.Bind(camSection, "BlockInputInFreecam", true,
            "When free-cam is active, prevent the player character from moving or using actions.");
        CamZoomNonFreecam = cfg.Bind(camSection, "CamZoomNonFreecam", 1f,
            "Camera Zoom outside of Freecam");

        // Initialize runtime state from saved defaults.
        CamSpeed = _defaultCamSpeed.Value;
        CamZoom = _defaultCamZoom.Value;

        ShowHud = cfg.Bind(visSection, "ShowHUD", true,
            "Show the in-game HUD.");

        ShowDebugHud = cfg.Bind(visSection, "ShowDebug", false,
            "Show the in-game Debug HUD.");
        ShowPlayer = cfg.Bind(visSection, "ShowPlayer", true,
            "Show the player character and their shadow.");

        for (var i = 0; i < MonsterTypeLabels.Length; i++)
            ShowMonsterType[i] = cfg.Bind(visSection, $"Show_{MonsterTypeLabels[i]}", true,
                $"Show {MonsterTypeLabels[i]} entities.");
    }

    /// Returns true when the given monster type index should be drawn.
    public bool GetTypeVisible(int type) =>
        (uint)type < (uint)ShowMonsterType.Length && ShowMonsterType[type].Value;

    /// Snap the free-cam offset to whatever the game camera currently shows,
    /// so there is no jump when switching into free-cam mode.
    private void InitFreeCamPosition()
    {
        CamOffsetX = ScrollManager.scroll.X;
        CamOffsetY = ScrollManager.scroll.Y;
    }

    /// Reset the free-cam back to the player (same as InitFreeCamPosition, useful as a menu action while free-cam is already active).
    public void ResetCamToPlayer() => InitFreeCamPosition();

    /// Toggle free-cam on/off, snaps the offset when turning on.
    public void ToggleFreeCam()
    {
        ActivateFreecam = false;
        FreeCamActive = !FreeCamActive;
        if (FreeCamActive) InitFreeCamPosition();
    }

    /// Persist the current runtime speed/zoom back to config so they survive the next launch.
    public void SaveDefaults()
    {
        _defaultCamSpeed.Value = CamSpeed;
        _defaultCamZoom.Value = CamZoom;
    }
}