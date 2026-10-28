using System;
using System.Globalization;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEngine;

/// <summary>
/// Every tunable in one place. The file is JSON with // comments; Json.NET
/// ignores comments by default. An invalid value logs a warning and falls back
/// to the default, so a bad edit can never break the mod.
/// </summary>
internal sealed class GliderConfig
{
    public int FoldInputLockoutMs = 250;
    public bool AutoThirdPerson = true;
    public int ThirdPersonAfterMs = 500;
    public bool GlideCamera = true;
    public float GlideCameraHeight = 1.8f;
    public float GlideCameraDistanceMin = 4f;
    public float GlideCameraDistanceMax = 7f;
    public int ReactivateCooldownMs = 400;
    public float MinActivateHeight = 0.5f;
    public float LaunchSpeed = 12f;

    public float CruiseSpeed = 25f;
    public float GlideRatio = 12f;
    public float StallSpeed = 8f;
    public float StallRecoverSpeed = 11f;
    public float StallMinTime = 0.6f;
    public float StallDiveAngle = 55f;
    public float StallPitchRate = 120f;
    public float StallTurnFactor = 0.3f;

    public float PitchRate = 40f;
    public float PitchReturnRate = 0f;
    public float OpenTrimRate = 60f;
    public float MaxDiveAngle = 70f;
    public float MaxClimbAngle = 30f;

    public float TurnRate = 40f;
    public float HeadingFollowRate = 60f;
    public float TurnDrag = 0.6f;

    public float GlideGravity = 0f;
    public bool TakeoffCeiling;
    public float CeilingMargin = 0.25f;
    public float CeilingSoftenHeight = 2f;

    public float FoldAirDrag = 0.004f;
    public float CrashTangentFactor = 0.25f;
    public float CrashMinSpeed = 3f;
    public float CrashFoldSpeed = 6f;
    public float CrashDamageScale = 0.5f;
    public int LandingGraceMs = 300;

    public float UnfoldTime = 0.6f;
    public float FoldTime = 0.45f;
    public float CameraRollAngle = 8f;
    public float BodyPitchBase = 85f;
    public float BodyBankAngle = 30f;
    public float ArmLevelFactor = 1f;

    public Vector3 WornOffset = new Vector3(0f, 0f, -0.09f);
    public Vector3 WornRotation = Vector3.zero;
    public float WornScale = 1f;
    // The rig's origin is where the wing panels start, so in first person it must
    // sit above and behind the eye or the tarp is laid across the face.
    public Vector3 FpWingOffset = new Vector3(0f, 1.95f, 0.30f);
    public float FpWingPitch = 0f;
    public float FpWingScale = 0.45f;
    public bool FpWings = true;

    public bool FoldSounds;
    public bool WindLoop = true;
    public float WindVolume = 0.5f;
    public float WindDiveBoost = 0.6f;
    public float WindClimbVolume = 0.3f;
    public float WindStallVolume = 0.1f;
    public float SoundVolume = 1f;
    public bool Debug;

    /// <summary>True when GliderSuitDev.json was found and read.</summary>
    public bool DevFile;

    public float FoldInputLockoutSeconds => FoldInputLockoutMs / 1000f;
    public float ReactivateCooldownSeconds => ReactivateCooldownMs / 1000f;
    public float LandingGraceSeconds => LandingGraceMs / 1000f;

    internal const string PlayerFileName = "GliderSuitConfig.json";

    /// <summary>
    /// Jason's tuning file (2026-10-27): every option, read after the player file
    /// and winning over it. It is not shipped (package.ps1 leaves it out), so
    /// players see only the short player file, and every option they do not have
    /// takes the default written in this class.
    /// </summary>
    internal const string DevFileName = "GliderSuitDev.json";

    public static GliderConfig Load(string modDirectory)
    {
        var result = new GliderConfig();
        string path = Path.Combine(modDirectory, "Config", PlayerFileName);
        string devPath = Path.Combine(modDirectory, "Config", DevFileName);
        bool hasPlayer = File.Exists(path);
        bool hasDev = File.Exists(devPath);
        if (!hasPlayer) Log.Warning("[GliderSuit] Config missing; using defaults: " + path);
        if (!hasPlayer && !hasDev) return result;
        try
        {
            JObject data = hasPlayer ? JObject.Parse(File.ReadAllText(path)) : new JObject();
            if (hasDev)
            {
                JObject dev = JObject.Parse(File.ReadAllText(devPath));
                foreach (JProperty property in dev.Properties()) data[property.Name] = property.Value;
                Log.Out("[GliderSuit] Dev options: " + dev.Count + " values from " + devPath);
                result.DevFile = true;
            }
            result.FoldInputLockoutMs = Integer(data, "fold_input_lockout_ms", 250, 0, 1000);
            result.AutoThirdPerson = Boolean(data, "auto_third_person", true);
            result.ThirdPersonAfterMs = Integer(data, "third_person_after_ms", 500, 0, 10000);
            result.GlideCamera = Boolean(data, "glide_camera", true);
            result.GlideCameraHeight = Number(data, "glide_camera_height", 1.8f, 0f, 5f);
            result.GlideCameraDistanceMin = Number(data, "glide_camera_distance_min", 4f, 1f, 20f);
            result.GlideCameraDistanceMax = Number(data, "glide_camera_distance_max", 7f, 1f, 20f);
            result.ReactivateCooldownMs = Integer(data, "reactivate_cooldown_ms", 400, 0, 5000);
            result.MinActivateHeight = Number(data, "min_activate_height", 0.5f, 0f, 50f);
            result.LaunchSpeed = Number(data, "launch_speed", 12f, 0f, 40f);

            result.CruiseSpeed = Number(data, "cruise_speed", 25f, 6f, 40f);
            result.GlideRatio = Number(data, "glide_ratio", 12f, 1.5f, 12f);
            result.StallSpeed = Number(data, "stall_speed", 8f, 2f, 30f);
            result.StallRecoverSpeed = NumberAbove(data, "stall_recover_speed", 11f, result.StallSpeed, 60f);
            result.StallMinTime = Number(data, "stall_min_time", 0.6f, 0f, 5f);
            result.StallDiveAngle = Number(data, "stall_dive_angle", 55f, 20f, 85f);
            result.StallPitchRate = Number(data, "stall_pitch_rate", 120f, 30f, 360f);
            result.StallTurnFactor = Number(data, "stall_turn_factor", 0.3f, 0f, 1f);

            result.PitchRate = Number(data, "pitch_rate", 40f, 5f, 180f);
            result.PitchReturnRate = Number(data, "pitch_return_rate", 0f, 0f, 90f);
            result.OpenTrimRate = Number(data, "open_trim_rate", 60f, 0f, 360f);
            result.MaxDiveAngle = Number(data, "max_dive_angle", 70f, 20f, 89f);
            result.MaxClimbAngle = Number(data, "max_climb_angle", 30f, 0f, 60f);

            result.TurnRate = Number(data, "turn_rate", 40f, 10f, 180f);
            result.HeadingFollowRate = Number(data, "heading_follow_rate", 60f, 30f, 720f);
            result.TurnDrag = Number(data, "turn_drag", 0.6f, 0f, 2f);

            result.GlideGravity = Number(data, "glide_gravity", 0f, 0f, 30f);
            result.TakeoffCeiling = Boolean(data, "takeoff_ceiling", false);
            result.CeilingMargin = Number(data, "ceiling_margin", 0.25f, 0f, 5f);
            result.CeilingSoftenHeight = Number(data, "ceiling_soften_height", 2f, 0.1f, 10f);

            result.FoldAirDrag = Number(data, "fold_air_drag", 0.004f, 0f, 1f);
            result.CrashTangentFactor = Number(data, "crash_tangent_factor", 0.25f, 0f, 1f);
            result.CrashMinSpeed = Number(data, "crash_min_speed", 3f, 0.5f, 20f);
            result.CrashFoldSpeed = Number(data, "crash_fold_speed", 6f, 1f, 40f);
            result.CrashDamageScale = Number(data, "crash_damage_scale", 0.5f, 0f, 5f);
            result.LandingGraceMs = Integer(data, "landing_grace_ms", 300, 0, 2000);

            result.UnfoldTime = Number(data, "unfold_time", 0.6f, 0.1f, 3f);
            result.FoldTime = Number(data, "fold_time", 0.45f, 0.05f, 3f);
            result.CameraRollAngle = Number(data, "camera_roll_angle", 8f, 0f, 30f);
            result.BodyPitchBase = Number(data, "body_pitch_base", 85f, 0f, 90f);
            result.BodyBankAngle = Number(data, "body_bank_angle", 30f, 0f, 60f);
            result.ArmLevelFactor = Number(data, "arm_level_factor", 1f, 0f, 1f);

            result.WornOffset = Vector(data, "worn_offset", result.WornOffset, -5f, 5f);
            result.WornRotation = Vector(data, "worn_rotation", result.WornRotation, -180f, 180f);
            result.WornScale = Number(data, "worn_scale", 1f, 0.01f, 10f);
            result.FpWingOffset = Vector(data, "fp_wing_offset", result.FpWingOffset, -5f, 5f);
            result.FpWingPitch = Number(data, "fp_wing_pitch", 0f, -180f, 180f);
            result.FpWingScale = Number(data, "fp_wing_scale", 0.45f, 0.1f, 2f);
            result.FpWings = Boolean(data, "fp_wings", true);

            result.FoldSounds = Boolean(data, "fold_sounds", false);
            result.WindLoop = Boolean(data, "wind_loop", true);
            result.WindVolume = Number(data, "wind_volume", 0.5f, 0f, 1f);
            result.WindDiveBoost = Number(data, "wind_dive_boost", 0.6f, 0f, 3f);
            result.WindClimbVolume = Number(data, "wind_climb_volume", 0.3f, 0f, 1f);
            result.WindStallVolume = Number(data, "wind_stall_volume", 0.1f, 0f, 1f);
            result.SoundVolume = Number(data, "sound_volume", 1f, 0f, 1f);
            result.Debug = Boolean(data, "debug", false);
            if (hasPlayer) Log.Out("[GliderSuit] Config loaded from " + path);
        }
        catch (Exception ex)
        {
            Log.Warning("[GliderSuit] Invalid config; using defaults: " + ex.Message);
            return new GliderConfig();
        }
        return result;
    }

    private static Vector3 Vector(JObject data, string key, Vector3 fallback, float min, float max)
    {
        JToken token = data[key];
        if (token == null) return fallback;
        if (token.Type == JTokenType.Array && ((JArray)token).Count == 3)
        {
            var array = (JArray)token;
            var values = new float[3];
            for (int i = 0; i < 3; i++)
            {
                float component;
                if (!float.TryParse(array[i].ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out component) ||
                    float.IsNaN(component) || float.IsInfinity(component) || component < min || component > max)
                {
                    Log.Warning("[GliderSuit] Invalid " + key + " (each component " + min + " to " + max + "); using " + fallback);
                    return fallback;
                }
                values[i] = component;
            }
            return new Vector3(values[0], values[1], values[2]);
        }
        Log.Warning("[GliderSuit] Invalid " + key + " (expected [x, y, z]); using " + fallback);
        return fallback;
    }

    private static float Number(JObject data, string key, float fallback, float min, float max)
    {
        JToken token = data[key];
        if (token == null) return fallback;
        float value;
        if ((token.Type == JTokenType.Float || token.Type == JTokenType.Integer) &&
            float.TryParse(token.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out value) &&
            !float.IsNaN(value) && !float.IsInfinity(value) && value >= min && value <= max)
            return value;
        Log.Warning("[GliderSuit] Invalid " + key + " (allowed " + min + " to " + max + "); using " + fallback);
        return fallback;
    }

    /// <summary>For values that must stay above another setting (stall_recover_speed).</summary>
    private static float NumberAbove(JObject data, string key, float fallback, float above, float max)
    {
        JToken token = data[key];
        if (token == null) return fallback;
        float value;
        if ((token.Type == JTokenType.Float || token.Type == JTokenType.Integer) &&
            float.TryParse(token.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out value) &&
            !float.IsNaN(value) && !float.IsInfinity(value) && value > above && value <= max)
            return value;
        Log.Warning("[GliderSuit] Invalid " + key + " (must be above stall_speed " + above +
            " and at most " + max + "); using " + fallback);
        return fallback;
    }

    private static int Integer(JObject data, string key, int fallback, int min, int max)
    {
        JToken token = data[key];
        if (token == null) return fallback;
        int value;
        if ((token.Type == JTokenType.Integer || token.Type == JTokenType.Float) &&
            int.TryParse(token.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value) &&
            value >= min && value <= max)
            return value;
        Log.Warning("[GliderSuit] Invalid " + key + " (allowed " + min + " to " + max + "); using " + fallback);
        return fallback;
    }

    private static bool Boolean(JObject data, string key, bool fallback)
    {
        JToken token = data[key];
        if (token == null) return fallback;
        if (token.Type == JTokenType.Boolean) return token.Value<bool>();
        Log.Warning("[GliderSuit] Invalid " + key + "; using " + fallback);
        return fallback;
    }
}
