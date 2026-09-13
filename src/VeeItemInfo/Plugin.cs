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

    /// <summary>
    /// Undoes everything Awake did. Required for hot reloading (AutoReload and similar):
    /// without it a reload leaves the old patches applied and the old overlay in the HUD,
    /// so each reload stacks another copy on top of the last.
    /// </summary>
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

    /// <summary>
    /// Harmony resolves patch targets by name at runtime, so a method renamed by a game
    /// update fails here rather than at build time. Patching one type at a time keeps a
    /// single dead target from taking the whole plugin down with it.
    /// </summary>
    private void ApplyPatches()
    {
        foreach (Type patchType in PatchTypes)
        {
            try
            {
                // Keep the instance so OnDestroy can unpatch it again on reload.
                patches.Add(Harmony.CreateAndPatchAll(patchType, $"{Id}.{patchType.Name}"));
            }
            catch (Exception e)
            {
                Log.LogError($"Failed to apply {patchType.Name}, continuing without it: {e.Message}");
            }
        }
    }
}
