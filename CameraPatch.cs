using System;
using System.Collections.Generic;
using System.Reflection;
using Bestiary.monsters;
using Common;
using HarmonyLib;
using Menumancer.hud;
using ProjectMage.character;
using ProjectMage.config;
using ProjectMage.director;
using ProjectMage.gamestate;
using ProjectMage.player;
using System.Text;
using ButtonState = Common.ButtonState;
using Color = Common.Color;
using GamePadState = Common.GamePadState;
using KeyboardState = Common.KeyboardState;
using Keys = Common.Keys;

namespace SaS2DevTools;

[HarmonyPatch]
public static class CameraPatch
{
    private static readonly MethodInfo GetGamePadStateMethod =
        AccessTools.Method(typeof(GlobalInputMgr), "GetGamePadState", [typeof(int)]);

    private static GamePadState GetGamePadState(int playerIdx)
    {
        if (GetGamePadStateMethod == null) return default;
        // Guard against invalid indices
        if (playerIdx is < 0 or > 3) return default;
        try
        {
            return (GamePadState)GetGamePadStateMethod.Invoke(null, [playerIdx]);
        }
        catch
        {
            return default;
        }
    }

    // Input states
    private static readonly HashSet<Keys> KHeld = [];
    private static bool _rscLscWasPressed;
    private static GamePadState _prevGp;
    private static bool KDown(KeyboardState ks, Keys k) => ks.IsKeyDown(k);

    private static bool KSingle(KeyboardState ks, Keys k)
    {
        if (ks.IsKeyDown(k)) return KHeld.Add(k);
        KHeld.Remove(k);
        return false;
    }

    private static bool GpSingle(GamePadState cur, Func<GamePadState, bool> pressed) =>
        pressed(cur) && !pressed(_prevGp);

    // Block input when freecam is active
    [HarmonyPrefix]
    [HarmonyPatch(typeof(InputProfile), "Update", typeof(int))]
    // ReSharper disable once InconsistentNaming
    public static bool InputProfileUpdatePrefix(InputProfile __instance, int __0)
    {
        var global = SaS2DevTools.Instance?.Global;
        if (global == null) return true;
        GameState.hideHUD = !global.ShowHud.Value;

        if (global.FreeCamActive && global.BlockInputInFreecam.Value)
        {
            __instance.keysEnable = false;
            ProcessCameraInputs(global, __0);
            _prevGp = GetGamePadState(__0);
            return false;
        }

        __instance.keysEnable = true;
        ProcessCameraInputs(global, __0);
        _prevGp = GetGamePadState(__0);
        return true;
    }

    // Camera Input (keyboard + controller)
    private static void ProcessCameraInputs(GlobalSettings global, int playerIdx)
    {
        // Sync HUD state every frame (important when blocking input)
        GameState.hideHUD = !global.ShowHud.Value;

        // Keyboard
        var ks = GlobalInputMgr.ks;
        var ctrl = ks.IsKeyDown(Keys.LeftControl) || ks.IsKeyDown(Keys.RightControl);
        var alt = ks.IsKeyDown(Keys.LeftAlt) || ks.IsKeyDown(Keys.RightAlt);

        // Toggle free-cam with F5
        if (KSingle(ks, Keys.F5) || global.ActivateFreecam) global.ToggleFreeCam();
        if (global.FreeCamActive)
        {
            // Pan with WASD
            if (KDown(ks, Keys.W)) global.CamOffsetY -= global.CamSpeed;
            if (KDown(ks, Keys.S)) global.CamOffsetY += global.CamSpeed;
            if (KDown(ks, Keys.A)) global.CamOffsetX -= global.CamSpeed;
            if (KDown(ks, Keys.D)) global.CamOffsetX += global.CamSpeed;

            // Speed: Q slower, E faster
            if (KDown(ks, Keys.Q)) global.CamSpeed = Math.Max(1f, global.CamSpeed - 0.5f);
            if (KDown(ks, Keys.E)) global.CamSpeed += 0.5f;

            // Zoom: F zoom in, R zoom out
            var zoomStep = ctrl ? 0.1f :
                alt ? 10f : 1f;
            if (KDown(ks, Keys.F)) global.CamZoom = Math.Max(0.5f, global.CamZoom - zoomStep);
            if (KDown(ks, Keys.R)) global.CamZoom += zoomStep;
        }

        // HUD visibility toggle
        if (KSingle(ks, Keys.F6))
        {
            global.ShowHud.Value = !global.ShowHud.Value;
        }

        // Monster-type visibility toggles
        var monsterKeys = new[] { Keys.D0, Keys.D1, Keys.D2, Keys.D3, Keys.D4, Keys.D5, Keys.D6, Keys.D7 };
        for (var i = 0; i < monsterKeys.Length; i++)
            if (KSingle(ks, monsterKeys[i]))
                global.ShowMonsterType[i].Value = !global.ShowMonsterType[i].Value;

        // Reset camera to player (C)
        if (global.FreeCamActive && KSingle(ks, Keys.C)) global.ResetCamToPlayer();

        // Controller
        // Only process if we have a valid gamepad index
        if (playerIdx < 0) return;
        var gp = GetGamePadState(playerIdx);
        if (!gp.IsConnected) return;

        // Toggle free-cam with RSC + LSC (both sticks clicked)
        var rscPressed = gp.Buttons.RightStick == ButtonState.Pressed;
        var lscPressed = gp.Buttons.LeftStick == ButtonState.Pressed;
        if (rscPressed && lscPressed && !_rscLscWasPressed)
        {
            global.ToggleFreeCam();
            _rscLscWasPressed = true;
        }
        else if (!rscPressed || !lscPressed)
        {
            _rscLscWasPressed = false;
        }

        if (!global.FreeCamActive) return;

        // Camera pan: left stick + D-pad
        var panSpeed = global.CamSpeed;
        global.CamOffsetX += gp.ThumbSticks.Left.X * panSpeed;
        global.CamOffsetY -= gp.ThumbSticks.Left.Y * panSpeed; // Y inverted
        if (gp.DPad.Left == ButtonState.Pressed) global.CamOffsetX -= panSpeed;
        if (gp.DPad.Right == ButtonState.Pressed) global.CamOffsetX += panSpeed;
        if (gp.DPad.Up == ButtonState.Pressed) global.CamOffsetY -= panSpeed;
        if (gp.DPad.Down == ButtonState.Pressed) global.CamOffsetY += panSpeed;

        // Camera zoom: triggers
        var zoomDelta = 0f;
        if (gp.Triggers.Left > 0.3f) zoomDelta -= gp.Triggers.Left * 0.3f;
        if (gp.Triggers.Right > 0.3f) zoomDelta += gp.Triggers.Right * 0.3f;
        if (zoomDelta != 0f) global.CamZoom = Math.Max(1f, global.CamZoom + zoomDelta);

        // HUD visibility toggle (B)
        if (GpSingle(gp, g => g.Buttons.B == ButtonState.Pressed))
        {
            global.ShowHud.Value = !global.ShowHud.Value;
        }

        // Reset camera to player (Y)
        if (GpSingle(gp, g => g.Buttons.Y == ButtonState.Pressed)) global.ResetCamToPlayer();
    }

    // SCROLL OVERRIDE, runs after CamMgr finishes its own scroll update
    [HarmonyPostfix]
    [HarmonyPatch(typeof(CamMgr), "Update", typeof(float), typeof(float))]
    public static void CamMgrUpdatePatch()
    {
        var global = SaS2DevTools.Instance?.Global;
        if (global is not { FreeCamActive: true }) return;
        ScrollManager.scroll = new Vector2(global.CamOffsetX, global.CamOffsetY);
        ScrollManager.zoom = -global.CamZoom;
        ScrollManager.UpdateCannedValues();
        ScrollManager.tL = ScrollManager.GetRealLoc(default, 1f);
        ScrollManager.bR = ScrollManager.GetRealLoc(ScrollManager.screenSize, 1f);
    }

    // Zoom only
    [HarmonyPostfix]
    [HarmonyPatch(typeof(CamMgr), "Update", typeof(float), typeof(float))]
    public static void CamMgrUpdateZoomPatch()
    {
        var global = SaS2DevTools.Instance?.Global;
        if (global is { FreeCamActive: true } or null || GameState.state != 1) return;
        ScrollManager.zoom *= global.CamZoomNonFreecam.Value;
        ScrollManager.UpdateCannedValues();
        ScrollManager.tL = ScrollManager.GetRealLoc(default, 1f);
        ScrollManager.bR = ScrollManager.GetRealLoc(ScrollManager.screenSize, 1f);
    }

    // Skip drawing hidden entity types
    private static bool ShouldDrawCharacter(Character c)
    {
        var global = SaS2DevTools.Instance?.Global;
        if (global == null) return true;

        // Local player character?
        if (c.playerIdx >= 0 && PlayerMgr.player != null && c.playerIdx < PlayerMgr.player.Length &&
            c.ID == PlayerMgr.player[c.playerIdx].charIdx)
            return global.ShowPlayer.Value;

        // Monster / NPC / chest etc.
        if (c.monsterIdx <= -1) return true;
        var mDef = MonsterCatalog.monsterDef[c.monsterIdx];
        return global.GetTypeVisible(mDef.type);
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(Character), nameof(Character.Draw), typeof(Vector2), typeof(float))]
    // ReSharper disable once InconsistentNaming
    public static bool DrawPatch(Character __instance) => ShouldDrawCharacter(__instance);

    [HarmonyPrefix]
    [HarmonyPatch(typeof(Character), nameof(Character.DrawShadow), typeof(MonsterDef), typeof(bool))]
    // ReSharper disable once InconsistentNaming
    public static bool DrawShadowPatch(Character __instance) => ShouldDrawCharacter(__instance);

    // Free-cam status, visibility legend
    [HarmonyPostfix]
    [HarmonyPatch(typeof(GameDraw), "DrawGame")]
    public static void DrawGamePatch()
    {
        var global = SaS2DevTools.Instance?.Global;
        if (global == null) return;
        GameState.hideHUD = !global.ShowHud.Value;
        if (!global.ShowHud.Value || !global.ShowDebugHud.Value) return;
        SpriteTools.BeginAlpha();
        var y = 300;
        const float scale = 0.45f;
        const float scaleSmall = 0.32f;
        if (global.FreeCamActive)
        {
            Text.DrawText(new StringBuilder($"[FREE-CAM] X:{global.CamOffsetX:0} Y:{global.CamOffsetY:0}"),
                new Vector2(100, y), new Color(0.4f, 1f, 0.5f, 1f), scale, 0);
            y += 40;
            Text.DrawText(new StringBuilder($"  Zoom:{global.CamZoom:F1}  Speed:{global.CamSpeed:F1}"),
                new Vector2(100, y), new Color(0.4f, 1f, 0.5f, 1f), scaleSmall, 0);
            y += 45;
        }

        Text.DrawText(new StringBuilder("PLAYER: " + (global.ShowPlayer.Value ? "on" : "off")), new Vector2(100, y),
            Color.White, scaleSmall, 0);
        y += 28;
        for (var i = 0; i < GlobalSettings.MonsterTypeLabels.Length; i++)
        {
            var vis = global.ShowMonsterType[i].Value;
            Text.DrawText(new StringBuilder($"{GlobalSettings.MonsterTypeLabels[i]}: {(vis ? "on" : "off")}"),
                new Vector2(100, y), vis ? Color.White : new Color(0.5f, 0.5f, 0.5f, 1f), scaleSmall, 0);
            y += 24;
        }

        SpriteTools.End();
    }
}