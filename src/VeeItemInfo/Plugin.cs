using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using System;
using System.Collections.Generic;

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

    private readonly List<Harmony> patches = new();

    private void Awake()
    {
        Log = Logger;
        PluginConfig.Bind(Config);
        ApplyPatches();
        Log.LogInfo($"Plugin {Name} is loaded!");
    }

    /// <summary>Undoes everything Awake did, or each hot reload stacks another copy.</summary>
    private void OnDestroy()
    {
        foreach (Harmony harmony in patches)
        {
            harmony.UnpatchSelf();
        }

        patches.Clear();
        Overlay.Destroy();
        StatusIcons.Reset();
        ItemDump.Forget();
        ItemDescriptionBuilder.Forget();
        Log.LogInfo($"Plugin {Name} is unloaded!");
    }

    /// <summary>One type at a time, so a target that fails at runtime costs one hook, not the plugin.</summary>
    private void ApplyPatches()
    {
        foreach (Type patchType in PatchTypes)
        {
            try
            {
                patches.Add(Harmony.CreateAndPatchAll(patchType, $"{Id}.{patchType.Name}"));
            }
            catch (Exception e)
            {
                Log.LogError($"Failed to apply {patchType.Name}, continuing without it: {e.Message}");
            }
        }
    }
}
