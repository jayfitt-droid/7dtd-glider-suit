using UnityEngine;

/// <summary>
/// Third-person body pose while gliding (D7, §5.7): the torso goes horizontal and
/// banks with the turn, and the arm roots and neck are counter-rotated so the
/// arms, weapon and gaze still point where the player aims. The overrides are
/// written in LateUpdate, after the Animator has posed the bones, so they hold
/// for exactly one frame and need no cleanup.
/// </summary>
internal static class GliderPose
{
    /// <summary>Confirmed by "glider bones" in game. The upper arms are children of the
    /// shoulders, so only one of the two pairs may be rotated or the arms are levelled twice.</summary>
    private static readonly string[] ShoulderBones = { "LeftShoulder", "RightShoulder" };
    private static readonly string[] FallbackArmBones = { "LeftArm", "RightArm" };

    /// <summary>
    /// The forward tip, in degrees, at which the wing model is level: 90 minus
    /// WING_TILT in unity/blender/build_assets.py. Change one and the other must follow.
    /// </summary>
    internal const float WingLevelPitch = 65f;

    internal static void LateUpdate(EntityPlayerLocal player, GliderConfig config)
    {
        float blend = GliderState.FoldFraction;
        if (blend <= 0.001f || player == null || player.bFirstPersonView) return;

        EModelSDCS model = player.emodel as EModelSDCS;
        if (model == null || model.boneCatalog == null) return;

        Vector3 right = player.transform.right;
        float bodyPitch = BodyPitch(config, blend);
        float bank = config.BodyBankAngle * GliderState.Bank * blend;

        // Bank after tipping (D43): the roll is about the flight direction, so the
        // lying body rolls with the wings. Rolling first, about the upright body's
        // forward axis, turned into a twist about the vertical once tipped 85°.
        Rotate(model.boneCatalog, "Hips", Quaternion.AngleAxis(-bank, player.transform.forward) * Quaternion.AngleAxis(bodyPitch, right));

        Quaternion level = Quaternion.AngleAxis(-bodyPitch * config.ArmLevelFactor, right);
        if (!RotateAll(model.boneCatalog, ShoulderBones, level))
            RotateAll(model.boneCatalog, FallbackArmBones, level);
        Rotate(model.boneCatalog, "Neck", Quaternion.AngleAxis(-bodyPitch * 0.6f * config.ArmLevelFactor, right));
    }

    /// <summary>
    /// How far forward the torso is tipped, in degrees: body_pitch_base in level
    /// flight, further in a dive and less in a climb, by half the flight pitch.
    /// (Flight pitch is negative nose-down, hence the minus; it was a plus, which
    /// stood the body up in a dive.) The open wings do not use it: they are aimed
    /// in world space (D27), so the body can lie flatter than the wing model's
    /// WingLevelPitch (D36).
    /// </summary>
    internal static float BodyPitch(GliderConfig config, float blend)
    {
        return (config.BodyPitchBase - 0.5f * GliderState.PitchDeg) * blend;
    }

    /// <summary>
    /// The forward tip that lays the wing model level, following the flight pitch
    /// by half like the body does. The first-person wings hang from the upright
    /// player root and are tipped by this (D36; they used BodyPitch, which only
    /// matched while body_pitch_base was 65).
    /// </summary>
    internal static float WingPitch(float blend)
    {
        return (WingLevelPitch - 0.5f * GliderState.PitchDeg) * blend;
    }

    /// <summary>Rotates every named bone that exists; false if none of them did.</summary>
    private static bool RotateAll(SDCSUtils.TransformCatalog catalog, string[] names, Quaternion delta)
    {
        bool any = false;
        foreach (string name in names)
        {
            if (catalog == null || !catalog.ContainsKey(name)) continue;
            Rotate(catalog, name, delta);
            any = true;
        }
        return any;
    }

    private static void Rotate(SDCSUtils.TransformCatalog catalog, string name, Quaternion delta)
    {
        if (catalog == null || !catalog.ContainsKey(name)) return;
        Transform bone = catalog[name];
        if (bone == null) return;
        bone.rotation = delta * bone.rotation;
    }
}
