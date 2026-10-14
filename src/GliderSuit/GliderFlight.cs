using UnityEngine;

/// <summary>
/// Pure flight math, kept apart from the game types so it is easy to reason
/// about. All speeds are in meters per second, all times in seconds and all
/// angles in degrees (Unity measures rotations in degrees).
/// </summary>
internal static class GliderFlight
{
    /// <summary>
    /// vp_FPController.Delta is Time.deltaTime * 60, so a per-step velocity
    /// value of v moves the player v * 60 meters per second. Never change this
    /// without re-checking FixedMove in the decompiled source (§3.1).
    /// </summary>
    internal const float VpStepsPerSecondFactor = 60f;

    /// <summary>The vanilla fall-impact factor (§3.2).</summary>
    internal const float VanillaImpactFactor = 0.86775f;

    /// <summary>
    /// Reading the controller's gravity back gives the acceleration the player
    /// was actually falling under when they opened the wings. Using it keeps
    /// the energy bookkeeping honest, which is what makes R9 hold.
    /// </summary>
    internal static float Gravity(GliderConfig config, vp_FPController controller, float deltaTime)
    {
        if (config.GlideGravity > 0f) return config.GlideGravity;
        if (controller != null && controller.m_GravityForce != 0f && deltaTime > 0f)
            return Mathf.Abs(controller.m_GravityForce) * VpStepsPerSecondFactor / deltaTime;
        return Mathf.Abs(Physics.gravity.y);
    }

    /// <summary>
    /// The takeoff ceiling (R9): the highest point since the player last stood
    /// on the ground. Runs every fixed step, active or not, so folding and
    /// reopening cannot reset it.
    /// </summary>
    internal static void UpdateCeiling(vp_FPController controller)
    {
        if (controller == null) return;
        float y = WorldY(controller);
        if (controller.m_Grounded) GliderState.CeilingY = y;
        else if (!GliderState.IsActive) GliderState.CeilingY = Mathf.Max(GliderState.CeilingY, y);
    }

    internal static float WorldY(vp_FPController controller)
    {
        return controller.Transform.position.y + Origin.position.y;
    }

    /// <summary>The player's current velocity in m/s, from the controller.</summary>
    internal static Vector3 CurrentVelocity(EntityPlayerLocal player)
    {
        vp_FPController controller = player != null ? player.vp_FPController : null;
        if (controller == null) return Vector3.zero;
        Vector3 horizontal = controller.CharacterController != null
            ? controller.CharacterController.velocity : Vector3.zero;
        float timeScale = Mathf.Max(0.0001f, Time.timeScale);
        return new Vector3(horizontal.x, controller.m_FallSpeed * VpStepsPerSecondFactor * timeScale, horizontal.z);
    }

    /// <summary>
    /// The compass direction the player is looking, in degrees. First person keeps
    /// it in the camera's Yaw. Third person does not: there the camera orbits the
    /// character on yaw3P, which is the direction from the character back to the
    /// camera, half a turn round from the view (vp_FPCamera sets Yaw = yaw3P - 180
    /// when it switches back). Reading Yaw in third person made the glider ignore
    /// the mouse and the Left/Right keys turn the flight without turning the view.
    /// </summary>
    internal static float LookYaw(EntityPlayerLocal player)
    {
        vp_FPCamera camera = player != null ? player.vp_FPCamera : null;
        if (camera == null) return GliderState.HeadingDeg;
        return player.bFirstPersonView ? camera.Yaw : camera.yaw3P - 180f;
    }

    /// <summary>Turns the view by this many degrees, in whichever of the two the current view uses.</summary>
    internal static void TurnLook(EntityPlayerLocal player, float degrees)
    {
        vp_FPCamera camera = player != null ? player.vp_FPCamera : null;
        if (camera == null) return;
        if (player.bFirstPersonView) camera.Yaw += degrees;
        else camera.yaw3P += degrees;
    }

    /// <summary>Reads the movement actions; analog sticks and the D-pad both work.</summary>
    internal static void ReadInput(PlayerActionsLocal input, out float pitchInput, out float turnInput)
    {
        pitchInput = 0f;
        turnInput = 0f;
        if (input == null) return;

        RawMove(input, out float x, out float y);

        // D32: a direction already held when the wings opened (Forward from a
        // running jump, say) does nothing until it is let go.
        if (GliderState.HeldForward) { if (y > 0.3f) y = 0f; else GliderState.HeldForward = false; }
        if (GliderState.HeldBack) { if (y < -0.3f) y = 0f; else GliderState.HeldBack = false; }
        if (GliderState.HeldRight) { if (x > 0.3f) x = 0f; else GliderState.HeldRight = false; }
        if (GliderState.HeldLeft) { if (x < -0.3f) x = 0f; else GliderState.HeldLeft = false; }

        if (y > 0.3f) pitchInput = 1f;          // Forward lowers the nose
        else if (y < -0.3f) pitchInput = -1f;   // Back raises it
        if (Mathf.Abs(x) < 0.3f) x = 0f;
        turnInput = Mathf.Clamp(x, -1f, 1f);
    }

    /// <summary>D32: notes which directions are held at the moment the wings open.</summary>
    internal static void LatchHeldInput(PlayerActionsLocal input)
    {
        float x = 0f, y = 0f;
        if (input != null) RawMove(input, out x, out y);
        GliderState.HeldForward = y > 0.3f;
        GliderState.HeldBack = y < -0.3f;
        GliderState.HeldRight = x > 0.3f;
        GliderState.HeldLeft = x < -0.3f;
    }

    private static void RawMove(PlayerActionsLocal input, out float x, out float y)
    {
        y = input.Move != null ? input.Move.Value.y : 0f;
        x = input.Move != null ? input.Move.Value.x : 0f;
        if (input.MoveForward != null && input.MoveForward.IsPressed) y = Mathf.Max(y, 1f);
        if (input.MoveBack != null && input.MoveBack.IsPressed) y = Mathf.Min(y, -1f);
        if (input.MoveRight != null && input.MoveRight.IsPressed) x = Mathf.Max(x, 1f);
        if (input.MoveLeft != null && input.MoveLeft.IsPressed) x = Mathf.Min(x, -1f);
    }

    /// <summary>
    /// One fixed step of the point-mass glider (§5.3). Returns the velocity
    /// (m/s) the controller should be given for this step.
    /// </summary>
    internal static Vector3 Step(GliderConfig config, vp_FPController controller,
        EntityPlayerLocal player, float deltaTime)
    {
        float g = Gravity(config, controller, deltaTime);
        float trimAngle = Mathf.Atan(1f / Mathf.Max(0.01f, config.GlideRatio)) * Mathf.Rad2Deg;
        float dragK = g * Mathf.Sin(trimAngle * Mathf.Deg2Rad) /
            Mathf.Max(0.01f, config.CruiseSpeed * config.CruiseSpeed);

        ReadInput(player != null ? player.playerInput : null, out float pitchInput, out float turnInput);
        GliderState.LastTurn = turnInput;

        // 2. Stall.
        bool stalled = GliderState.Phase == GliderPhase.Stalled;
        if (!stalled && GliderState.Speed < config.StallSpeed)
        {
            stalled = true;
            GliderState.Phase = GliderPhase.Stalled;
            GliderState.StallStartTime = Time.time;
            if (config.Debug) Log.Out("[GliderSuit] Stall at " + GliderState.Speed.ToString("F1") + " m/s.");
        }
        else if (stalled && GliderState.Speed >= config.StallRecoverSpeed &&
                 Time.time - GliderState.StallStartTime >= config.StallMinTime)
        {
            stalled = false;
            GliderState.Phase = GliderPhase.Gliding;
            // A stall leaves the nose at the stall dive angle. With the pitch holding
            // where it is left, a hands-off recovery was a dive into the ground, so
            // pull out to the glide angle unless the player takes the nose.
            GliderState.EasingToTrim = config.OpenTrimRate > 0f;
            GliderState.EaseRate = config.StallPitchRate;
            if (config.Debug) Log.Out("[GliderSuit] Stall recovered at " + GliderState.Speed.ToString("F1") + " m/s.");
        }

        // 3. Pitch command. Forward lowers the nose; a stall overrides the input.
        float pitch = GliderState.PitchDeg;
        if (stalled)
        {
            GliderState.EasingToTrim = false;
            pitch = Mathf.MoveTowards(pitch, -config.StallDiveAngle, config.StallPitchRate * deltaTime);
        }
        else
        {
            // After opening, and after a stall, the nose eases to the glide angle by
            // itself, so hands off always ends in a trimmed glide. Input against the
            // ease hands the nose to the player at once. Input the same way as the
            // ease (Back while it pulls out of a dive) does not cancel it: the ease is
            // the faster of the two, and cancelling it made pulling back the slowest
            // way out of a stall. The player takes over when the glide angle is reached.
            bool eased = false;
            if (GliderState.EasingToTrim)
            {
                float easeDirection = Mathf.Sign(-trimAngle - pitch);   // +1 = nose coming up
                float inputDirection = -pitchInput;                      // Back raises the nose
                if (pitchInput != 0f && inputDirection != easeDirection)
                {
                    GliderState.EasingToTrim = false;
                }
                else
                {
                    float rate = pitchInput != 0f
                        ? Mathf.Max(GliderState.EaseRate, config.PitchRate)
                        : GliderState.EaseRate;
                    pitch = Mathf.MoveTowards(pitch, -trimAngle, rate * deltaTime);
                    if (Mathf.Approximately(pitch, -trimAngle)) GliderState.EasingToTrim = false;
                    eased = true;
                }
            }
            if (!eased) pitch += -pitchInput * config.PitchRate * deltaTime;
            if (config.PitchReturnRate > 0f)
                pitch = Mathf.MoveTowards(pitch, -trimAngle, config.PitchReturnRate * deltaTime);
            pitch = Mathf.Clamp(pitch, -config.MaxDiveAngle, config.MaxClimbAngle);
        }

        // Test cheat: unlimited energy and full back-stick, so the only thing
        // left to stop the climb is the ceiling below.
        if (Time.time < GliderState.ClimbTestUntil)
        {
            GliderState.EasingToTrim = false;
            GliderState.Speed = GliderState.ClimbTestSpeed;
            pitch = config.MaxClimbAngle;
        }

        // 4. Ceiling (R9): flatten a climb smoothly into the takeoff height.
        float y = controller != null ? WorldY(controller) : 0f;
        float room = GliderState.CeilingY - config.CeilingMargin - y;
        if (room <= 0f) pitch = Mathf.Min(pitch, 0f);
        else if (pitch > 0f)
            pitch = Mathf.Min(pitch, config.MaxClimbAngle * Mathf.Clamp01(room / config.CeilingSoftenHeight));

        // 5. Heading follows the camera (D2), limited so a mouse flick cannot
        //    reverse the flight path instantly.
        float wanted = LookYaw(player);
        float heading = GliderState.HeadingDeg;
        float applied = Mathf.Clamp(Mathf.DeltaAngle(heading, wanted),
            -config.HeadingFollowRate * deltaTime, config.HeadingFollowRate * deltaTime);
        heading = Mathf.Repeat(heading + applied, 360f);
        float headingRate = deltaTime > 0f ? applied / deltaTime : 0f;
        float bankTarget = Mathf.Clamp(headingRate / Mathf.Max(1f, config.HeadingFollowRate), -1f, 1f);
        GliderState.Bank = Mathf.MoveTowards(GliderState.Bank, bankTarget, 4f * deltaTime);

        // 6. Speed: only losing height buys speed, drag always takes some.
        float sinPitch = Mathf.Sin(pitch * Mathf.Deg2Rad);
        float speed = GliderState.Speed;
        speed += (-g * sinPitch -
                  dragK * speed * speed * (1f + config.TurnDrag * Mathf.Abs(GliderState.Bank))) * deltaTime;
        speed = Mathf.Max(0f, speed);

        // 7. Velocity out.
        float psi = heading * Mathf.Deg2Rad;
        float gamma = pitch * Mathf.Deg2Rad;
        Vector3 flat = new Vector3(Mathf.Sin(psi), 0f, Mathf.Cos(psi));
        Vector3 velocity = flat * (speed * Mathf.Cos(gamma)) + Vector3.up * (speed * Mathf.Sin(gamma));
        if (y + velocity.y * deltaTime > GliderState.CeilingY) velocity.y = 0f;

        // 7. Lift ramp: from whatever the player arrived with to the model's
        //    velocity, eased, so the wings catch instead of snapping.
        float elapsed = Time.time - GliderState.PhaseStartTime;
        if (config.UnfoldTime > 0f && elapsed < config.UnfoldTime)
        {
            Vector3 seeded = GliderState.InitialVelocity + Vector3.down * (g * elapsed);
            velocity = Vector3.Lerp(seeded, velocity, EaseOut(elapsed / config.UnfoldTime));
        }

        GliderState.Speed = speed;
        GliderState.PitchDeg = pitch;
        GliderState.HeadingDeg = heading;
        return velocity;
    }

    /// <summary>
    /// The vanilla fall-damage value for hitting something at v m/s. Treating the
    /// crash as the fall from the height that gives the same speed keeps every
    /// vanilla modifier (parkour, Impact Bracing, boots) working (§3.2).
    /// </summary>
    internal static float Impact(float speed)
    {
        if (speed <= 0f) return 0f;
        return VanillaImpactFactor * speed * Mathf.Sqrt(Time.fixedDeltaTime / VpStepsPerSecondFactor);
    }

    /// <summary>
    /// Hands the motion back to the vanilla controller when the wings fold in
    /// the air, so ordinary falling (and ordinary fall damage) resume from that
    /// exact moment (§5.2).
    /// </summary>
    internal static void HandBack(EntityPlayerLocal player, Vector3 velocity)
    {
        vp_FPController controller = player != null ? player.vp_FPController : null;
        if (controller == null) return;
        float timeScale = Mathf.Max(0.0001f, Time.timeScale);
        float deltaTime = Time.fixedDeltaTime;

        // m_MotorThrottle x/z are left as last written; vanilla damping bleeds them off.
        controller.m_FallSpeed = velocity.y / (VpStepsPerSecondFactor * timeScale);
        controller.m_MaxHeight = WorldY(controller);
        controller.m_MaxHeightInitialFallSpeed =
            -Mathf.Max(0f, -velocity.y) * Mathf.Sqrt(deltaTime / VpStepsPerSecondFactor);
    }

    internal static float EaseOut(float t)
    {
        t = Mathf.Clamp01(t);
        return 1f - (1f - t) * (1f - t);
    }

    internal static float BackOut(float t)
    {
        const float overshoot = 1.7f;
        t -= 1f;
        return t * t * ((overshoot + 1f) * t + overshoot) + 1f;
    }
}
