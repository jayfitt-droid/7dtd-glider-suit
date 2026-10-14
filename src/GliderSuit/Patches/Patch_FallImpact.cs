using HarmonyLib;
using UnityEngine;

/// <summary>
/// Routes the glider's own crash value into the vanilla fall-damage pipeline, so
/// every vanilla modifier (parkour, Impact Bracing, boots) still applies (§5.5).
/// While the wings are open, vanilla impacts are silenced; ordinary falls with
/// the wings folded are left completely alone.
/// </summary>
[HarmonyPatch(typeof(EntityPlayerLocal), "FallImpact")]
internal static class Patch_FallImpact
{
    private static bool Prefix(EntityPlayerLocal __instance, ref float speed)
    {
        if (GliderState.Player == null || __instance != GliderState.Player) return true;

        source = "vanilla";
        if (Time.time < GliderState.ImpactArmedUntil)
        {
            source = "glider";
            speed = GliderState.PendingImpact;
            GliderState.ImpactGuardValue = speed;
            GliderState.ImpactGuardUntil = Time.time + ImpactGuardSeconds;
            GliderState.PendingImpact = 0f;
            GliderState.ImpactArmedUntil = 0f;
            return true;
        }

        // The game applies fall damage from the _fallSpeed cvar on the next buff
        // update, not inside this call. A crash is often followed within a step or
        // two by a small vanilla impact (the folded player touching the surface they
        // just hit), which would overwrite the cvar and erase the crash. Drop it.
        if (Time.time < GliderState.ImpactGuardUntil && speed <= GliderState.ImpactGuardValue)
        {
            source = "echo of the crash, dropped";
            speed = 0f;
            if (GliderMod.Settings.Debug) Log.Out("[GliderSuit] fall impact dropped: an echo of the crash just delivered.");
            return true;
        }

        // Ground touches the postfix did not classify are still not vanilla falls.
        if (GliderState.IsActive)
        {
            source = "silenced";
            speed = 0f;
            return true;
        }

        return true;
    }

    /// <summary>
    /// Debug only: every impact that could hurt, glider or vanilla, goes to the log
    /// with the health before it. The damage itself is applied by a buff a moment
    /// later, so the controller logs the health after (GliderController.Tick).
    /// </summary>
    private static void Postfix(EntityPlayerLocal __instance, float speed)
    {
        if (!GliderMod.Settings.Debug || __instance != GliderState.Player || speed < 0.05f) return;
        Log.Out("[GliderSuit] fall impact " + speed.ToString("F3") + " (" + source + "), health " +
            __instance.Health + " before");
        GliderState.ImpactLogHealth = __instance.Health;
        GliderState.ImpactLogAt = Time.time + 1f;
    }

    private static string source = "vanilla";

    /// <summary>How long after a glider crash a smaller vanilla impact counts as its echo.</summary>
    private const float ImpactGuardSeconds = 0.3f;
}
