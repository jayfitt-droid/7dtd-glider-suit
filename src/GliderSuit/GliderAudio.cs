using UnityEngine;

/// <summary>
/// The unfold and fold one-shots and the speed-driven wind loop (§5.11). The
/// clips come from the bundle; if one is missing the mod logs once and falls
/// back to the vanilla parachute open sound, or to silence.
/// </summary>
internal static class GliderAudio
{
    private const float WindFadeSeconds = 0.3f;

    private static AudioSource oneShot;
    private static AudioSource wind;
    private static bool warnedUnfold;
    private static bool warnedFold;

    internal static void PlayUnfold(EntityPlayerLocal player)
    {
        // D45: off by default; the buckle click in the generated clips beeps.
        if (player == null || !GliderMod.Settings.FoldSounds) return;
        GliderConfig config = GliderMod.Settings;
        AudioClip clip = GliderAssets.UnfoldClip;
        if (clip != null)
        {
            Ensure(player);
            oneShot.Stop();
            oneShot.clip = clip;
            oneShot.volume = config.SoundVolume;
            oneShot.Play();
            return;
        }
        if (!warnedUnfold)
        {
            warnedUnfold = true;
            Log.Warning("[GliderSuit] Unfold sound missing from the bundle; using the vanilla open sound.");
        }
        Audio.Manager.PlayInsidePlayerHead("Parachute_Opens", player.entityId, 0f, false, false);
    }

    internal static void PlayFold(EntityPlayerLocal player)
    {
        if (player == null || !GliderMod.Settings.FoldSounds) return;
        AudioClip clip = GliderAssets.FoldClip;
        if (clip == null)
        {
            if (!warnedFold)
            {
                warnedFold = true;
                Log.Warning("[GliderSuit] Fold sound missing from the bundle; folding stays silent.");
            }
            return;
        }
        Ensure(player);
        oneShot.Stop();
        oneShot.clip = clip;
        oneShot.volume = GliderMod.Settings.SoundVolume;
        oneShot.Play();
    }

    /// <summary>
    /// The wind follows the airspeed and fades out over 0.3 s when the wings
    /// fold. Nothing is created or loaded until the wings have actually been used.
    /// </summary>
    internal static void Tick(EntityPlayerLocal player, GliderConfig config)
    {
        if (player == null || GameManager.IsDedicatedServer) return;
        if (wind == null && !GliderState.IsActive && !GliderState.InGrace) return;

        AudioClip clip = GliderAssets.WindClip;
        if (clip == null) return;
        Ensure(player);
        if (wind.clip != clip)
        {
            wind.clip = clip;
            wind.loop = true;
        }

        float cruise = Mathf.Max(0.01f, config.CruiseSpeed);
        float level = Mathf.Clamp01(GliderState.Speed / cruise);
        Attitude(config, out float dive, out float climb);
        bool wanted = config.WindLoop && GliderState.IsActive;
        float target = 0f;
        if (wanted)
        {
            // D29: the nose angle is heard as well as the speed. A dive is louder,
            // a climb fades toward wind_climb_volume, and a stall is nearly silent.
            float attitude = 1f + config.WindDiveBoost * dive - (1f - config.WindClimbVolume) * climb;
            target = Mathf.Clamp01(config.WindVolume * level * level * attitude);
            if (GliderState.Phase == GliderPhase.Stalled) target = Mathf.Min(target, config.WindVolume * config.WindStallVolume);
        }

        wind.volume = Mathf.MoveTowards(wind.volume, target, Time.deltaTime / WindFadeSeconds);
        wind.pitch = Mathf.Lerp(0.8f, 1.5f, Mathf.Clamp01(GliderState.Speed / 30f)) * (1f + 0.15f * dive - 0.15f * climb);
        TargetVolume = target;

        if (wanted && !wind.isPlaying && target > 0.001f) wind.Play();
        else if (!wanted && wind.isPlaying && wind.volume <= 0.001f) wind.Stop();
    }

    /// <summary>The loop's wanted volume this frame, for the debug log.</summary>
    internal static float TargetVolume;

    /// <summary>
    /// How far the nose is below the glide angle (dive, 0 at the glide angle,
    /// 1 at max_dive_angle) or above it (climb, 1 at max_climb_angle).
    /// </summary>
    private static void Attitude(GliderConfig config, out float dive, out float climb)
    {
        float trim = -Mathf.Atan(1f / Mathf.Max(0.01f, config.GlideRatio)) * Mathf.Rad2Deg;
        float pitch = GliderState.PitchDeg;
        dive = Mathf.Clamp01((trim - pitch) / Mathf.Max(1f, config.MaxDiveAngle + trim));
        climb = Mathf.Clamp01((pitch - trim) / Mathf.Max(1f, config.MaxClimbAngle - trim));
    }

    private static void Ensure(EntityPlayerLocal player)
    {
        if (oneShot != null) return;
        var oneShotObject = new GameObject("GliderSuitAudio");
        oneShotObject.transform.SetParent(player.transform, false);
        oneShot = oneShotObject.AddComponent<AudioSource>();
        oneShot.spatialBlend = 0f;
        oneShot.playOnAwake = false;

        var windObject = new GameObject("GliderSuitWind");
        windObject.transform.SetParent(player.transform, false);
        wind = windObject.AddComponent<AudioSource>();
        wind.spatialBlend = 0f;
        wind.playOnAwake = false;
        wind.loop = true;
        wind.volume = 0f;
    }

    internal static void Reset()
    {
        if (oneShot != null) Object.Destroy(oneShot.gameObject);
        if (wind != null) Object.Destroy(wind.gameObject);
        oneShot = null;
        wind = null;
        warnedUnfold = false;
        warnedFold = false;
    }
}
