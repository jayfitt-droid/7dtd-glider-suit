using HarmonyLib;

/// <summary>
/// Keeps the body out of the flailing fall animation while the wings are open.
/// The game feeds the animator "in the air" and the walk/run speeds every frame;
/// while the glider is active those are replaced with "standing still", so the
/// character holds its idle pose under the wings. The arms are deliberately left
/// alone (D7): weapons stay usable and their animation stays correct.
/// </summary>
[HarmonyPatch(typeof(AvatarLocalPlayerController))]
internal static class Patch_AvatarAnimation
{
    [HarmonyPrefix]
    [HarmonyPatch("_setBool", typeof(int), typeof(bool), typeof(bool))]
    private static void BoolPrefix(AvatarLocalPlayerController __instance, int _pid, ref bool _value)
    {
        if (!IsLocalActive(__instance)) return;
        if (_pid == AvatarController.inAirHash || _pid == AvatarController.isMovingHash) _value = false;
    }

    [HarmonyPrefix]
    [HarmonyPatch("_setFloat", typeof(int), typeof(float), typeof(bool))]
    private static void FloatPrefix(AvatarLocalPlayerController __instance, int _pid, ref float _value)
    {
        if (!IsLocalActive(__instance)) return;
        if (_pid == AvatarController.forwardHash || _pid == AvatarController.strafeHash) _value = 0f;
    }

    [HarmonyPrefix]
    [HarmonyPatch("_setInt", typeof(int), typeof(int), typeof(bool))]
    private static void IntPrefix(AvatarLocalPlayerController __instance, int _pid, ref int _value)
    {
        if (!IsLocalActive(__instance)) return;
        if (_pid == AvatarController.movementStateHash) _value = 0;
    }

    private static bool IsLocalActive(AvatarController controller)
    {
        return GliderState.IsActive && GliderState.Player != null && controller.Entity == GliderState.Player;
    }
}
