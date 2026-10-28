using HarmonyLib;

/// <summary>
/// The gliding pose for other players (D47). Patch_AvatarAnimation only reaches
/// the owner's own game: the walk values (forward, strafe, moving, in the air and
/// the movement state) are not sent between games, each game works them out from
/// the player's replicated speed every frame. Without this, other players would
/// run or flail in the air under their wings (the Parachute hit exactly that).
/// Other players' characters use AvatarSDCSController, whose setters are declared
/// on LegacyAvatarController. Prepare switches this off if a game update renames
/// them, so only the pose is lost.
/// </summary>
[HarmonyPatch(typeof(LegacyAvatarController))]
internal static class Patch_RemoteAvatarAnimation
{
    [HarmonyPrepare]
    private static bool Prepare()
    {
        bool found =
            AccessTools.DeclaredMethod(typeof(LegacyAvatarController), "_setBool", new[] { typeof(int), typeof(bool), typeof(bool) }) != null &&
            AccessTools.DeclaredMethod(typeof(LegacyAvatarController), "_setFloat", new[] { typeof(int), typeof(float), typeof(bool) }) != null &&
            AccessTools.DeclaredMethod(typeof(LegacyAvatarController), "_setInt", new[] { typeof(int), typeof(int), typeof(bool) }) != null;
        if (!found)
            Log.Error("[GliderSuit] LegacyAvatarController setters not found; other players keep their walk animation while gliding.");
        return found;
    }

    [HarmonyPrefix]
    [HarmonyPatch("_setBool", typeof(int), typeof(bool), typeof(bool))]
    private static void BoolPrefix(LegacyAvatarController __instance, int _propertyHash, ref bool _value)
    {
        if (!IsRemoteOpen(__instance)) return;
        if (_propertyHash == AvatarController.inAirHash || _propertyHash == AvatarController.isMovingHash) _value = false;
    }

    [HarmonyPrefix]
    [HarmonyPatch("_setFloat", typeof(int), typeof(float), typeof(bool))]
    private static void FloatPrefix(LegacyAvatarController __instance, int _propertyHash, ref float _value)
    {
        if (!IsRemoteOpen(__instance)) return;
        if (_propertyHash == AvatarController.forwardHash || _propertyHash == AvatarController.strafeHash) _value = 0f;
    }

    [HarmonyPrefix]
    [HarmonyPatch("_setInt", typeof(int), typeof(int), typeof(bool))]
    private static void IntPrefix(LegacyAvatarController __instance, int _propertyHash, ref int _value)
    {
        if (!IsRemoteOpen(__instance)) return;
        if (_propertyHash == AvatarController.movementStateHash) _value = 0;
    }

    private static bool IsRemoteOpen(AvatarController controller)
    {
        EntityAlive entity = controller.Entity;
        return entity is EntityPlayer && !(entity is EntityPlayerLocal) && GliderRemote.IsOpen(entity.entityId);
    }
}
