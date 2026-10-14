using System;
using UnityEngine;

internal enum WornKind
{
    World,
    Ui
}

/// <summary>
/// Owns every live wing instance: the one on the world rig's back, the one on
/// the inventory preview rig and the first-person one (§5.6). Instances are
/// parented under Spine3 (which survives the game's equipment rebuilds) and are
/// found by name before being created, so a rebuild never doubles or strands
/// them.
/// </summary>
internal static class GliderWorn
{
    internal const string RootName = "GliderSuitWorn";
    internal const string FirstPersonName = "GliderSuitFP";

    /// <summary>How far past the camera roll the first-person wings bank in a full turn, degrees.</summary>
    private const float FirstPersonExtraBank = 4f;

    private static GliderWings world;
    private static GliderWings preview;
    private static GliderWings firstPerson;

    /// <summary>Set by "glider foldpose" to hold the art at one fold fraction; below zero is off.</summary>
    internal static float HoldFold = -1f;

    /// <summary>Called from the SDCS postfixes and as the controller's backstop.</summary>
    internal static void Sync(EntityAlive entity, SDCSUtils.TransformCatalog catalog, WornKind kind)
    {
        if (entity == null || catalog == null) return;
        if (kind == WornKind.World && GameManager.IsDedicatedServer) return;

        Transform spine = Bone(catalog, "Spine3");
        if (spine == null) return;

        bool hasGlider = GliderEquip.HasGlider(entity);
        bool firstPerson = IsFirstPerson(entity);
        bool wanted = hasGlider && (kind == WornKind.Ui || !firstPerson);
        Transform existing = spine.Find(RootName);

        if (!wanted)
        {
            if (existing != null)
            {
                UnityEngine.Object.Destroy(existing.gameObject);
                // Logged unconditionally: this is the event that makes the wings
                // vanish off the player's back, and it must be visible without debug.
                Log.Out("[GliderSuit] worn " + kind + " wings removed: harness=" + hasGlider +
                    " firstPerson=" + firstPerson);
            }
            if (kind == WornKind.Ui) preview = null;
            else world = null;
            return;
        }

        GliderConfig config = GliderMod.Settings;
        GliderWings wings = existing != null ? existing.GetComponent<GliderWings>() : null;
        if (wings == null)
        {
            if (existing != null) UnityEngine.Object.Destroy(existing.gameObject);
            wings = GliderWings.Attach(spine, RootName, config.WornOffset, config.WornRotation, config.WornScale);
            if (wings == null) return;
            wings.BaseRotation = BodyAlignment(catalog, spine);
            Place(wings, config.WornOffset, config.WornRotation, config.WornScale);
            Log.Out("[GliderSuit] worn " + kind + " wings attached to " + spine.name +
                ", body alignment " + wings.BaseRotation.eulerAngles);
        }
        else
        {
            Place(wings, config.WornOffset, config.WornRotation, config.WornScale);
        }

        if (kind == WornKind.Ui) preview = wings;
        else world = wings;
        wings.SetFold(kind == WornKind.Ui ? 0f : GliderState.FoldFraction, false);
    }

    /// <summary>True while the third-person set is attached and alive.</summary>
    internal static bool WorldPresent => world != null;

    /// <summary>Re-applies placement live (used by "glider reload").</summary>
    internal static void ReapplyPlacement()
    {
        GliderConfig config = GliderMod.Settings;
        if (world != null) Place(world, config.WornOffset, config.WornRotation, config.WornScale);
        if (preview != null) Place(preview, config.WornOffset, config.WornRotation, config.WornScale);
    }

    /// <summary>
    /// The attitude the rig would have if the body were simply tipped forward into
    /// the flying pose, facing where the player faces: the wing model leans back from
    /// the spine by 90 minus WingLevelPitch, so tipping the rig WingLevelPitch forward
    /// lays the wings level. They follow the flight pitch by half and bank in turns.
    /// </summary>
    private static Quaternion FlightRotation(EntityPlayerLocal player, GliderConfig config)
    {
        float pitch = GliderPose.WingLevelPitch - 0.5f * GliderState.PitchDeg;
        float bank = -config.WingBankAngle * GliderState.Bank;
        return player.transform.rotation *
            Quaternion.AngleAxis(bank, Vector3.forward) *
            Quaternion.Euler(pitch, 0f, 0f);
    }

    /// <summary>
    /// The rotation, in the bone's own space, that points the rig's right, up and
    /// forward along the body's. The bone's axes cannot be assumed (the first real
    /// harness came out sideways on the back), so the body's are read from where
    /// the neck and the two shoulders sit relative to the bone. Those offsets are
    /// bone lengths: they do not change with the animation.
    /// </summary>
    private static Quaternion BodyAlignment(SDCSUtils.TransformCatalog catalog, Transform spine)
    {
        Transform neck = Bone(catalog, "Neck");
        Transform left = Bone(catalog, "LeftShoulder");
        Transform right = Bone(catalog, "RightShoulder");
        if (neck == null || left == null || right == null)
        {
            Log.Warning("[GliderSuit] Neck or shoulder bones missing; the worn wings may sit crooked.");
            return Quaternion.identity;
        }
        Vector3 up = spine.InverseTransformPoint(neck.position);
        Vector3 across = spine.InverseTransformPoint(right.position) - spine.InverseTransformPoint(left.position);
        across = Vector3.ProjectOnPlane(across, up);
        if (up.sqrMagnitude < 1e-8f || across.sqrMagnitude < 1e-8f) return Quaternion.identity;
        Vector3 forward = Vector3.Cross(across.normalized, up.normalized);
        return Quaternion.LookRotation(forward, up);
    }

    /// <summary>offset is in the body's axes: right, up the spine, out of the chest.</summary>
    private static void Place(GliderWings wings, Vector3 offset, Vector3 euler, float scale)
    {
        offset = wings.BaseRotation * offset;
        wings.transform.localPosition = offset;
        wings.PlacementEuler = euler;
        wings.transform.localScale = Vector3.one * Mathf.Max(0.01f, scale);
    }

    /// <summary>The per-frame visual: fold progress, visibility and the first-person set.</summary>
    internal static void Tick(EntityPlayerLocal player, GliderConfig config)
    {
        if (player == null || GameManager.IsDedicatedServer) return;

        if (HoldFold >= 0f) GliderState.FoldFraction = Mathf.Clamp01(HoldFold);
        else
        {
            float target = GliderState.IsActive ? 1f : 0f;
            float rate = GliderState.IsActive
                ? 1f / Mathf.Max(0.05f, config.UnfoldTime)
                : 1f / Mathf.Max(0.05f, config.FoldTime);
            GliderState.FoldFraction = Mathf.MoveTowards(GliderState.FoldFraction, target, rate * Time.deltaTime);
        }
        bool opening = GliderState.IsActive;
        float fold = GliderState.FoldFraction;

        if (world != null)
        {
            world.SetFold(fold, opening);
            world.gameObject.SetActive(!IsFirstPerson(player));
            // No bank of their own: the wings are fixed to the back, and GliderPose
            // banks the whole body.
            world.Tick(config, GliderState.Speed, GliderState.Phase == GliderPhase.Stalled, fold, 0f);
            world.AimWings(FlightRotation(player, config), fold);
        }
        if (preview != null)
        {
            preview.SetFold(0f, false);
            preview.Tick(config, 0f, false, 0f, 0f);
        }
        SyncFirstPerson(player, config, fold, opening);
    }

    private static void SyncFirstPerson(EntityPlayerLocal player, GliderConfig config, float fold, bool opening)
    {
        bool wanted = config.FpWings && IsFirstPerson(player) && fold > 0.01f;
        if (!wanted)
        {
            if (firstPerson != null)
            {
                UnityEngine.Object.Destroy(firstPerson.gameObject);
                firstPerson = null;
            }
            return;
        }

        Vector3 euler = new Vector3(config.FpWingPitch, 0f, 0f);
        float scale = Mathf.Max(0.1f, config.FpWingScale);
        if (firstPerson == null)
        {
            firstPerson = GliderWings.Attach(player.transform, FirstPersonName, config.FpWingOffset, euler, scale);
            if (firstPerson != null) firstPerson.SetHarnessVisible(false);
        }
        if (firstPerson != null)
        {
            // Re-read every frame so "glider reload" moves and resizes them live.
            firstPerson.transform.localPosition = config.FpWingOffset;
            firstPerson.transform.localScale = Vector3.one * scale;
            firstPerson.PlacementEuler = euler;
            firstPerson.SetFold(fold, opening);
            // The first-person wings sit just above the eye, so the full wing bank
            // (30 degrees) swung the inside wing down across the screen in a hard
            // turn. The camera already rolls with the turn; the wings roll with it
            // and only a little further, so they tilt on screen without covering it.
            firstPerson.Tick(config, GliderState.Speed, GliderState.Phase == GliderPhase.Stalled, fold,
                config.CameraRollAngle + FirstPersonExtraBank, GliderPose.BodyPitch(config, fold));
        }
    }

    internal static void Destroy()
    {
        if (world != null) UnityEngine.Object.Destroy(world.gameObject);
        if (preview != null) UnityEngine.Object.Destroy(preview.gameObject);
        if (firstPerson != null) UnityEngine.Object.Destroy(firstPerson.gameObject);
        world = null;
        preview = null;
        firstPerson = null;
        HoldFold = -1f;
    }

    private static bool IsFirstPerson(EntityAlive entity)
    {
        var local = entity as EntityPlayerLocal;
        return local != null && local.bFirstPersonView;
    }

    private static Transform Bone(SDCSUtils.TransformCatalog catalog, string name)
    {
        if (catalog == null || !catalog.ContainsKey(name)) return null;
        return catalog[name];
    }
}
