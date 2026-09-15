using HarmonyLib;

namespace VeeItemInfo;

// Every patch is a Postfix that only signals ItemInfoController; none contains update logic.
// Targets are named with nameof (private Unity messages included, since Assembly-CSharp is
// publicized), so a method the game renames breaks the build instead of silently never firing.

internal static class GUIManagerStartPatch
{
    [HarmonyPatch(typeof(GUIManager), nameof(GUIManager.Start))]
    [HarmonyPostfix]
    private static void Postfix()
    {
        Overlay.Create();
    }
}

internal static class CharacterItemsUpdatePatch
{
    [HarmonyPatch(typeof(CharacterItems), nameof(CharacterItems.Update))]
    [HarmonyPostfix]
    private static void Postfix()
    {
        ItemInfoController.Tick();
    }
}

internal static class CharacterItemsEquipPatch
{
    [HarmonyPatch(typeof(CharacterItems), nameof(CharacterItems.Equip))]
    [HarmonyPostfix]
    private static void Postfix(CharacterItems __instance)
    {
        ItemInfoController.MarkDirtyIfObserved(__instance.character);
    }
}

internal static class ItemCookingFinishCookingPatch
{
    [HarmonyPatch(typeof(ItemCooking), nameof(ItemCooking.FinishCooking))]
    [HarmonyPostfix]
    private static void Postfix(ItemCooking __instance)
    {
        ItemInfoController.MarkDirtyIfObserved(__instance.item.holderCharacter);
    }
}

internal static class ActionReduceUsesPatch
{
    [HarmonyPatch(typeof(Action_ReduceUses), nameof(Action_ReduceUses.ReduceUsesRPC))]
    [HarmonyPostfix]
    private static void Postfix(Action_ReduceUses __instance)
    {
        ItemInfoController.MarkDirtyIfObserved(__instance.character);
    }
}
