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
    private static readonly MethodInfo JumpMethod        = AccessTools.Method(typeof(CharUpdate),  "Jump");
    private static readonly MethodInfo SetAnimMethod     = AccessTools.Method(typeof(CharAnim),    "SetAnim",
                                                               [typeof(string), typeof(bool), typeof(bool)]);

    private static readonly object[] FalseArgs = [false];
    private static readonly object[] EmptyArgs = [];
    private static readonly object[] FlyArgs   = ["fly", false, true];

    // Per-character / per-player state
    /// Rising-edge detection for the jump key, keyed by player ID.
    private static readonly Dictionary<int, bool>  PrevJumpKey = new();

    /// HP from the previous frame, keyed by character ID.
    /// Used by Invulnerable to detect and roll back any damage taken.
    private static readonly Dictionary<int, float> PrevHp = new();

    private static bool _loggedError;

    // Main patch
    [HarmonyPatch(typeof(PlayerMgr), "Update", typeof(float), typeof(float))]
    [HarmonyPostfix]
    private static void PlayerMgrUpdatePatch()
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

                ApplyCheats(player, character, cheats, prevJump, prevHp);

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

    private static void ApplyCheats(
        Player player, Character character,
        PlayerCheats cheats,
        bool prevJump, float prevHp)
    {
        if (player.stats == null) return;

        if (cheats.Godmode.Value && GetMaxHpMethod != null)
        {
            var maxHp = (float)GetMaxHpMethod.Invoke(player.stats, FalseArgs);
            if (character.hp < maxHp) character.hp = maxHp;
        }

        // prevHp == 0f on the very first frame – only act once we have a valid prior reading.
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

        if (cheats.Unstaggerable.Value)
        {
            character.update.staggerFrame = 0f;

            if (character.state == 0 && character.anim.animName == "stagger")
                SetAnimMethod?.Invoke(character.anim, ["idle", false, true]);
        }

        // Infinite Jumps (+ fall damage cancellation)
        if (cheats.InfJumps.Value)
        {
            // Pin the "last grounded Y" so Land() never sees a large fall distance
            if (character.state == 1)
                character.update.lastGroundedY = character.loc.Y;

            // Rising-edge air jump
            var currJump = character.keys.keyJump;
            if (JumpMethod    != null
                && SetAnimMethod  != null
                && character.state == 1
                && currJump && !prevJump
                && character.dyingFrame <= 0f
                && character.leap is not { active: true }
                && character.update.grappleFrame <= 0f)
            {
                JumpMethod.Invoke(character.update, null);
                SetAnimMethod.Invoke(character.anim, FlyArgs);
            }
        }
    }
}
