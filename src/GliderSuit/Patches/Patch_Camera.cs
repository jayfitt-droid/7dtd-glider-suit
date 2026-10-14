using HarmonyLib;
using UnityEngine;

/// <summary>
/// Camera roll while banking (D7). No pitch is added: the mouse owns the pitch.
/// camera_roll_angle = 0 disables the effect for anyone it makes queasy.
///
/// In third person it also keeps the character facing the way it is flying. The
/// game turns a third-person character to face whichever way the movement keys
/// point, so with Left/Right as the glider's steering it spun the character round
/// to face left or right, or back at the camera, on every turn.
/// </summary>
[HarmonyPatch(typeof(vp_FPCamera))]
internal static class Patch_Camera
{
    private static float roll;

    [HarmonyPostfix]
    [HarmonyPatch("LateUpdate")]
    private static void Postfix(vp_FPCamera __instance)
    {
        try
        {
            GliderConfig config = GliderMod.Settings;
            EntityPlayerLocal local = __instance.FPController != null ? __instance.FPController.localPlayer : null;
            if (local == null || local != GliderState.Player) return;

            // vp_FPCamera.Update turns the character toward targetRotation3P; this
            // runs after Update3rdPerson has set it from the movement keys.
            if (GliderState.IsActive && !local.bFirstPersonView)
                __instance.targetRotation3P = GliderState.HeadingDeg;

            float target = GliderState.IsActive ? -GliderState.Bank * config.CameraRollAngle : 0f;
            float rate = GliderState.IsActive
                ? 60f
                : Mathf.Max(1f, config.CameraRollAngle) / 0.3f;
            roll = Mathf.MoveTowards(roll, target, rate * Time.deltaTime);
            if (Mathf.Abs(roll) < 0.001f) return;
            __instance.Transform.localEulerAngles += Vector3.forward * roll;
        }
        catch (System.Exception)
        {
            // A camera effect is never worth breaking a frame over.
        }
    }

    internal static void Reset()
    {
        roll = 0f;
    }
}
