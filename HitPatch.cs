using System;
using Common;
using HarmonyLib;
using ProjectMage.character;
using ProjectMage.hit;
using ProjectMage.particles;
using ProjectMage.player;

namespace SaS2DevTools;

[HarmonyPatch]
public static class HitPatch
{
    private static bool HasCheat(Character targ, Func<PlayerCheats, bool> check)
    {
        if (targ.playerIdx < 0 || PlayerMgr.player == null) return false;
        var idx = targ.playerIdx;
        if (idx >= PlayerMgr.player.Length) return false;
        var p = PlayerMgr.player[idx];
        if (p is not { isLocal: true }) return false;
        var cheats = SaS2DevTools.Instance?.GetCheats(p.ID);
        return cheats != null && check(cheats);
    }

    // Unstaggerable
    // Target overload: ProcessHit(owner, targ, p, traj, bigHit, poiseAtk, block)
    // Zeroing poiseAtk prevents the poise-break branch (fling traj + stagger anim).
    // Zeroing bigHit prevents BigHit() from applying launch trajectories.
    // Clamping poise ensures we don't fire from pre-depleted poise either.
    [HarmonyPatch(typeof(HitManager), "ProcessHit", typeof(Character), typeof(Character), typeof(Particle),
        typeof(Vector2), typeof(int), typeof(float), typeof(int))]
    [HarmonyPrefix]
    public static void ProcessHit_Prefix(Character targ, ref int bigHit, ref float poiseAtk)
    {
        if (!HasCheat(targ, c => c.Unstaggerable.Value)) return;

        poiseAtk = 0f; // no poise damage: poise-break path never taken
        bigHit = 0; // no BigHit launch trajectory

        // If poise was already depleted by earlier hits, clamp it above zero so the poise <= 0f stagger check doesn't fire anyway
        if (targ.poise <= 0f) targ.poise = 1f;
    }

    // Invulnerable
    // DealDamage is the single site that executes targ.hp -= total.
    // Zeroing total here means the subtraction becomes a no-op while still letting the method run (it also forwards damage to mage parents).
    [HarmonyPatch(typeof(HitManager), "DealDamage", typeof(Character), typeof(float))]
    [HarmonyPrefix]
    public static void DealDamage_Prefix(Character targ, ref float total)
    {
        if (!HasCheat(targ, c => c.Invulnerable.Value)) return;
        total = 0f;
    }
}