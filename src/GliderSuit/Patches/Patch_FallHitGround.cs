using HarmonyLib;
using UnityEngine;

/// <summary>
/// Safety net for EntityAlive.fallHitGround, which deals damage directly when the
/// per-tick motion is fast. Clamping the motion keeps the vanilla landing
/// animation and AI notification while suppressing a second charge on top of the
/// glider's own impact.
/// </summary>
[HarmonyPatch(typeof(EntityAlive), "fallHitGround")]
internal static class Patch_FallHitGround
{
    private static void Prefix(EntityAlive __instance, ref Vector3 _fallMotion)
    {
        if (GliderState.Player == null || __instance != GliderState.Player) return;
        if (!(GliderState.IsActive || GliderState.InGrace)) return;
        if (_fallMotion.y < -0.5f) _fallMotion.y = -0.5f;
    }
}
