using UnityEngine;

internal enum GliderPhase
{
    Folded,
    Gliding,
    Stalled
}

/// <summary>
/// The single source of truth for the local player's glider. v1 is
/// single-player/host only, so one static state is enough; Phase 10 gives each
/// remote player their own visual driven from replicated state.
/// </summary>
internal static class GliderState
{
    internal static GliderPhase Phase = GliderPhase.Folded;
    internal static EntityPlayerLocal Player;

    /// <summary>When the wings opened; drives the lift ramp and the fold lockout.</summary>
    internal static float PhaseStartTime;
    internal static float RedeployReadyTime;

    /// <summary>The highest point since the player last stood on the ground (R9).</summary>
    internal static float CeilingY;

    // Flight model state (§5.3).
    internal static float Speed;
    internal static float PitchDeg;
    internal static float HeadingDeg;
    internal static float Bank;
    internal static float StallStartTime;

    /// <summary>True from opening until the nose reaches the glide angle or the player takes over.</summary>
    internal static bool EasingToTrim;

    /// <summary>How fast that easing moves the nose, deg/s: gentle after opening, quick after a stall.</summary>
    internal static float EaseRate;

    /// <summary>Test cheat ("glider climb"): until this time the nose is held fully up at ClimbTestSpeed.</summary>
    internal static float ClimbTestUntil;
    internal const float ClimbTestSpeed = 40f;
    internal const float ClimbTestSeconds = 10f;
    internal static float ClimbTestOpenAt;

    /// <summary>Velocity (m/s) the wings were opened with; the lift ramp blends from it (§5.2).</summary>
    internal static Vector3 InitialVelocity;
    internal static float LastTurn;

    /// <summary>Set by the FixedMove prefix while it ran the flight step, read by the postfix.</summary>
    internal static bool StepActive;
    internal static Vector3 StepPosBefore;
    internal static Vector3 StepIntended;

    /// <summary>0 = fully folded, 1 = fully extended. Visual only (§5.6).</summary>
    internal static float FoldFraction;

    /// <summary>A crash impact waiting to be handed to vanilla FallImpact.</summary>
    internal static float PendingImpact;
    internal static float ImpactArmedUntil;

    /// <summary>The crash just delivered, and until when a smaller vanilla impact is its echo.</summary>
    internal static float ImpactGuardValue;
    internal static float ImpactGuardUntil;

    internal static string LastFoldReason = "";

    /// <summary>Debug only: when to log the health left after an impact, and the health before it.</summary>
    internal static float ImpactLogAt;
    internal static int ImpactLogHealth;

    /// <summary>
    /// D31: set whenever the player is on the ground, used up by the next opening.
    /// Not cleared by Reset: a teleport (glider up) fires PlayerSpawnedInWorld.
    /// </summary>
    internal static bool LaunchKickReady = true;

    /// <summary>D32: directions held when the wings opened, ignored until let go.</summary>
    internal static bool HeldForward, HeldBack, HeldLeft, HeldRight;

    internal static bool IsActive => Phase == GliderPhase.Gliding || Phase == GliderPhase.Stalled;
    internal static bool InGrace => Time.time < ImpactArmedUntil;

    internal static void Reset()
    {
        Phase = GliderPhase.Folded;
        PhaseStartTime = 0f;
        RedeployReadyTime = 0f;
        CeilingY = 0f;
        Speed = 0f;
        PitchDeg = 0f;
        HeadingDeg = 0f;
        Bank = 0f;
        StallStartTime = 0f;
        EasingToTrim = false;
        ClimbTestUntil = 0f;
        // ClimbTestOpenAt is deliberately not cleared here: the teleport that starts
        // the climb test fires PlayerSpawnedInWorld, which resets this state. It is
        // cleared when it fires and when the world shuts down.
        InitialVelocity = Vector3.zero;
        LastTurn = 0f;
        StepActive = false;
        StepPosBefore = Vector3.zero;
        StepIntended = Vector3.zero;
        FoldFraction = 0f;
        PendingImpact = 0f;
        ImpactArmedUntil = 0f;
        ImpactLogAt = 0f;
        LastFoldReason = "";
        HeldForward = HeldBack = HeldLeft = HeldRight = false;
    }
}
