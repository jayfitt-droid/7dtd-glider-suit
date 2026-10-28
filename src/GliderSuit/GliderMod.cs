using System;
using System.IO;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

public sealed class ModApi : IModApi
{
    public void InitMod(Mod mod)
    {
        // Each step is isolated so one failure cannot silently take the rest of
        // the mod down with it. Events come first; patches last.
        try
        {
            // A Windows dedicated server loads mod DLLs from bytes, so the assembly
            // has no file location there; the game's own Mod.Path always works.
            string location = Assembly.GetExecutingAssembly().Location;
            GliderMod.Directory = mod != null && !string.IsNullOrEmpty(mod.Path) ? mod.Path
                : !string.IsNullOrEmpty(location) ? Path.GetDirectoryName(location) : ".";
            Log.Out("[GliderSuit] Time.fixedDeltaTime = " + Time.fixedDeltaTime +
                ", Time.timeScale = " + Time.timeScale);
            GliderMod.Reload();
        }
        catch (Exception ex)
        {
            Log.Error("[GliderSuit] Config load failed; using defaults: " + ex);
        }

        try
        {
            ModEvents.PlayerSpawnedInWorld.RegisterHandler(GliderMod.OnPlayerSpawnedInWorld);
            ModEvents.WorldShuttingDown.RegisterHandler(GliderMod.OnWorldShuttingDown);
        }
        catch (Exception ex)
        {
            Log.Error("[GliderSuit] Event registration failed: " + ex);
        }

        GliderMod.ApplyPatches();
        Log.Out("[GliderSuit] Loaded v" + GliderMod.Version + ".");
    }
}

internal static class GliderMod
{
    internal const string Version = "1.0.0";
    internal static string Directory = "";
    internal static GliderConfig Settings = new GliderConfig();

    /// <summary>
    /// False if any Harmony patch failed to apply. Activating the wings is then
    /// refused, so a half-patched game can never fly without its damage rules.
    /// </summary>
    internal static bool PatchesOk = true;

    internal static void Reload()
    {
        Settings = GliderConfig.Load(Directory);
    }

    internal static void ApplyPatches()
    {
        var harmony = new Harmony("jason.glidersuit");
        int applied = 0;
        int failed = 0;
        foreach (Type type in Assembly.GetExecutingAssembly().GetTypes())
        {
            if (!type.IsDefined(typeof(HarmonyPatch), false)) continue;
            try
            {
                harmony.CreateClassProcessor(type).Patch();
                applied++;
            }
            catch (Exception ex)
            {
                failed++;
                Log.Error("[GliderSuit] PATCH FAILED: " + type.Name + ": " + ex);
            }
        }

        PatchesOk = failed == 0;
        if (PatchesOk)
            Log.Out("[GliderSuit] " + applied + " patch classes applied.");
        else
            Log.Error("[GliderSuit] " + failed + " patch class(es) failed, " + applied +
                " applied. Activating the wings is DISABLED until this is fixed.");
    }

    internal static void OnPlayerSpawnedInWorld(ref ModEvents.SPlayerSpawnedInWorldData data)
    {
        if (!data.IsLocalPlayer) return;
        try
        {
            World world = GameManager.Instance != null ? GameManager.Instance.World : null;
            EntityPlayerLocal player = world != null ? world.GetEntity(data.EntityId) as EntityPlayerLocal : null;
            if (player == null && world != null) player = world.GetPrimaryPlayer();
            if (player == null) return;
            GliderController.Attach(player);
        }
        catch (Exception ex)
        {
            Log.Warning("[GliderSuit] PlayerSpawnedInWorld: " + ex.Message);
        }
    }

    internal static void OnWorldShuttingDown(ref ModEvents.SWorldShuttingDownData data)
    {
        try
        {
            GliderController.Detach();
            GliderAudio.Reset();
            GliderAssets.Clear();
        }
        catch (Exception ex)
        {
            Log.Warning("[GliderSuit] WorldShuttingDown: " + ex.Message);
        }
    }
}
