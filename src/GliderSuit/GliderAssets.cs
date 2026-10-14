using System;
using UnityEngine;

/// <summary>
/// Art and audio from the mod's asset bundle (built by the Unity project in
/// unity/GliderSuitAssets). Loaded through the game's DataLoader so the bundle
/// is cached alongside the game's own. Everything degrades gracefully when the
/// bundle is missing: the wings stay invisible and the vanilla parachute open
/// sound is played instead.
/// </summary>
internal static class GliderAssets
{
    private const string BundleUri = "#@modfolder(GliderSuit):Resources/GliderSuit.unity3d?";
    internal const string RigName = "GliderSuitRig";
    internal const string UnfoldClipName = "GliderUnfold";
    internal const string FoldClipName = "GliderFold";
    internal const string WindClipName = "GliderWind";

    private static bool loaded;
    private static bool warnedMissing;
    private static GameObject rigPrefab;
    private static AudioClip unfoldClip;
    private static AudioClip foldClip;
    private static AudioClip windClip;

    internal static GameObject RigPrefab { get { Load(); return rigPrefab; } }
    internal static AudioClip UnfoldClip { get { Load(); return unfoldClip; } }
    internal static AudioClip FoldClip { get { Load(); return foldClip; } }
    internal static AudioClip WindClip { get { Load(); return windClip; } }

    private static void Load()
    {
        if (loaded) return;
        loaded = true;
        try
        {
            rigPrefab = DataLoader.LoadAsset<GameObject>(BundleUri + RigName, false);
        }
        catch (Exception ex)
        {
            Log.Warning("[GliderSuit] Wing model failed to load: " + ex.Message);
        }
        unfoldClip = LoadClip(UnfoldClipName);
        foldClip = LoadClip(FoldClipName);
        windClip = LoadClip(WindClipName);

        int found = (rigPrefab != null ? 1 : 0) + (unfoldClip != null ? 1 : 0) +
            (foldClip != null ? 1 : 0) + (windClip != null ? 1 : 0);
        if (found == 4)
            Log.Out("[GliderSuit] Asset bundle loaded: rig, unfold, fold and wind.");
        else if (found == 0 && !warnedMissing)
        {
            warnedMissing = true;
            Log.Warning("[GliderSuit] Asset bundle missing; the wings are invisible and the vanilla open sound is used.");
        }
        else
            Log.Warning("[GliderSuit] Asset bundle incomplete (" + found + "/4): rig " +
                (rigPrefab != null ? "ok" : "missing") + ", unfold " + (unfoldClip != null ? "ok" : "missing") +
                ", fold " + (foldClip != null ? "ok" : "missing") + ", wind " + (windClip != null ? "ok" : "missing") + ".");
    }

    private static AudioClip LoadClip(string name)
    {
        try
        {
            return DataLoader.LoadAsset<AudioClip>(BundleUri + name, false);
        }
        catch (Exception ex)
        {
            Log.Warning("[GliderSuit] Sound " + name + " failed to load: " + ex.Message);
            return null;
        }
    }

    internal static void Clear()
    {
        loaded = false;
        rigPrefab = null;
        unfoldClip = null;
        foldClip = null;
        windClip = null;
    }
}
