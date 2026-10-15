using HarmonyLib;
using UnityEngine;

/// <summary>
/// D40: while gliding in third person, the camera sits where the game puts it
/// for a vehicle instead of over the shoulder: centred behind the player, higher,
/// and further back the faster the glider goes.
///
/// The vehicle camera is the same vp_FPCamera.Update3rdPerson code; the only
/// difference is the offset it is fed. On foot EntityPlayerLocal.FrameUpdateCamera
/// sets Position3rdPersonOffset to (0.4, 1.65, 1.2) every frame; in a vehicle
/// EntityVehicle.GetCameraOffset feeds (0, 0, distance) around a point 1.8 m up,
/// the distance blended from the vehicle's cameraDistance by speed. This postfix
/// overwrites the on-foot value the same way. Nothing else changes, so the
/// toolbar and weapons work as before. While aiming, the game's own over-the-
/// shoulder offset is left alone so the sights still come in close.
/// </summary>
[HarmonyPatch(typeof(EntityPlayerLocal))]
internal static class Patch_GlideCamera
{
    [HarmonyPostfix]
    [HarmonyPatch("FrameUpdateCamera")]
    private static void Postfix(EntityPlayerLocal __instance)
    {
        try
        {
            GliderConfig config = GliderMod.Settings;
            if (!config.GlideCamera || !GliderState.IsActive) return;
            if (__instance != GliderState.Player || __instance.bFirstPersonView) return;
            if (__instance.AttachedToEntity != null || __instance.AimingGun) return;
            vp_FPCamera camera = __instance.vp_FPCamera;
            if (camera == null || camera.Locked3rdPerson) return;

            // vp_FPCamera.Update eases its current offset toward this at 10/s, so
            // the change on opening and folding is already smooth.
            float fraction = Mathf.Clamp01(GliderState.Speed / (2f * config.CruiseSpeed));
            float distance = Mathf.Lerp(config.GlideCameraDistanceMin, config.GlideCameraDistanceMax, fraction);
            camera.Position3rdPersonOffset = new Vector3(0f, config.GlideCameraHeight, distance);
        }
        catch (System.Exception)
        {
            // A camera effect is never worth breaking a frame over.
        }
    }
}
