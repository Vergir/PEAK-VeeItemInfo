using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using System;

namespace VeeItemInfo;

[BepInAutoPlugin]
public partial class Plugin : BaseUnityPlugin
{
    internal static ManualLogSource Log { get; private set; } = null!;

    private static readonly Type[] PatchTypes =
    {
        typeof(GUIManagerStartPatch),
        typeof(CharacterItemsUpdatePatch),
        typeof(CharacterItemsEquipPatch),
        typeof(ItemCookingFinishCookingPatch),
        typeof(ActionReduceUsesPatch),
    };

    private void Awake()
    {
        Log = Logger;
        PluginConfig.Bind(Config);
        ApplyPatches();
        Log.LogInfo($"Plugin {Name} is loaded!");
    }

    /// <summary>
    /// Harmony resolves patch targets by name at runtime, so a method renamed by a game
    /// update fails here rather than at build time. Patching one type at a time keeps a
    /// single dead target from taking the whole plugin down with it.
    /// </summary>
    private static void ApplyPatches()
    {
        foreach (Type patchType in PatchTypes)
        {
            try
            {
                Harmony.CreateAndPatchAll(patchType);
            }
            catch (Exception e)
            {
                Log.LogError($"Failed to apply {patchType.Name}, continuing without it: {e.Message}");
            }
        }
    }
}
