using System.Reflection;
using HarmonyLib;
using UnityEngine;

/// <summary>
/// Attaches the worn rig whenever the game rebuilds a character's meshes: the
/// world (third-person) rig and the inventory preview rig (§5.6). The
/// first-person set is managed by the controller, because it hangs off the
/// player rather than off a bone.
/// </summary>
[HarmonyPatch(typeof(SDCSUtils), "CreateVizTP")]
internal static class Patch_SDCSCreateVizTP
{
    /// <summary>CreateVizTP(Archetype, ref GameObject baseRig, ref TransformCatalog boneCatalog, EntityAlive entity, bool isFPV)</summary>
    [HarmonyPostfix]
    private static void Postfix(SDCSUtils.TransformCatalog __2, EntityAlive __3)
    {
        GliderWorn.Sync(__3, __2, WornKind.World);
    }
}

/// <summary>
/// The 5-argument CreateVizUI overload used by the inventory character windows.
/// The 3-argument one is the character creator and is deliberately left alone.
/// Its signature contains ref parameters, which cannot go in an attribute, so
/// the target is resolved at patch time.
/// </summary>
[HarmonyPatch]
internal static class Patch_SDCSCreateVizUI
{
    /// <summary>
    /// CreateVizUI(Archetype, ref GameObject baseRigUI, ref TransformCatalog boneCatalogUI,
    /// EntityAlive entity, bool useTempCosmetics). Matched by shape rather than by an
    /// exact by-ref signature, because a by-ref type array that does not line up makes
    /// AccessTools return null, which would silently disable every patch in the mod.
    /// </summary>
    [HarmonyTargetMethod]
    private static MethodBase TargetMethod()
    {
        foreach (MethodBase method in AccessTools.GetDeclaredMethods(typeof(SDCSUtils)))
        {
            if (method.Name != "CreateVizUI") continue;
            ParameterInfo[] parameters = method.GetParameters();
            if (parameters.Length == 5 && parameters[0].ParameterType == typeof(Archetype))
                return method;
        }
        Log.Error("[GliderSuit] SDCSUtils.CreateVizUI (5 arguments) was not found; the inventory preview wings are disabled.");
        return null;
    }

    [HarmonyPostfix]
    private static void Postfix(SDCSUtils.TransformCatalog __2, EntityAlive __3)
    {
        GliderWorn.Sync(__3, __2, WornKind.Ui);
    }
}
