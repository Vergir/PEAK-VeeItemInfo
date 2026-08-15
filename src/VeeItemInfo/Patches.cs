using HarmonyLib;

namespace VeeItemInfo;

// Every patch here is a Postfix that only flags state or forwards to ItemInfoController.
// None of them contain update logic. Keeping them trivial means a patch target that
// disappears in a game update costs us one signal, not the feature.

/// <summary>Builds the overlay once the HUD exists, instead of probing for it every frame.</summary>
internal static class GUIManagerStartPatch
{
    [HarmonyPatch(typeof(GUIManager), "Start")]
    [HarmonyPostfix]
    private static void Postfix()
    {
        Overlay.Create();
    }
}

/// <summary>The frame tick. The only patch that drives an update.</summary>
internal static class CharacterItemsUpdatePatch
{
    [HarmonyPatch(typeof(CharacterItems), "Update")]
    [HarmonyPostfix]
    private static void Postfix()
    {
        ItemInfoController.Tick();
    }
}

internal static class CharacterItemsEquipPatch
{
    [HarmonyPatch(typeof(CharacterItems), "Equip")]
    [HarmonyPostfix]
    private static void Postfix(CharacterItems __instance)
    {
        ItemInfoController.MarkDirtyIfObserved(__instance.character);
    }
}

internal static class ItemCookingFinishCookingPatch
{
    [HarmonyPatch(typeof(ItemCooking), "FinishCooking")]
    [HarmonyPostfix]
    private static void Postfix(ItemCooking __instance)
    {
        ItemInfoController.MarkDirtyIfObserved(__instance.item.holderCharacter);
    }
}

internal static class ActionReduceUsesPatch
{
    [HarmonyPatch(typeof(Action_ReduceUses), "ReduceUsesRPC")]
    [HarmonyPostfix]
    private static void Postfix(Action_ReduceUses __instance)
    {
        ItemInfoController.MarkDirtyIfObserved(__instance.character);
    }
}
