using System;
using UnityEngine;

/// <summary>
/// Lives on the local player entity. Owns input, the Jump press, activation and
/// folding. The flight step itself lives in Patch_FixedMove / GliderFlight.
/// </summary>
internal sealed class GliderController : MonoBehaviour
{
    internal static GliderController Instance;

    private EntityPlayerLocal player;
    private Vector3 lastPosition;
    private bool gravityLogged;
    private bool lastHasGlider;
    private bool lastFirstPerson;
    private float nextDebugLog;
    private bool debugReadoutFailed;
    private bool ropeWasAttached;
    // D35: this controller put the camera in third person, so folding puts it back.
    private bool switchedToThirdPerson;
    // D35: the view is decided once per glide, when the delay runs out, so a view
    // the player picks after that is never overridden.
    private bool viewDecided;
    // The "switch back waits" line is logged once per wait, not every frame.
    private bool restoreWaitLogged;

    internal static void Attach(EntityPlayerLocal entityPlayer)
    {
        if (entityPlayer == null) return;
        if (Instance != null && Instance.player != entityPlayer)
        {
            Destroy(Instance);
            Instance = null;
        }
        if (Instance == null)
        {
            Instance = entityPlayer.gameObject.AddComponent<GliderController>();
            Instance.player = entityPlayer;
        }
        GliderState.Player = entityPlayer;
        GliderState.Reset();
        GliderAudio.Reset();
        GliderWorn.Destroy();
        Instance.lastPosition = entityPlayer.transform.position + Origin.position;
    }

    internal static void Detach()
    {
        if (Instance != null)
        {
            Destroy(Instance);
            Instance = null;
        }
        GliderAudio.Reset();
        GliderWorn.Destroy();
        GliderRemote.Clear();
        Patch_Camera.Reset();
        GliderState.Reset();
        GliderState.ClimbTestOpenAt = 0f;
    }

    /// <summary>Used by "glider open"; still needs the harness, like the real thing.</summary>
    internal static void ForceOpen(EntityPlayerLocal entityPlayer)
    {
        if (Instance == null || Instance.player != entityPlayer) Attach(entityPlayer);
        if (Instance == null) return;
        if (!GliderMod.PatchesOk)
        {
            Log.Out("[GliderSuit] Open refused: a Harmony patch failed at startup (see log).");
            return;
        }
        if (!entityPlayer.Buffs.HasBuff(HarnessBuff))
        {
            Log.Out("[GliderSuit] No glider harness on the equipped chest armor.");
            return;
        }
        if (!GliderState.IsActive) Instance.Activate("command");
    }

    internal static void ForceFold(string reason)
    {
        if (Instance != null && GliderState.IsActive) Instance.Fold(reason);
    }

    /// <summary>Fold from a patch or another class, using the controller's own velocity.</summary>
    internal static void FoldActive(string reason)
    {
        if (Instance != null && GliderState.IsActive) Instance.Fold(reason);
    }

    /// <summary>Fold with the velocity measured by the FixedMove postfix.</summary>
    internal static void FoldActive(string reason, Vector3 velocity)
    {
        if (Instance != null && GliderState.IsActive) Instance.Fold(reason, velocity);
    }

    private void LateUpdate()
    {
        if (GameManager.IsDedicatedServer || player == null) return;
        try
        {
            // The pose first: it moves the bones, and the wings are aimed in world
            // space on top of where the bones end up.
            GliderPose.LateUpdate(player, GliderMod.Settings);
            GliderWorn.Tick(player, GliderMod.Settings);
            GliderRemote.LateTick(GliderMod.Settings);
        }
        catch (Exception ex)
        {
            Log.Warning("[GliderSuit] LateUpdate error: " + ex.Message);
        }
    }

    private void Update()
    {
        if (GameManager.IsDedicatedServer) return;
        if (player == null)
        {
            player = GliderState.Player;
            if (player == null) return;
        }
        try
        {
            LogGravityOnce();
            Tick();
            GliderRemote.Tick(player, GliderMod.Settings);
            TickDebugLog(GliderMod.Settings);
        }
        catch (Exception ex)
        {
            Log.Warning("[GliderSuit] Update error: " + ex.Message);
        }
    }

    /// <summary>Resolves the plan's §3.1 [VERIFY]: the controller's real gravity and fixed step.</summary>
    private void LogGravityOnce()
    {
        if (gravityLogged) return;
        vp_FPController controller = player.vp_FPController;
        if (controller == null || controller.m_GravityForce == 0f) return;

        float dt = Time.fixedDeltaTime;
        float gravity = Mathf.Abs(controller.m_GravityForce) * 60f / dt;
        Log.Out("[GliderSuit] gravity: m_GravityForce = " + controller.m_GravityForce +
            ", Time.fixedDeltaTime = " + dt +
            ", derived gravity = " + gravity + " m/s^2");
        gravityLogged = true;
    }

    private void Tick()
    {
        GliderConfig config = GliderMod.Settings;
        if (player.IsDead() || GameManager.Instance == null || GameManager.Instance.World == null)
        {
            if (GliderState.IsActive) Fold("gone");
            // The death camera and the respawn set the view themselves.
            if (player.IsDead()) switchedToThirdPerson = false;
            // The rest of the tick is skipped while dead, so fade the wind out here.
            GliderAudio.Tick(player, config);
            return;
        }

        // A crash the vanilla call did not pick up in its own step; never charged twice.
        if (GliderState.PendingImpact > 0f)
        {
            // Kept armed so the prefix treats it as the glider's own (and guards it).
            float impact = GliderState.PendingImpact;
            GliderState.ImpactArmedUntil = Time.time + 1f;
            player.FallImpact(impact);
            GliderState.PendingImpact = 0f;
            GliderState.ImpactArmedUntil = 0f;
            if (config.Debug) Log.Out("[GliderSuit] impact delivered late: " + impact.ToString("F3"));
        }

        // "glider climb": open after the short fall, then climb into the ceiling.
        if (GliderState.ClimbTestOpenAt > 0f && Time.time >= GliderState.ClimbTestOpenAt)
        {
            GliderState.ClimbTestOpenAt = 0f;
            ForceOpen(player);
            if (GliderState.IsActive)
            {
                GliderState.ClimbTestUntil = Time.time + GliderState.ClimbTestSeconds;
                Log.Out("[GliderSuit] Climb test started: ceilingY=" + GliderState.CeilingY.ToString("F1") + ".");
            }
            else Log.Out("[GliderSuit] Climb test: the wings did not open.");
        }
        if (GliderState.ClimbTestUntil > 0f && Time.time >= GliderState.ClimbTestUntil)
        {
            GliderState.ClimbTestUntil = 0f;
            Log.Out("[GliderSuit] Climb test over: " + DebugText());
        }

        // World space: Unity positions jump by hundreds of metres whenever the game
        // re-centres its floating origin, which is not a teleport.
        Vector3 position = player.transform.position + Origin.position;
        if (GliderState.IsActive && (position - lastPosition).sqrMagnitude > 50f * 50f)
        {
            lastPosition = position;
            Fold("teleport");
            return;
        }
        lastPosition = position;

        if (GliderState.IsActive)
        {
            string reason = AutoFoldReason();
            if (reason != null) Fold(reason);
        }

        // A switch back that had to wait for the player to stop aiming.
        if (!GliderState.IsActive && switchedToThirdPerson) RestoreView();

        // Keep buffGliderSuitActive present while active and through the landing grace.
        bool wantBuff = GliderState.IsActive || GliderState.InGrace;
        bool hasBuff = GliderEquip.HasLiveBuff(player, ActiveBuff);
        if (wantBuff && !hasBuff) AddBuff(ActiveBuff);
        else if (!wantBuff && hasBuff) player.Buffs.RemoveBuff(ActiveBuff, player.entityId, true);

        // Multiplayer (D47): other players' games draw the open wings from this one.
        // The owner is the instigator, so the server does not echo it back.
        bool open = GliderState.IsActive;
        bool hasOpen = GliderEquip.HasLiveBuff(player, OpenBuff);
        if (open && !hasOpen) AddBuff(OpenBuff);
        else if (!open && hasOpen) player.Buffs.RemoveBuff(OpenBuff, player.entityId, true);

        PlayerActionsLocal input = player.playerInput;
        bool airborne = !Grounded();
        // D31: the first opening after leaving the ground gets the launch kick.
        if (!airborne) GliderState.LaunchKickReady = true;

        // Jump also lets go of a grappling rope. That press belongs to the rope, or
        // letting go would open the wings at once. The rope detaches in the same
        // frame as the press, before or after this runs, so a rope seen on the
        // previous frame counts too.
        bool ropeNow = GliderRope.Attached;
        bool ropePress = ropeNow || ropeWasAttached;
        ropeWasAttached = ropeNow;

        if (input != null && input.Jump != null && input.Jump.WasPressed && ropePress)
        {
            if (config.Debug) Log.Out("[GliderSuit] Jump tap ignored: it let go of the grappling rope.");
        }
        else if (input != null && input.Jump != null && input.Jump.WasPressed)
        {
            if (GliderState.IsActive)
            {
                if (Time.time < GliderState.PhaseStartTime + config.FoldInputLockoutSeconds)
                {
                    if (config.Debug)
                        Log.Out("[GliderSuit] Jump tap ignored: fold lockout (" +
                            (GliderState.PhaseStartTime + config.FoldInputLockoutSeconds - Time.time).ToString("F2") + "s left)");
                }
                else
                {
                    Fold("jump");
                    return;
                }
            }
            else
            {
                // D31: one press in the air opens the wings. The press that starts a
                // jump is made on the ground, so it never counts.
                if (config.Debug) Log.Out("[GliderSuit] Jump tap: airborne=" + airborne);
                if (airborne) Toggle();
            }
        }

        if (GliderState.IsActive) AutoThirdPerson(config);

        // D2: Left/Right steer the heading (and therefore the camera). The flight
        // path follows the camera over a moment, in GliderFlight.Step.
        if (GliderState.IsActive)
        {
            GliderFlight.ReadInput(input, out float _, out float turn);
            GliderState.LastTurn = turn;
            if (Mathf.Abs(turn) > 0.01f && player.vp_FPCamera != null)
            {
                float factor = GliderState.Phase == GliderPhase.Stalled ? config.StallTurnFactor : 1f;
                GliderFlight.TurnLook(player, config.TurnRate * Time.deltaTime * turn * factor);
            }
        }

        GliderAudio.Tick(player, config);
        SyncWornBackstop();
    }

    /// <summary>
    /// The SDCS postfixes cover most cases; this catches installing or removing
    /// the mod and switching views, in case neither rebuilds the rig (§5.6).
    /// </summary>
    private void SyncWornBackstop()
    {
        bool hasGlider = GliderEquip.HasGlider(player);
        bool firstPerson = player.bFirstPersonView;
        bool changed = hasGlider != lastHasGlider || firstPerson != lastFirstPerson;
        lastHasGlider = hasGlider;
        lastFirstPerson = firstPerson;

        // Re-attach on a view or equipment change, and also whenever the worn set has
        // gone missing. A rig rebuild during a teleport can take the instance with it,
        // and without this nothing would ever bring it back.
        if (!changed && GliderWorn.WorldPresent) return;

        EModelSDCS model = player.emodel as EModelSDCS;
        if (model != null) GliderWorn.Sync(player, model.boneCatalog, WornKind.World);
    }

    private void Toggle()
    {
        if (Time.time < GliderState.RedeployReadyTime) return;
        // Without a glider every Jump press in the air lands here (D31); say nothing.
        if (!player.Buffs.HasBuff(HarnessBuff))
        {
            if (GliderMod.Settings.Debug) Log.Out("[GliderSuit] Open refused: no glider harness (silent)");
            return;
        }
        string blocked = ActivateBlockReason();
        if (blocked == null)
        {
            Activate("jump");
            return;
        }
        Log.Out("[GliderSuit] Open refused: " + blocked);
        GameManager.ShowTooltip(player, "Glider: " + blocked);
    }

    /// <summary>Null when opening the wings is allowed, otherwise why it is not.</summary>
    private string ActivateBlockReason()
    {
        GliderConfig config = GliderMod.Settings;
        if (!GliderMod.PatchesOk) return "a Harmony patch failed at startup (see log)";
        if (!player.Buffs.HasBuff(HarnessBuff)) return "no glider harness on the equipped chest armor";
        if (Grounded()) return "on the ground";
        if (!HasClearAirBelow(config.MinActivateHeight))
            return "too low (needs " + config.MinActivateHeight.ToString("F1") + " m of clear air below)";
        if (player.IsInWater() || player.IsSwimming()) return "in water";
        if (player.isLadderAttached) return "on a ladder";
        if (player.AttachedToEntity != null) return "in a vehicle";
        if (GliderRope.Attached) return "on a grappling rope (let go first)";
        if (player.IsFlyMode.Value || player.IsGodMode.Value) return "fly or god mode is on";
        if (player.JetpackWearing) return "wearing a jetpack";
        if (UiBlocksInput()) return "a window is open";
        if (Time.time < GliderState.RedeployReadyTime) return "reactivate cooldown";
        return null;
    }

    internal void Activate(string reason)
    {
        GliderConfig config = GliderMod.Settings;

        // Seed the flight model from the controller so the wings catch without a
        // snap (§5.2).
        Vector3 v0 = GliderFlight.CurrentVelocity(player);
        float horizontal = new Vector2(v0.x, v0.z).magnitude;
        GliderState.InitialVelocity = v0;
        GliderState.CarryVelocity = Vector3.zero;
        GliderState.Speed = v0.magnitude;
        // Start along the real direction of travel (a straight drop is a dive, not
        // level flight); GliderFlight.Step then eases the nose to the glide angle.
        GliderState.PitchDeg = v0.sqrMagnitude > 0.0001f
            ? Mathf.Clamp(Mathf.Atan2(v0.y, horizontal) * Mathf.Rad2Deg, -config.MaxDiveAngle, config.MaxClimbAngle)
            : 0f;
        GliderState.EasingToTrim = config.OpenTrimRate > 0f;
        GliderState.EaseRate = config.OpenTrimRate;
        GliderState.HeadingDeg = horizontal > 2f
            ? Mathf.Repeat(Mathf.Atan2(v0.x, v0.z) * Mathf.Rad2Deg, 360f)
            : Mathf.Repeat(GliderFlight.LookYaw(player), 360f);
        GliderState.Bank = 0f;
        GliderState.HeadingRate = 0f;
        if (GliderState.LaunchKickReady) LaunchKick(player, config);
        GliderState.LaunchKickReady = false;
        GliderFlight.LatchHeldInput(player.playerInput);
        GliderState.PendingImpact = 0f;
        GliderState.ImpactArmedUntil = 0f;

        GliderState.Phase = GliderPhase.Gliding;
        GliderState.PhaseStartTime = Time.time;
        viewDecided = false;
        GliderState.StallStartTime = 0f;
        SyncFallAnimation();
        GliderAudio.PlayUnfold(player);
        if (config.Debug)
            Log.Out("[GliderSuit] Wings open (" + reason + ") at " + v0.magnitude.ToString("F1") + " m/s, pitch " +
                GliderState.PitchDeg.ToString("F1") + " deg, heading " + GliderState.HeadingDeg.ToString("F0") + " deg.");
    }

    /// <summary>
    /// D31: the first opening after leaving the ground tops the speed up to
    /// launch_speed, on the glide angle and toward where the player is looking,
    /// so even a standing jump glides a few metres. Later openings in the same
    /// fall get nothing, so folding and reopening cannot be used to gain speed.
    /// </summary>
    private static void LaunchKick(EntityPlayerLocal player, GliderConfig config)
    {
        if (config.LaunchSpeed <= 0f || GliderState.Speed >= config.LaunchSpeed) return;
        float trimAngle = Mathf.Atan(1f / Mathf.Max(0.01f, config.GlideRatio)) * Mathf.Rad2Deg;
        float speedBefore = GliderState.Speed;
        GliderState.Speed = config.LaunchSpeed;
        GliderState.PitchDeg = -trimAngle;
        GliderState.EasingToTrim = false;
        GliderState.HeadingDeg = Mathf.Repeat(GliderFlight.LookYaw(player), 360f);
        // Seed the opening blend with the kicked velocity, or the unfold time would
        // spend the whole of a short hop on the jump's own slow motion.
        float heading = GliderState.HeadingDeg * Mathf.Deg2Rad;
        float pitch = GliderState.PitchDeg * Mathf.Deg2Rad;
        GliderState.InitialVelocity = new Vector3(Mathf.Sin(heading) * Mathf.Cos(pitch), Mathf.Sin(pitch),
            Mathf.Cos(heading) * Mathf.Cos(pitch)) * config.LaunchSpeed;
        if (config.Debug)
            Log.Out("[GliderSuit] Launch kick: " + speedBefore.ToString("F1") + " -> " +
                config.LaunchSpeed.ToString("F1") + " m/s.");
    }

    /// <summary>
    /// D35: after third_person_after_ms of gliding, switch to third person the way
    /// the game does when a player gets into a vehicle. Nothing happens if the
    /// player is already in third person or the world restricts the camera. While
    /// the player is aiming it waits until they stop.
    /// </summary>
    private void AutoThirdPerson(GliderConfig config)
    {
        if (viewDecided || !config.AutoThirdPerson) return;
        if (Time.time - GliderState.PhaseStartTime < config.ThirdPersonAfterMs / 1000f) return;
        if (ViewSwitchBlocked()) return;
        viewDecided = true;
        if (!player.bFirstPersonView) return;
        if (player.vp_FPCamera == null || player.vp_FPCamera.Locked3rdPerson) return;
        if (GameStats.GetInt(EnumGameStats.CameraRestrictionMode) != 0) return;
        player.SetFirstPersonView(false, true);
        switchedToThirdPerson = true;
        if (config.Debug) Log.Out("[GliderSuit] View: third person while gliding.");
    }

    /// <summary>
    /// Changing the view while aiming swaps the weapon between its first- and
    /// third-person models mid-zoom and leaves the zoom stuck on after the player
    /// lets go. The game avoids it too: its view key does nothing while an item
    /// action runs, and a vehicle clears AimingGun before its camera takes over.
    /// The zoom action only counts as running for its first 0.3 s, so AimingGun is
    /// checked as well.
    /// </summary>
    private bool ViewSwitchBlocked()
    {
        if (player.AimingGun) return true;
        return player.inventory != null && player.inventory.IsHoldingItemActionRunning();
    }

    /// <summary>
    /// D35: back to the view the player last chose with the view key (the game's
    /// own preference, as when leaving a vehicle). Not while dead: the death
    /// camera and the respawn set the view themselves. While the player is aiming
    /// it waits: Tick calls this again every frame until the switch happens.
    /// </summary>
    private void RestoreView()
    {
        if (!switchedToThirdPerson) return;
        if (player.IsDead())
        {
            switchedToThirdPerson = false;
            return;
        }
        if (ViewSwitchBlocked())
        {
            if (GliderMod.Settings.Debug && !restoreWaitLogged)
                Log.Out("[GliderSuit] View: switch back waits until you stop aiming.");
            restoreWaitLogged = true;
            return;
        }
        switchedToThirdPerson = false;
        restoreWaitLogged = false;
        player.SwitchToPreferredCameraMode(true);
        if (GliderMod.Settings.Debug)
            Log.Out("[GliderSuit] View: back to " + (player.bFirstPersonView ? "first" : "third") + " person.");
    }

    internal void Fold(string reason)
    {
        Fold(reason, GliderFlight.CurrentVelocity(player));
    }

    internal void Fold(string reason, Vector3 velocity)
    {
        if (!GliderState.IsActive) return;
        GliderConfig config = GliderMod.Settings;

        GliderState.Phase = GliderPhase.Folded;
        GliderState.LastFoldReason = reason;
        GliderState.RedeployReadyTime = Time.time + config.ReactivateCooldownSeconds;
        GliderState.Bank = 0f;
        GliderState.HeadingRate = 0f;

        // A landing keeps the impact the postfix just armed; every other fold
        // hands the motion back so vanilla falling resumes (§5.2).
        // A rope takes the movement over itself, so nothing is handed back either.
        if (reason != "landed" && reason != "rope") GliderFlight.HandBack(player, velocity);

        SyncFallAnimation();
        RestoreView();
        GliderAudio.PlayFold(player);
        if (config.Debug)
            Log.Out("[GliderSuit] Wings folded (" + reason + ") at " + velocity.magnitude.ToString("F1") +
                " m/s, " + DebugText());
    }

    /// <summary>
    /// The game only sends "in the air" to the animator when it changes, so push
    /// it again whenever the wings open or close. Patch_AvatarAnimation turns it
    /// into "not falling" while the wings are active.
    /// </summary>
    private void SyncFallAnimation()
    {
        if (player.emodel != null && player.emodel.avatarController != null)
            player.emodel.avatarController.SetInAir(player.inAir);
    }

    private string AutoFoldReason()
    {
        if (!player.Buffs.HasBuff(HarnessBuff)) return "mod removed";
        if (player.IsDead()) return "death";
        if (player.IsInWater() || player.IsSwimming()) return "water";
        if (player.isLadderAttached) return "ladder";
        if (player.AttachedToEntity != null) return "attached";
        if (GliderRope.Attached) return "rope";
        if (player.IsFlyMode.Value || player.IsGodMode.Value) return "fly/god";
        return null;
    }

    private void AddBuff(string name)
    {
        EntityBuffs.BuffStatus status = player.Buffs.AddBuff(name, player.entityId, true);
        if (status != EntityBuffs.BuffStatus.Added)
            Log.Warning("[GliderSuit] Could not add " + name + ": " + status);
    }

    private bool Grounded()
    {
        if (player.onGround) return true;
        return player.vp_FPController != null && player.vp_FPController.Grounded;
    }

    private bool HasClearAirBelow(float meters)
    {
        if (meters <= 0f) return true;
        Vector3 origin = player.transform.position + Vector3.up * 0.1f;
        return !Physics.Raycast(origin, Vector3.down, out RaycastHit _, meters, GroundMask);
    }

    private bool UiBlocksInput()
    {
        try
        {
            LocalPlayerUI ui = player.PlayerUI;
            GUIWindowManager windows = ui != null ? ui.windowManager : null;
            if (windows != null && (windows.IsModalWindowOpen() || windows.IsCursorWindowOpen() || windows.IsInputActive()))
                return true;
            return GameManager.Instance != null && GameManager.Instance.isAnyCursorWindowOpen(ui);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private void OnGUI()
    {
        if (!GliderMod.Settings.Debug || GliderState.Player != player) return;
        try
        {
            GUI.Label(new Rect(10f, 10f, 1100f, 24f), "[GliderSuit] " + DebugText());
        }
        catch (Exception ex)
        {
            // Swallowing this silently is why a blank readout had no explanation.
            if (!debugReadoutFailed)
            {
                debugReadoutFailed = true;
                Log.Warning("[GliderSuit] on-screen readout failed, use the log instead: " + ex);
            }
        }
    }

    /// <summary>State, speed, attitude and heights, for the readout and the debug log.</summary>
    private string DebugText()
    {
        float ground = 0f;
        if (player != null && Physics.Raycast(player.transform.position + Vector3.up * 0.1f, Vector3.down,
                out RaycastHit hit, 500f, GroundMask))
            ground = hit.distance;
        float belowCeiling = 0f;
        if (player != null && player.vp_FPController != null)
            belowCeiling = GliderState.CeilingY - GliderFlight.WorldY(player.vp_FPController);
        return GliderState.Phase +
            " V " + GliderState.Speed.ToString("F1") + " m/s" +
            " pitch " + GliderState.PitchDeg.ToString("F1") + " deg" +
            " sink " + (-Mathf.Sin(GliderState.PitchDeg * Mathf.Deg2Rad) * GliderState.Speed).ToString("F1") + " m/s" +
            " heading " + GliderState.HeadingDeg.ToString("F0") +
            " bank " + GliderState.Bank.ToString("F2") +
            " ground " + ground.ToString("F1") + " m" +
            " below ceiling " + belowCeiling.ToString("F1") + " m" +
            " wind " + GliderAudio.TargetVolume.ToString("F2") +
            " lastFold " + GliderState.LastFoldReason;
    }

    /// <summary>
    /// Twice a second while gliding, so every number the readout would show also
    /// lands in output_log.txt. Jason reads the log, not the screen.
    /// </summary>
    private void TickDebugLog(GliderConfig config)
    {
        if (GliderState.ImpactLogAt > 0f && Time.time >= GliderState.ImpactLogAt)
        {
            GliderState.ImpactLogAt = 0f;
            Log.Out("[GliderSuit] after impact: health " + GliderState.ImpactLogHealth + " -> " + player.Health +
                " (-" + (GliderState.ImpactLogHealth - player.Health) + ")" +
                " _fallSpeed=" + player.GetCVar("_fallSpeed").ToString("F3") +
                " legSprained=" + player.Buffs.HasBuff("buffLegSprained") +
                " legBroken=" + player.Buffs.HasBuff("buffLegBroken") +
                " dead=" + player.IsDead());
        }
        if (!config.Debug || !GliderState.IsActive || Time.time < nextDebugLog) return;
        nextDebugLog = Time.time + 0.5f;
        Log.Out("[GliderSuit] " + DebugText());
    }

    internal const string HarnessBuff = "buffGliderSuitHarness";
    internal const string ActiveBuff = "buffGliderSuitActive";
    internal const string OpenBuff = "buffGliderSuitOpen";

    /// <summary>The same layer mask vp_FPController uses for its ground sphere cast.</summary>
    private const int GroundMask = 1084850184;
}
