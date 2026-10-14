using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// One flying instance of the GliderSuitRig prefab: fold/unfold between the
/// authored extended pose and the *_Folded empties, bank with the turn, flex
/// with speed and flutter in a stall (§5.6). The prefab ships no script and no
/// Animator (bundles cannot reference mod-DLL scripts), so the folded pose is
/// data in the prefab and the motion is done here.
/// </summary>
internal sealed class GliderWings : MonoBehaviour
{
    private sealed class Spar
    {
        public Transform Bone;
        public Quaternion ExtendedRotation;
        public Vector3 ExtendedScale;
        public Quaternion FoldedRotation;
        public Vector3 FoldedScale;
        public int Order;
        /// <summary>The interpolated fold pose, kept so the speed flex does not compound.</summary>
        public Quaternion FoldRotation;
    }

    private readonly List<Spar> spars = new List<Spar>();

    /// <summary>The harness is several meshes (HarnessPlate, HarnessStrapA, HarnessBelt,
    /// HarnessBuckle), not one node, so every piece has to be found and hidden.</summary>
    private readonly List<Transform> harnessParts = new List<Transform>();

    /// <summary>Hub_L and Hub_R: each wing hangs from one, so turning a hub turns a whole wing.</summary>
    private readonly List<Transform> hubs = new List<Transform>();
    private float flutterTime;
    private float appliedFold = -1f;

    /// <summary>Euler rotation from the config; the dynamic bank and flex compose on top.</summary>
    internal Vector3 PlacementEuler;

    /// <summary>
    /// Turns the rig's axes (right, up the spine, out of the chest) onto the bone it
    /// hangs from, whose own axes are whatever the character rig gave it. Identity
    /// for the first-person set, which hangs from the upright player root.
    /// </summary>
    internal Quaternion BaseRotation = Quaternion.identity;

    internal static GliderWings Attach(Transform parent, string name, Vector3 offset, Vector3 euler, float scale)
    {
        GameObject prefab = GliderAssets.RigPrefab;
        if (prefab == null) return null;
        GameObject instance = UnityEngine.Object.Instantiate(prefab, parent, false);
        instance.name = name;
        instance.transform.localPosition = offset;
        instance.transform.localEulerAngles = euler;
        instance.transform.localScale = Vector3.one * Mathf.Max(0.01f, scale);
        GliderWings wings = instance.AddComponent<GliderWings>();
        wings.PlacementEuler = euler;
        wings.Build();
        return wings;
    }

    private void Build()
    {
        Transform root = transform;
        // The prefab's root carries the placement; the orientated rig is its child,
        // so pieces are found by name rather than by path.
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
        {
            if (child.name.StartsWith("Harness", StringComparison.Ordinal))
                harnessParts.Add(child);
            else if (child.name.StartsWith("Hub_", StringComparison.Ordinal))
                hubs.Add(child);
        }

        foreach (Transform bone in root.GetComponentsInChildren<Transform>(true))
        {
            if (!bone.name.StartsWith("Spar_", StringComparison.Ordinal)) continue;
            if (bone.name.EndsWith("_Folded", StringComparison.Ordinal)) continue;
            Transform folded = bone.parent != null ? bone.parent.Find(bone.name + "_Folded") : null;
            spars.Add(new Spar
            {
                Bone = bone,
                ExtendedRotation = bone.localRotation,
                ExtendedScale = bone.localScale,
                FoldedRotation = folded != null ? folded.localRotation : bone.localRotation,
                FoldedScale = folded != null ? folded.localScale : bone.localScale * 0.2f,
                Order = ParseOrder(bone.name),
            });
        }
        spars.Sort((a, b) => a.Order.CompareTo(b.Order));
    }

    private static int ParseOrder(string name)
    {
        int underscore = name.LastIndexOf('_');
        if (underscore >= 0 && int.TryParse(name.Substring(underscore + 1), out int index)) return index;
        return 0;
    }

    /// <summary>
    /// Third person: as the wings open they stop following the torso and turn to
    /// the flight attitude, level and facing the way the player flies, while the
    /// harness stays on the body. Folded (fold = 0) they hang from the torso as
    /// built. Without this the open wings sat wherever the torso happened to be
    /// twisted: aiming a weapon turns the chest half sideways, and once the body
    /// tips forward that twist stands one wing up and buries the other.
    /// Call it after the pose has moved the bones.
    /// </summary>
    internal void AimWings(Quaternion flightRotation, float fold)
    {
        float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(fold));
        foreach (Transform hub in hubs)
        {
            if (hub == null || hub.parent == null) continue;
            hub.rotation = Quaternion.Slerp(hub.parent.rotation, flightRotation, t);
        }
    }

    internal void SetHarnessVisible(bool visible)
    {
        foreach (Transform part in harnessParts)
            if (part != null) part.gameObject.SetActive(visible);
    }

    /// <summary>0 = fully folded, 1 = fully extended. The spars open leading-first.</summary>
    internal void SetFold(float fraction, bool opening)
    {
        fraction = Mathf.Clamp01(fraction);
        if (Mathf.Approximately(fraction, appliedFold)) return;
        appliedFold = fraction;
        int count = Mathf.Max(1, spars.Count);
        for (int i = 0; i < spars.Count; i++)
        {
            Spar spar = spars[i];
            float delay = (spar.Order / (float)count) * 0.25f;
            float t = Mathf.Clamp01((fraction - delay) / Mathf.Max(0.01f, 1f - delay));
            if (opening) t = GliderFlight.BackOut(t);
            else t = GliderFlight.EaseOut(t);
            spar.FoldRotation = Quaternion.Slerp(spar.FoldedRotation, spar.ExtendedRotation, t);
            spar.Bone.localRotation = spar.FoldRotation;
            spar.Bone.localScale = Vector3.Lerp(spar.FoldedScale, spar.ExtendedScale, t);
        }
    }

    /// <summary>Bank, speed flex and the stall flutter, composed onto the placement.</summary>
    internal void Tick(GliderConfig config, float speed, bool stalled, float fold, float bankAngle,
        float bodyPitch = 0f)
    {
        flutterTime += Time.deltaTime * 30f;
        float flex = Mathf.Clamp01(speed / 30f) * 4f * fold;
        float bank = -bankAngle * GliderState.Bank * fold;
        float shake = stalled ? Mathf.Sin(flutterTime) * 2.5f * fold : 0f;
        if (bodyPitch != 0f)
        {
            // First person: there is no body to tip forward, so the wing set is
            // tipped by the same angle the third-person torso is, and banked about
            // the player's forward axis rather than the rig's own.
            transform.localRotation =
                Quaternion.AngleAxis(bank + shake, Vector3.forward) *
                Quaternion.Euler(PlacementEuler.x + bodyPitch + flex * 0.5f, PlacementEuler.y, PlacementEuler.z);
        }
        else
        {
            transform.localRotation = BaseRotation * Quaternion.Euler(
                PlacementEuler.x + flex * 0.5f,
                PlacementEuler.y,
                PlacementEuler.z + bank + shake);
        }

        // The ribs sweep a touch with speed. This used to grow with the bone's index,
        // which suited three fanned spars and would bend the outer ribs of the
        // seven-rib wing by 28 degrees.
        for (int i = 0; i < spars.Count; i++)
        {
            Spar spar = spars[i];
            if (spar.Order < 1) continue;
            spar.Bone.localRotation = spar.FoldRotation * Quaternion.Euler(0f, 0f, flex * 0.4f);
        }
    }
}
