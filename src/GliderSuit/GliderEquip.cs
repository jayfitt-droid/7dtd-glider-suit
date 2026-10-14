using UnityEngine;

/// <summary>
/// "Is the glider installed in this entity's chest armor?" The replicated
/// equipment is the source of truth (so other players' folded wings work without
/// any new network message); the hidden buff is an equal alternative for the
/// local player, because the game adds it the moment the mod is equipped.
/// </summary>
internal static class GliderEquip
{
    internal const string ModItemName = "modArmorGliderSuit";
    internal const string HarnessBuff = "buffGliderSuitHarness";

    internal static bool HasGlider(EntityAlive entity)
    {
        if (entity == null) return false;
        if (entity is EntityPlayerLocal && entity.Buffs != null && entity.Buffs.HasBuff(HarnessBuff))
            return true;
        Equipment equipment = entity.equipment;
        if (equipment == null) return false;
        return HasMod(equipment.GetSlotItem((int)EquipmentSlots.Chest));
    }

    private static bool HasMod(ItemValue item)
    {
        if (item == null || item.IsEmpty()) return false;
        for (int i = 0; i < item.ModificationCount; i++)
        {
            ItemValue mod = item.GetModification(i);
            if (mod == null || mod.IsEmpty()) continue;
            ItemClass itemClass = mod.ItemClass;
            if (itemClass != null && itemClass.Name == ModItemName) return true;
        }
        return false;
    }
}
