using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class ConsoleCmdGlider : ConsoleCmdAbstract
{
    private const string ModItem = "modArmorGliderSuit";
    private const string SchematicItem = "modArmorGliderSuitSchematic";
    private const string HarnessBuff = "buffGliderSuitHarness";
    private const string DefaultOutfit = "armorScavengerOutfit";

    public override string[] getCommands() { return new[] { "glider", "glidersuit" }; }
    public override string getDescription() { return "Glider Suit test commands"; }
    public override string getHelp()
    {
        return "glider give | outfit [item] [quality] | mod [count] | schematic [count] | kit | " +
               "up [m] | climb | open | fold | speed <m/s> | pitch <deg> | impact <m/s> | ceiling | " +
               "foldpose [0..1|on|off] | status | reload | debug [on|off] | bones | heal";
    }
    public override bool IsExecuteOnClient => true;

    public override void Execute(List<string> args, CommandSenderInfo sender)
    {
        string command = args.Count == 0 ? "help" : args[0].ToLowerInvariant();
        EntityPlayerLocal player = GameManager.Instance != null && GameManager.Instance.World != null
            ? GameManager.Instance.World.GetPrimaryPlayer() : null;
        if (player == null)
        {
            Log.Out("[GliderSuit] No local player; run this from a game client.");
            return;
        }
        try
        {
            switch (command)
            {
                case "give": GiveAll(player); break;
                case "outfit": GiveOutfit(args, player); break;
                case "mod": GiveItem(player, ModItem, Count(args, 1, 1)); break;
                case "schematic": GiveItem(player, SchematicItem, Count(args, 1, 1)); break;
                case "kit": GiveKit(player); break;
                case "up": Up(args, player); break;
                case "open": GliderController.ForceOpen(player); Log.Out("[GliderSuit] Open requested (state=" + GliderState.Phase + ")."); break;
                case "fold": GliderController.ForceFold("command"); Log.Out("[GliderSuit] Fold requested (state=" + GliderState.Phase + ")."); break;
                case "speed": SetSpeed(args, player); break;
                case "pitch": SetPitch(args, player); break;
                case "impact": Impact(args, player); break;
                case "ceiling": Ceiling(player); break;
            case "climb": Climb(args, player); break;
                case "foldpose": FoldPose(args); break;
                case "status": Status(player); break;
                case "reload":
                {
                    // "glider debug on" is a session switch, not a file setting: keep it,
                    // or every reload silently stops the flight log.
                    bool debugWasOn = GliderMod.Settings.Debug;
                    GliderMod.Reload();
                    if (debugWasOn) GliderMod.Settings.Debug = true;
                    GliderWorn.ReapplyPlacement();
                    Status(player);
                    break;
                }
                case "debug": Debug(args); break;
                case "bones": Bones(player); break;
                case "heal": Heal(player); break;
                default: Log.Out("[GliderSuit] " + getHelp()); break;
            }
        }
        catch (Exception ex)
        {
            Log.Warning("[GliderSuit] Command failed: " + ex);
        }
    }

    private static void GiveAll(EntityPlayerLocal player)
    {
        GiveOutfit(new List<string> { "outfit" }, player);
        GiveItem(player, SchematicItem, 1);
        GiveItem(player, ModItem, 1);
    }

    private static void GiveOutfit(List<string> args, EntityPlayerLocal player)
    {
        string name = args.Count > 1 ? args[1] : DefaultOutfit;
        int quality = 6;
        if (args.Count > 2 && (!int.TryParse(args[2], out quality) || quality < 1 || quality > 6))
        {
            Log.Out("[GliderSuit] Quality must be 1-6.");
            return;
        }
        ItemValue prototype = ItemClass.GetItem(name, false);
        if (prototype == null || prototype.IsEmpty())
        {
            Log.Out("[GliderSuit] Unknown item: " + name);
            return;
        }
        if (prototype.ItemClass == null || !prototype.ItemClass.ItemTags.Test_AnySet(ArmorChestTag))
        {
            Log.Out("[GliderSuit] " + name + " is not chest armor (armorChest).");
            return;
        }
        ItemValue outfit = new ItemValue(prototype.type, quality, quality, false);
        GiveStack(player, new ItemStack(outfit, 1));
        Log.Out("[GliderSuit] Gave " + name + " Q" + quality + ".");
    }

    private static void GiveKit(EntityPlayerLocal player)
    {
        ItemValue prototype = ItemClass.GetItem(DefaultOutfit, false);
        if (prototype == null || prototype.IsEmpty())
        {
            Log.Out("[GliderSuit] Unknown item: " + DefaultOutfit);
            return;
        }
        ItemValue mod = ItemClass.GetItem(ModItem, false);
        if (mod == null || mod.IsEmpty())
        {
            Log.Out("[GliderSuit] " + ModItem + " is not loaded.");
            return;
        }
        ItemValue outfit = new ItemValue(prototype.type, 6, 6, false);
        bool installed = false;
        for (int i = 0; i < outfit.ModificationCount; i++)
        {
            ItemValue slot = outfit.GetModification(i);
            if (slot == null || slot.IsEmpty())
            {
                outfit.SetModification(i, new ItemValue(mod.type));
                installed = true;
                break;
            }
        }
        if (!installed)
        {
            Log.Out("[GliderSuit] No free mod slot on " + DefaultOutfit + " Q6; giving the mod loose instead.");
            GiveItem(player, ModItem, 1);
        }
        GiveStack(player, new ItemStack(outfit, 1));
        Log.Out("[GliderSuit] Gave " + DefaultOutfit + " Q6" + (installed ? " with the glider mod installed." : "."));
    }

    private static void GiveItem(EntityPlayerLocal player, string name, int count)
    {
        ItemValue value = ItemClass.GetItem(name, false);
        if (value == null || value.IsEmpty())
        {
            Log.Out("[GliderSuit] Item unavailable after XML load: " + name);
            return;
        }
        int remaining = count;
        int given = 0;
        while (remaining > 0)
        {
            int batch = Math.Min(remaining, 150);
            ItemStack stack = new ItemStack(value, batch);
            if ((player.bag != null && player.bag.AddItem(stack)) ||
                (player.inventory != null && player.inventory.AddItem(stack)))
            {
                given += batch;
                remaining -= batch;
                continue;
            }
            break;
        }
        if (remaining > 0)
        {
            GameManager.Instance.ItemDropServer(new ItemStack(value, remaining), player.GetPosition(),
                Vector3.zero, player.entityId, 60f, false);
            Log.Out("[GliderSuit] Backpack full; dropped " + remaining + " " + name + ".");
        }
        if (given > 0) Log.Out("[GliderSuit] Gave " + given + " " + name + ".");
    }

    private static void GiveStack(EntityPlayerLocal player, ItemStack stack)
    {
        if ((player.bag != null && player.bag.AddItem(stack)) ||
            (player.inventory != null && player.inventory.AddItem(stack)))
            return;
        if (stack.count > 0)
            GameManager.Instance.ItemDropServer(stack, player.GetPosition(), Vector3.zero, player.entityId, 60f, false);
    }

    private static void Up(List<string> args, EntityPlayerLocal player)
    {
        float meters = 80f;
        if (args.Count > 1 && (!float.TryParse(args[1], out meters) || meters < 1f || meters > 500f))
        {
            Log.Out("[GliderSuit] Height must be 1-500 m.");
            return;
        }
        player.TeleportToPosition(player.GetPosition() + Vector3.up * meters);
        Log.Out("[GliderSuit] Moved up " + meters.ToString("F0") + " m.");
    }

    /// <summary>Test cheat: jump straight to a flight speed without the climb or the height.</summary>
    private static void SetSpeed(List<string> args, EntityPlayerLocal player)
    {
        if (!RequireHarness(player)) return;
        if (!GliderState.IsActive) { Log.Out("[GliderSuit] Not gliding."); return; }
        if (args.Count < 2 || !float.TryParse(args[1], out float speed) || speed < 0f || speed > 100f)
        {
            Log.Out("[GliderSuit] Speed must be 0-100 m/s.");
            return;
        }
        GliderState.Speed = speed;
        Log.Out("[GliderSuit] Flight speed set to " + speed.ToString("F1") + " m/s.");
    }

    /// <summary>Test cheat: jump straight to a nose angle (negative is down).</summary>
    private static void SetPitch(List<string> args, EntityPlayerLocal player)
    {
        if (!RequireHarness(player)) return;
        if (!GliderState.IsActive) { Log.Out("[GliderSuit] Not gliding."); return; }
        if (args.Count < 2 || !float.TryParse(args[1], out float pitch) || pitch < -89f || pitch > 89f)
        {
            Log.Out("[GliderSuit] Pitch must be -89 to 89 degrees.");
            return;
        }
        GliderState.PitchDeg = pitch;
        GliderState.EasingToTrim = false;
        Log.Out("[GliderSuit] Nose set to " + pitch.ToString("F1") + " degrees.");
    }

    /// <summary>
    /// Test cheat: a crash at this speed, through the same path as a real one, so
    /// crash damage can be tuned without flying (D11).
    /// </summary>
    private static void Impact(List<string> args, EntityPlayerLocal player)
    {
        if (!RequireHarness(player)) return;
        if (args.Count < 2 || !float.TryParse(args[1], out float speed) || speed < 0f || speed > 100f)
        {
            Log.Out("[GliderSuit] Impact speed must be 0-100 m/s.");
            return;
        }
        GliderConfig config = GliderMod.Settings;
        float impact = GliderFlight.Impact(speed) * config.CrashDamageScale;
        int before = player.Health;
        GliderState.PendingImpact = impact;
        GliderState.ImpactArmedUntil = Time.time + config.LandingGraceSeconds;
        player.FallImpact(impact);
        Log.Out("[GliderSuit] impact " + speed.ToString("F1") + " m/s -> impact value " + impact.ToString("F3") +
            ", health " + before + " -> " + player.Health + " (-" + (before - player.Health) + ")");
    }

    /// <summary>
    /// Test cheat for the takeoff ceiling (R9), in one command because nobody can
    /// type while falling. From anywhere: lifts the player 100 m, lets them fall
    /// for two seconds, opens the wings, then holds the nose fully up at 40 m/s
    /// for ten seconds. The climb must stop just under the height of the lift.
    /// If the wings are already open it starts the climb straight away.
    /// </summary>
    private static void Climb(List<string> args, EntityPlayerLocal player)
    {
        if (!RequireHarness(player)) return;
        if (GliderState.IsActive)
        {
            GliderState.ClimbTestUntil = Time.time + GliderState.ClimbTestSeconds;
            Log.Out("[GliderSuit] Climb test started: nose fully up at " +
                GliderState.ClimbTestSpeed.ToString("F0") + " m/s for " +
                GliderState.ClimbTestSeconds.ToString("F0") + " s.");
            return;
        }
        player.TeleportToPosition(player.GetPosition() + Vector3.up * 100f);
        GliderState.ClimbTestOpenAt = Time.time + 2f;
        Log.Out("[GliderSuit] Climb test: moved up 100 m; the wings open in 2 s and then climb.");
    }

    private static void Ceiling(EntityPlayerLocal player)
    {
        if (!RequireHarness(player)) return;
        float y = player.vp_FPController != null ? GliderFlight.WorldY(player.vp_FPController) : player.transform.position.y;
        Log.Out("[GliderSuit] ceilingY=" + GliderState.CeilingY.ToString("F2") +
            " playerY=" + y.ToString("F2") +
            " room=" + (GliderState.CeilingY - y).ToString("F2") + " m");
    }

    /// <summary>
    /// Test cheat: hold the wings at one fold fraction to check the art. Takes the
    /// same on/off words as "glider debug", because that is what everyone types.
    /// </summary>
    private static void FoldPose(List<string> args)
    {
        string value = args.Count > 1 ? args[1].ToLowerInvariant() : "off";
        if (value == "off" || value == "false")
        {
            GliderWorn.HoldFold = -1f;
            Log.Out("[GliderSuit] foldpose off; the wings follow the state again.");
            return;
        }
        if (value == "on" || value == "true")
        {
            GliderWorn.HoldFold = 1f;
            Log.Out("[GliderSuit] foldpose held fully extended.");
            return;
        }
        if (!float.TryParse(value, out float fraction) || fraction < 0f || fraction > 1f)
        {
            Log.Out("[GliderSuit] foldpose takes 0..1, on or off.");
            return;
        }
        GliderWorn.HoldFold = fraction;
        Log.Out("[GliderSuit] foldpose held at " + fraction.ToString("F2") + ".");
    }

    private static bool RequireHarness(EntityPlayerLocal player)
    {
        if (player.Buffs.HasBuff(HarnessBuff)) return true;
        Log.Out("[GliderSuit] No glider harness on the equipped chest armor.");
        return false;
    }

    private static void Status(EntityPlayerLocal player)
    {
        GliderConfig config = GliderMod.Settings;
        Log.Out("[GliderSuit] patches=" + (GliderMod.PatchesOk ? "ok" : "FAILED") +
            " state=" + GliderState.Phase +
            " harness=" + player.Buffs.HasBuff(HarnessBuff) +
            " active=" + player.Buffs.HasBuff(GliderController.ActiveBuff) +
            " ceilingY=" + GliderState.CeilingY.ToString("F1") +
            " lastFold=" + GliderState.LastFoldReason);
        Log.Out("[GliderSuit] game=" + (SingletonMonoBehaviour<ConnectionManager>.Instance.IsServer ? "host" : "client") +
            " other players gliding: " + GliderRemote.Describe());
        Log.Out("[GliderSuit] cruise_speed=" + config.CruiseSpeed + " glide_ratio=" + config.GlideRatio +
            " stall_speed=" + config.StallSpeed + " stall_recover_speed=" + config.StallRecoverSpeed +
            " turn_rate=" + config.TurnRate + " unfold_time=" + config.UnfoldTime +
            " crash_damage_scale=" + config.CrashDamageScale + " debug=" + config.Debug +
            " devFile=" + config.DevFile);
    }

    private static void Debug(List<string> args)
    {
        bool on = true;
        if (args.Count > 1)
        {
            string value = args[1].ToLowerInvariant();
            if (value == "off" || value == "false" || value == "0") on = false;
        }
        GliderMod.Settings.Debug = on;
        Log.Out("[GliderSuit] Debug " + (on ? "on" : "off") + ".");
    }

    /// <summary>Dumps the rig's bone catalogs. §3.4 and §5.7 need the real names.</summary>
    private static void Bones(EntityPlayerLocal player)
    {
        EModelSDCS model = player.emodel as EModelSDCS;
        if (model == null)
        {
            Log.Out("[GliderSuit] No SDCS model on the local player; cannot list bones.");
            return;
        }
        Dump(model.boneCatalog, "boneCatalog");
        Dump(model.boneCatalogFP, "boneCatalogFP");
    }

    private static void Dump(SDCSUtils.TransformCatalog catalog, string label)
    {
        if (catalog == null)
        {
            Log.Out("[GliderSuit] " + label + " is null.");
            return;
        }
        Log.Out("[GliderSuit] " + label + ": " + catalog.Count + " bones.");
        foreach (KeyValuePair<string, Transform> entry in catalog)
        {
            if (entry.Value == null) continue;
            Log.Out("[GliderSuit]   " + entry.Key +
                " localPos=" + entry.Value.localPosition +
                " localRot=" + entry.Value.localRotation.eulerAngles +
                " localScale=" + entry.Value.localScale);
        }
    }

    private static void Heal(EntityPlayerLocal player)
    {
        player.Health = player.GetMaxHealth();
        player.Buffs.RemoveBuff("buffLegSprained", -1, true);
        player.Buffs.RemoveBuff("buffLegBroken", -1, true);
        Log.Out("[GliderSuit] Health restored to " + player.Health + " and leg injuries cleared.");
    }

    private static int Count(List<string> args, int index, int fallback)
    {
        if (args.Count <= index) return fallback;
        if (!int.TryParse(args[index], out int count) || count < 1 || count > 1000)
        {
            Log.Out("[GliderSuit] Count must be 1-1000.");
            return fallback;
        }
        return count;
    }

    private static readonly FastTags<TagGroup.Global> ArmorChestTag = FastTags<TagGroup.Global>.Parse("armorChest");
}
