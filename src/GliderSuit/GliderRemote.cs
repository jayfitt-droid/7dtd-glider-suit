using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Other players' gliders, on this client (D47). Nothing new is sent over the
/// network: the owner's game keeps buffGliderSuitOpen exactly while the wings are
/// open, and the game passes buffs to every client near that player (at once on
/// add and remove, and again in its regular buff sync, which also covers players
/// who join or come into range later). The folded wings need nothing at all: the
/// chest armor and its mods are replicated, and Patch_SDCS hangs the rig on any
/// player who wears the glider. This class opens and folds that rig, poses the
/// body, and plays the wind, with the bank and pitch estimated from how the
/// player moves. A custom NetPackage was avoided on purpose: a client that does
/// not know a package type the server uses is kicked.
///
/// Ticked by the local player's GliderController, so it never runs on a
/// dedicated server.
/// </summary>
internal static class GliderRemote
{
    /// <summary>How often the worn-wings backstop runs for other players, in seconds.</summary>
    private const float WornSyncInterval = 0.5f;
    /// <summary>Time constants, seconds, of the filters on the estimated motion.</summary>
    private const float VelocitySmoothing = 0.25f;
    private const float TurnSmoothing = 0.15f;
    /// <summary>Below this horizontal speed, m/s, the heading is read from the body instead.</summary>
    private const float MinHeadingSpeed = 2f;

    private sealed class Remote
    {
        internal EntityPlayer Player;
        internal int EntityId;
        internal bool Open;
        internal float Fold;
        internal bool Primed;
        internal Vector3 LastPosition;
        internal float LastHeading;
        internal Vector3 Velocity;
        internal float HeadingRate;
        internal float Bank;
        internal float Pitch;
        internal float Speed;
        internal AudioSource Wind;
    }

    private static readonly Dictionary<int, Remote> remotes = new Dictionary<int, Remote>();
    private static readonly HashSet<int> seen = new HashSet<int>();
    private static readonly List<int> gone = new List<int>();
    private static float nextWornSync;

    /// <summary>Per frame (Update): notice other players opening and folding, and estimate their flight.</summary>
    internal static void Tick(EntityPlayerLocal local, GliderConfig config)
    {
        World world = GameManager.Instance != null ? GameManager.Instance.World : null;
        if (world == null || world.Players == null) return;

        bool wornSync = Time.time >= nextWornSync;
        if (wornSync) nextWornSync = Time.time + WornSyncInterval;

        seen.Clear();
        List<EntityPlayer> players = world.Players.list;
        for (int i = 0; i < players.Count; i++)
        {
            EntityPlayer player = players[i];
            if (player == null || player == local || player is EntityPlayerLocal) continue;
            try
            {
                if (wornSync) GliderWorn.SyncRemote(player);
                TickPlayer(player, config);
                seen.Add(player.entityId);
            }
            catch (Exception ex)
            {
                if (config.Debug) Log.Warning("[GliderSuit] Remote player " + player.entityId + ": " + ex.Message);
            }
        }

        // Players who left, died or went out of range take their wind with them.
        gone.Clear();
        foreach (KeyValuePair<int, Remote> pair in remotes)
            if (!seen.Contains(pair.Key)) gone.Add(pair.Key);
        foreach (int id in gone)
        {
            StopWind(remotes[id]);
            remotes.Remove(id);
        }
    }

    private static void TickPlayer(EntityPlayer player, GliderConfig config)
    {
        bool open = !player.IsDead() && player.AttachedToEntity == null &&
            GliderEquip.HasLiveBuff(player, GliderController.OpenBuff);
        remotes.TryGetValue(player.entityId, out Remote remote);
        if (remote == null)
        {
            if (!open) return;
            remote = new Remote { Player = player, EntityId = player.entityId };
            remotes[player.entityId] = remote;
        }

        if (open != remote.Open)
        {
            remote.Open = open;
            if (open) remote.Primed = false;
            if (config.Debug)
                Log.Out("[GliderSuit] " + player.EntityName + "'s wings " + (open ? "opened." : "folded."));
        }

        float rate = open
            ? 1f / Mathf.Max(0.05f, config.UnfoldTime)
            : 1f / Mathf.Max(0.05f, config.FoldTime);
        remote.Fold = Mathf.MoveTowards(remote.Fold, open ? 1f : 0f, rate * Time.deltaTime);

        Estimate(remote, config);
        TickWind(remote, config);
    }

    /// <summary>
    /// The owner's bank follows how fast the flight path turns, and the pitch is the
    /// flight path's angle, so both are read from the player's smoothed movement,
    /// judged against this client's own heading_follow_rate.
    /// </summary>
    private static void Estimate(Remote remote, GliderConfig config)
    {
        float deltaTime = Time.deltaTime;
        if (deltaTime <= 0f) return;
        Transform body = remote.Player.transform;
        // World positions: the game re-centres its floating origin, which moves
        // every transform at once.
        Vector3 position = body.position + Origin.position;
        if (!remote.Primed)
        {
            remote.Primed = true;
            remote.LastPosition = position;
            remote.LastHeading = body.eulerAngles.y;
            remote.Velocity = Vector3.zero;
            remote.HeadingRate = 0f;
            return;
        }

        Vector3 moved = position - remote.LastPosition;
        remote.LastPosition = position;
        // A teleport is not movement.
        if (moved.sqrMagnitude > 50f * 50f) return;

        remote.Velocity = Vector3.Lerp(remote.Velocity, moved / deltaTime,
            1f - Mathf.Exp(-deltaTime / VelocitySmoothing));
        Vector3 flat = new Vector3(remote.Velocity.x, 0f, remote.Velocity.z);
        float horizontal = flat.magnitude;
        remote.Speed = remote.Velocity.magnitude;

        float heading = horizontal >= MinHeadingSpeed
            ? Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg
            : body.eulerAngles.y;
        float turned = Mathf.DeltaAngle(remote.LastHeading, heading);
        remote.LastHeading = heading;
        remote.HeadingRate = Mathf.Lerp(remote.HeadingRate, turned / deltaTime,
            1f - Mathf.Exp(-deltaTime / TurnSmoothing));

        float bankTarget = remote.Open
            ? Mathf.Clamp(remote.HeadingRate / Mathf.Max(1f, config.HeadingFollowRate), -1f, 1f)
            : 0f;
        remote.Bank = Mathf.MoveTowards(remote.Bank, bankTarget, 4f * deltaTime);
        float pitch = horizontal > 0.1f || Mathf.Abs(remote.Velocity.y) > 0.1f
            ? Mathf.Atan2(remote.Velocity.y, horizontal) * Mathf.Rad2Deg
            : 0f;
        remote.Pitch = Mathf.Clamp(pitch, -config.MaxDiveAngle, config.MaxClimbAngle);
    }

    /// <summary>
    /// Per frame (LateUpdate): after the animator has posed the bodies, tip and bank
    /// each flying body, then open and aim its wings, in that order, as for the
    /// local player.
    /// </summary>
    internal static void LateTick(GliderConfig config)
    {
        gone.Clear();
        foreach (Remote remote in remotes.Values)
        {
            EntityPlayer player = remote.Player;
            if (player == null) continue;
            try
            {
                GliderWings wings = GliderWorn.FindWorld(player);
                float fold = remote.Fold;
                GliderPose.Apply(player, config, fold, remote.Pitch, remote.Bank);
                if (wings == null) continue;
                bool stalled = remote.Open && remote.Speed < config.StallSpeed;
                wings.SetFold(fold, remote.Open);
                wings.Tick(config, remote.Speed, stalled, fold, 0f);
                wings.AimWings(GliderWorn.FlightRotation(player.transform, config, remote.Pitch, remote.Bank), fold);
            }
            catch (Exception ex)
            {
                if (config.Debug) Log.Warning("[GliderSuit] Remote wings " + remote.EntityId + ": " + ex.Message);
            }
            // Folded and quiet, with the folded pose applied above: nothing left to drive.
            if (!remote.Open && remote.Fold <= 0f && remote.Wind == null) gone.Add(remote.EntityId);
        }
        foreach (int id in gone) remotes.Remove(id);
    }

    /// <summary>How far open this client shows another player's wings; 0 if it does not.</summary>
    internal static float FoldOf(int entityId)
    {
        return remotes.TryGetValue(entityId, out Remote remote) ? remote.Fold : 0f;
    }

    /// <summary>True while this game shows that player gliding (Patch_RemoteAvatarAnimation).</summary>
    internal static bool IsOpen(int entityId)
    {
        return remotes.TryGetValue(entityId, out Remote remote) && remote.Open;
    }

    /// <summary>
    /// The wind loop at the other player, heard in 3D: louder with their speed, as
    /// the owner hears it, with the dive and climb shading left out.
    /// </summary>
    private static void TickWind(Remote remote, GliderConfig config)
    {
        bool wanted = config.WindLoop && remote.Open;
        if (remote.Wind == null)
        {
            if (!wanted) return;
            AudioClip clip = GliderAssets.WindClip;
            if (clip == null) return;
            var holder = new GameObject("GliderSuitRemoteWind");
            holder.transform.SetParent(remote.Player.transform, false);
            AudioSource source = holder.AddComponent<AudioSource>();
            source.clip = clip;
            source.loop = true;
            source.playOnAwake = false;
            source.spatialBlend = 1f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = 4f;
            source.maxDistance = 60f;
            source.dopplerLevel = 0f;
            source.volume = 0f;
            remote.Wind = source;
        }

        float level = Mathf.Clamp01(remote.Speed / Mathf.Max(0.01f, config.CruiseSpeed));
        float target = wanted ? Mathf.Clamp01(config.WindVolume * level * level) : 0f;
        AudioSource wind = remote.Wind;
        wind.volume = Mathf.MoveTowards(wind.volume, target, Time.deltaTime / 0.3f);
        wind.pitch = Mathf.Lerp(0.8f, 1.5f, Mathf.Clamp01(remote.Speed / 30f));
        if (wanted && !wind.isPlaying && target > 0.001f) wind.Play();
        else if (!wanted && wind.volume <= 0.001f) StopWind(remote);
    }

    private static void StopWind(Remote remote)
    {
        if (remote.Wind == null) return;
        UnityEngine.Object.Destroy(remote.Wind.gameObject);
        remote.Wind = null;
    }

    /// <summary>For "glider status": which other players this client shows gliding.</summary>
    internal static string Describe()
    {
        var open = new List<string>();
        foreach (Remote remote in remotes.Values)
        {
            if (!remote.Open) continue;
            string name = remote.Player != null ? remote.Player.EntityName : "?";
            bool rig = remote.Player != null && GliderWorn.FindWorld(remote.Player) != null;
            open.Add(name + " " + remote.Speed.ToString("F0") + " m/s" + (rig ? "" : " (no wings model)"));
        }
        return open.Count == 0 ? "none" : string.Join(", ", open.ToArray());
    }

    /// <summary>Removes every remote sound and forgets every remote player (leaving the world).</summary>
    internal static void Clear()
    {
        foreach (Remote remote in remotes.Values) StopWind(remote);
        remotes.Clear();
        seen.Clear();
        gone.Clear();
        nextWornSync = 0f;
    }
}
