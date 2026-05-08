using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using ProjectMage.character;
using ProjectMage.player;

namespace SaS2DevTools;

[HarmonyPatch]
public static class PlayerPatch
{
    private static readonly MethodInfo GetCharMethod     = AccessTools.Method(typeof(Player),      "GetCharacter");
    private static readonly MethodInfo GetMaxHpMethod    = AccessTools.Method(typeof(PlayerStats), "GetMaxHP");
    private static readonly MethodInfo GetMaxStamMethod  = AccessTools.Method(typeof(PlayerStats), "GetMaxStamina");
    private static readonly MethodInfo GetMaxPoiseMethod = AccessTools.Method(typeof(PlayerStats), "GetMaxPoise");
    private static readonly MethodInfo SetAnimMethod     = AccessTools.Method(typeof(CharAnim),    "SetAnim", [typeof(string), typeof(bool), typeof(bool)]);

    private static readonly object[] FalseArgs = [false];
    private static readonly object[] EmptyArgs = [];
    private static readonly object[] FlyArgs   = ["fly",  false, true];
    private static readonly object[] IdleArgs  = ["idle", false, true];
    private static bool _noClipRunning;

    /// Rising-edge detection for the jump key, keyed by player ID.
    private static readonly Dictionary<int, bool>  PrevJumpKey = new();

    /// HP from the previous frame, keyed by character ID.
    /// Backstop for DoDeath direct assignments that bypass DealDamage.
    private static readonly Dictionary<int, float> PrevHp = new();

    private static bool _loggedError;
    
    // __0 = frameTime, __1 = realTime  (PlayerMgr.Update signature)
    [HarmonyPatch(typeof(PlayerMgr), "Update", typeof(float), typeof(float))]
    [HarmonyPostfix]
    private static void PlayerMgrUpdatePatch(float __0)
    {
        try
        {
            if (PlayerMgr.player == null) return;

            foreach (var player in PlayerMgr.player)
            {
                if (player is not { active: true } || !player.isLocal) continue;
                if (GetCharMethod?.Invoke(player, null) is not Character { exists: true } character) continue;

                // Fetch the cheat-set that belongs to this specific player slot
                var cheats = SaS2DevTools.Instance?.GetCheats(player.ID);
                if (cheats == null) continue;

                PrevJumpKey.TryGetValue(player.ID,  out var prevJump);
                PrevHp.TryGetValue(character.ID,    out var prevHp);

                ApplyCheats(player, character, cheats, prevJump, prevHp, __0);

                PrevJumpKey[player.ID] = character.keys.keyJump;
                PrevHp[character.ID]   = character.hp;
            }
        }
        catch (Exception ex)
        {
            if (!_loggedError && SaS2DevTools.Instance != null)
            {
                SaS2DevTools.Instance.Log.LogError($"[PlayerPatch] Update loop failed: {ex}");
                _loggedError = true;
            }
        }
    }
    
    private static void ApplyCheats(Player player, Character character, PlayerCheats cheats, bool prevJump, float prevHp, float frameTime)
    {
        if (player.stats == null) return;
        
        if (cheats.Godmode.Value && GetMaxHpMethod != null)
        {
            var maxHp = (float)GetMaxHpMethod.Invoke(player.stats, FalseArgs);
            if (character.hp < maxHp) character.hp = maxHp;
        }

        // HitPatch.DealDamage_Prefix handles the common case.
        // This per-frame rollback catches DoDeath's direct "targ.hp = 1f" assignments and any other paths that bypass DealDamage.
        if (cheats.Invulnerable.Value
            && prevHp > 0f
            && character.hp < prevHp
            && character.dyingFrame <= 0f)
        {
            character.hp = prevHp;
        }

        if (cheats.InfStamina.Value && GetMaxStamMethod != null)
        {
            var maxStamina = (float)GetMaxStamMethod.Invoke(player.stats, FalseArgs);
            if (character.stamina < maxStamina) character.stamina = maxStamina;
        }

        if (cheats.InfPoise.Value && GetMaxPoiseMethod != null)
        {
            var maxPoise = (float)GetMaxPoiseMethod.Invoke(player.stats, EmptyArgs);
            if (character.poise < maxPoise) character.poise = maxPoise;
        }

        // HitPatch.ProcessHit_Prefix handles fling/launch prevention at the source.
        // This per-frame pass cleans up any anim that slipped through a code path that doesn't go through ProcessHit (e.g. the Block() helper).
        if (cheats.Unstaggerable.Value)
        {
            character.update.staggerFrame = 0f;

            if (character.state == 0 &&
                character.anim.animName is "stagger" or "lhit")
            {
                SetAnimMethod?.Invoke(character.anim, IdleArgs);
            }
        }

        // NoClip takes over all movement: skip InfJumps, NoFallDmg
        if (cheats.NoClip.Value)
        {
            ApplyNoClip(character, frameTime, cheats);
            return;
        }

        // Pin the "last grounded Y" so Land() sees a fall distance of ~0.
        if (cheats.NoFallDmg.Value && character.state == 1)
            character.update.lastGroundedY = character.loc.Y;
        
        if (cheats.InfJumps.Value)
        {
            var currJump = character.keys.keyJump;

            // Guard conditions:
            // state == 1             -> only while airborne
            // rising edge            -> one trigger per press, not every frame
            // animName != "walljump" -> the game transitions state->1 inside its own update loop on the walljump frame.
            //                           our postfix would otherwise see state==1 + fresh key-press and fire Jump() on top of the walljump, either zeroing X momentum or triggering JumpDrop().
            if (character.state != 1
                || !currJump || prevJump
                || !(character.dyingFrame <= 0f)
                || character.leap is { active: true }
                || !(character.update.grappleFrame <= 0f)
                || character.anim.animName == "walljump") return;
            // Set velocity directly instead of calling CharUpdate.Jump() to avoid:
            //   1. JumpDrop() zeroing both axes when keyDown is held over a passthrough tile (character looks down + presses jump -> drops instead of jumping upward).
            //   2. Jump() conditionally overwriting traj.X only when a run key is held, which is correct on the ground but would silently zero any wall-jump X momentum if neither run key is active.
            character.traj.Y = -950f;

            if (character.keys.keyRightRun)
                character.traj.X =  380f;
            else if (character.keys.keyLeftRun)
                character.traj.X = -380f;
            // else: traj.X is intentionally left as-is, preserving momentum (e.g. the X velocity from a preceding wall jump).

            SetAnimMethod?.Invoke(character.anim, FlyArgs);
        }
    }
    
    private static void ApplyNoClip(Character character, float frameTime, PlayerCheats cheats)
    {
        var speed = cheats.NoClipSpeed.Value;
        if (character.keys.keyRoll)
            _noClipRunning = !_noClipRunning;

        if (_noClipRunning)
            speed *= 2f;
        
        // Freeze physics: airborne state with no velocity accumulation
        character.state  = 1;
        character.traj.Y = 0f;
        character.traj.X = 0f;

        // Vertical: hold jump + look up/down
        if (character.keys.keyJumpHold)
            character.loc.Y += (character.keys.keyDown ? 1f : -1f) * speed * frameTime;

        // Horizontal: writing loc.X directly in the postfix runs AFTER the game's
        // collision resolution, so walls can't push back on this position.
        if (character.keys.keyRightRun)
            character.loc.X += speed * frameTime;
        else if (character.keys.keyLeftRun)
            character.loc.X -= speed * frameTime;

        // Force out of any grounded / hit animation; leave attack anims alone
        if (character.anim.animName is "idle" or "run" or "walk" or "sprint" or "sprintrun" or "land" or "stagger" or "lhit" or "hit")
        {
            SetAnimMethod?.Invoke(character.anim, FlyArgs);
        }
    }
}