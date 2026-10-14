using HarmonyLib;
using UnityEngine;

/// <summary>
/// The flight model, bolted onto the game's own movement step (§5.4). The prefix
/// hands the controller the glider's velocity; the postfix reads back what the
/// collision system actually allowed and turns that into a landing or a crash.
/// Only the local player's controller is ever touched.
/// </summary>
[HarmonyPatch(typeof(vp_FPController))]
internal static class Patch_FixedMove
{
    [HarmonyPrefix]
    [HarmonyPatch("FixedMove")]
    private static bool Prefix(vp_FPController __instance)
    {
        EntityPlayerLocal local = __instance.localPlayer;
        if (local == null || local != GliderState.Player) return true;

        // The ceiling (R9) is tracked all the time, not only while gliding.
        GliderFlight.UpdateCeiling(__instance);
        GliderState.StepActive = false;

        if (!GliderState.IsActive) return true;
        if (__instance.MotorFreeFly)
        {
            GliderController.FoldActive("fly");
            return true;
        }
        // A grappling rope drives the controller from this same method. Fold before
        // writing anything, or the two velocities fight and the postfix reads the
        // rope's pull as a wall strike.
        if (GliderRope.Attached)
        {
            GliderController.FoldActive("rope");
            return true;
        }

        GliderConfig config = GliderMod.Settings;
        float deltaTime = Time.fixedDeltaTime;
        Vector3 velocity = GliderFlight.Step(config, __instance, local, deltaTime);

        // Unit rule (§3.1): world velocity = controller value x 60 x timeScale.
        float timeScale = Mathf.Max(0.0001f, Time.timeScale);
        float speedModifier = Mathf.Max(0.01f, __instance.m_SpeedModifier);
        __instance.m_MotorThrottle = new Vector3(
            velocity.x / (GliderFlight.VpStepsPerSecondFactor * timeScale * speedModifier),
            0f,
            velocity.z / (GliderFlight.VpStepsPerSecondFactor * timeScale * speedModifier));
        __instance.m_FallSpeed = velocity.y / (GliderFlight.VpStepsPerSecondFactor * timeScale);

        // The glider owns the velocity: shoves and explosions do not add to it.
        __instance.m_ExternalForce = Vector3.zero;

        // Hide the vanilla height-based impact for this step; the real one is
        // supplied by the postfix (or by the fold hand-back).
        __instance.m_MaxHeight = float.MinValue;
        __instance.m_MaxHeightInitialFallSpeed = 0f;

        GliderState.StepActive = true;
        GliderState.StepPosBefore = __instance.Transform.position;
        GliderState.StepIntended = velocity;
        return true;
    }

    [HarmonyPostfix]
    [HarmonyPatch("FixedMove")]
    private static void Postfix(vp_FPController __instance)
    {
        if (!GliderState.StepActive) return;
        GliderState.StepActive = false;

        EntityPlayerLocal local = __instance.localPlayer;
        if (local == null || local != GliderState.Player) return;

        GliderConfig config = GliderMod.Settings;
        float deltaTime = Time.fixedDeltaTime;
        Vector3 actual = deltaTime > 0f
            ? (__instance.Transform.position - GliderState.StepPosBefore) / deltaTime
            : Vector3.zero;
        Vector3 intended = GliderState.StepIntended;

        if (__instance.m_Grounded)
        {
            // Landing: a normal arrival is mostly sliding, so only part of the
            // tangent speed counts. A slow touch-down is then harmless.
            Vector3 normal = __instance.m_GroundHit.normal;
            if (normal.sqrMagnitude < 0.001f) normal = Vector3.up;
            float normalSpeed = Mathf.Max(0f, -Vector3.Dot(intended, normal));
            float tangentSpeed = (intended + normal * normalSpeed).magnitude;
            Arm(config, normalSpeed + config.CrashTangentFactor * tangentSpeed);
            GliderController.FoldActive("landed");
            return;
        }

        // Wall or ceiling: the world stopped part of the velocity. The step in
        // which contact starts is usually only partly blocked, so the speed lost
        // in it understates the hit (a 14 m/s wall strike showed up as 4.3 then
        // 9.2). The direction of the loss is the surface normal either way, so the
        // crash speed is the whole of the intended velocity along that direction.
        Vector3 lost = intended - actual;
        float loss = lost.magnitude;
        if (loss <= config.CrashMinSpeed) return;
        float crashSpeed = Mathf.Max(loss, Vector3.Dot(intended, lost / loss));

        if (crashSpeed >= config.CrashFoldSpeed)
        {
            // Flying onto a ladder is a grab, not a crash (Jason, 2026-10-12): the
            // wings fold, the speed is dropped and nothing is charged.
            if (LadderAhead(local, __instance, lost / loss))
            {
                if (config.Debug)
                    Log.Out("[GliderSuit] ladder grab: " + crashSpeed.ToString("F1") + " m/s strike forgiven.");
                GliderController.FoldActive("ladder", Vector3.zero);
                return;
            }

            Arm(config, crashSpeed);
            local.FallImpact(GliderState.PendingImpact);
            GliderController.FoldActive("crash", actual);
            return;
        }

        // A scrape: bleed the speed off and keep flying.
        GliderState.Speed = actual.magnitude;
        if (config.Debug)
            Log.Out("[GliderSuit] scrape: " + crashSpeed.ToString("F1") + " m/s into the surface, lost " +
                loss.ToString("F1") + " m/s.");
    }

    /// <summary>
    /// True if the player is on a ladder, or a ladder block is where they are or
    /// just ahead in the direction they were stopped, at feet, waist or head
    /// height. Checked from the blocks because the game's own "on a ladder" flag
    /// is only set a moment after contact.
    /// </summary>
    private static bool LadderAhead(EntityPlayerLocal player, vp_FPController controller, Vector3 direction)
    {
        if (player.isLadderAttached || player.IsInElevator()) return true;
        World world = player.world;
        if (world == null) return false;

        direction.y = 0f;
        if (direction.sqrMagnitude > 0.0001f) direction.Normalize();
        Vector3 feet = controller.Transform.position + Origin.position;
        for (int ahead = 0; ahead < 2; ahead++)
        {
            for (int level = 0; level < 3; level++)
            {
                Vector3 point = feet + direction * (ahead * LadderReach) + Vector3.up * (0.2f + level * 0.75f);
                BlockValue block = world.GetBlock(World.worldToBlockPos(point));
                if (!block.isair && block.Block.IsElevator((int)block.rotation)) return true;
            }
        }
        return false;
    }

    /// <summary>How far ahead of the player, in metres, a ladder block still counts as grabbed.</summary>
    private const float LadderReach = 0.7f;

    private static void Arm(GliderConfig config, float crashSpeed)
    {
        GliderState.PendingImpact = GliderFlight.Impact(crashSpeed) * config.CrashDamageScale;
        GliderState.ImpactArmedUntil = Time.time + config.LandingGraceSeconds;
        if (config.Debug)
            Log.Out("[GliderSuit] crash " + crashSpeed.ToString("F1") + " m/s -> impact " +
                GliderState.PendingImpact.ToString("F3") + " (armed " + config.LandingGraceMs + " ms)");
    }
}
