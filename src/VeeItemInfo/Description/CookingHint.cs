using UnityEngine;

namespace VeeItemInfo;

/// <summary>
/// The one line saying what one more turn on the fire does. The shapes and their reasons:
/// docs/design.md, "The cooking hint"; the ladder and behaviours it judges:
/// docs/internals_game.md, "Cooking".
/// </summary>
internal static class CookingHint
{
    private static string Fire => StatusIcons.Tag("Cook");

    internal static string Good(int strength = 1) =>
        EffectColors.Positive + new string('+', Mathf.Clamp(strength, 1, 3)) + "</color> "
        + EffectColors.Get("Cook") + Fire + "</color>";

    internal static string Bad(int strength = 1) =>
        EffectColors.Negative + new string('-', Mathf.Clamp(strength, 1, 3)) + "</color> "
        + EffectColors.Get("Cook") + Fire + "</color>";

    private static string Destroyed() => Bad(3);

    /// <summary>A negative <paramref name="injury"/> leaves the damage figure off.</summary>
    internal static string Explodes(float radius, float injury = -1f)
    {
        string result = EffectColors.Get("Cook") + Fire + "</color>"
            + EffectColors.Neutral + EffectFormatter.Arrow + EffectFormatter.PeakMetres(radius) + "</color>";

        if (injury > 0f)
        {
            result += " " + EffectFormatter.Token(injury, "Injury");
        }

        return result;
    }

    /// <summary>
    /// Null <paramref name="cooking"/> is a prefab with no ItemCooking; a live item always has
    /// one, so the absence is described as the default ladder.
    /// </summary>
    internal static string? Describe(GameObject item, ItemCooking? cooking)
    {
        bool consumable = IsConsumable(item);
        if (cooking == null)
        {
            return Ladder(item, consumable, next: 1);
        }

        if (!cooking.canBeCooked)
        {
            return null;
        }

        // On a prefab timesCookedLocal is still zero, so preCooked is the floor.
        int cooked = Mathf.Max(cooking.timesCookedLocal, cooking.preCooked);
        int next = cooked + 1;
        if (next > GameValues.CookingMax)
        {
            return null;
        }

        if (cooking.wreckWhenCooked)
        {
            return cooked > 0 ? null : Destroyed();
        }

        // 0 for no gain, 1 for a gain, 3 for a cook that makes the item cure curse.
        int improvement = 0;
        foreach (AdditionalCookingBehavior behaviour in cooking.additionalCookingBehaviors)
        {
            if (behaviour == null || next < behaviour.cookedAmountToTrigger)
            {
                continue;
            }

            if (behaviour.onlyOnce && cooked >= behaviour.cookedAmountToTrigger)
            {
                continue;
            }

            switch (behaviour)
            {
                // An explosion or a wreck is the whole story, whatever else the cook does.
                case CookingBehavior_Explode:
                    return DescribeExplosion(cooking);
                case CookingBehavior_Wreck:
                    return Destroyed();

                case CookingBehavior_ReplaceItem:
                    improvement = Mathf.Max(improvement, 1);
                    break;

                case CookingBehavior_EnableScripts enable:
                    improvement = Mathf.Max(improvement, EnableStrength(enable));
                    break;

                case CookingBehavior_ChangeAfflictionTime change:
                    improvement = Mathf.Max(improvement, change.change > 0f ? 1 : 0);
                    break;
            }
        }

        if (cooking.ignoreDefaultCookBehavior)
        {
            return improvement > 0 ? Good(improvement) : null;
        }

        // The curse cure outranks the ladder; any other gain is the same "+" the ladder gives.
        string? ladder = Ladder(item, consumable, next);
        if (improvement > 1)
        {
            return Good(improvement);
        }

        return ladder ?? (improvement > 0 ? Good() : null);
    }

    private static string? Ladder(GameObject item, bool consumable, int next)
    {
        if (!LadderApplies(item, consumable))
        {
            return null;
        }

        if (next == 1)
        {
            return Good();
        }

        // Reaching stage 2 changes nothing.
        if (next == 2)
        {
            return null;
        }

        return Bad();
    }

    private static bool LadderApplies(GameObject item, bool consumable)
    {
        if (consumable)
        {
            return true;
        }

        foreach (Action_RestoreHunger hunger in item.GetComponents<Action_RestoreHunger>())
        {
            if (!hunger.OnConsumed)
            {
                return true;
            }
        }

        foreach (Action_GiveExtraStamina stamina in item.GetComponents<Action_GiveExtraStamina>())
        {
            if (!stamina.OnConsumed)
            {
                return true;
            }
        }

        return false;
    }

    internal static bool IsConsumable(GameObject item) =>
        item.GetComponent<Action_Consume>() != null
        || item.GetComponent<Action_ConsumeAndSpawn>() != null;

    /// <summary>0 where no action is currently off, 1 for one that is, 3 where it cures curse.</summary>
    private static int EnableStrength(CookingBehavior_EnableScripts enable)
    {
        if (enable.scriptsToEnable == null)
        {
            return 0;
        }

        int strength = 0;
        foreach (MonoBehaviour script in enable.scriptsToEnable)
        {
            if (script is not ItemAction || script.enabled)
            {
                continue;
            }

            bool curesCurse = script is Action_ModifyStatus modify
                && modify.statusType == CharacterAfflictions.STATUSTYPE.Curse
                && modify.changeAmount < 0f;
            strength = Mathf.Max(strength, curesCurse ? 3 : 1);
        }

        return strength;
    }

    private static string DescribeExplosion(ItemCooking cooking)
    {
        GameObject? prefab = cooking.explosionPrefab;
        if (prefab == null)
        {
            return Destroyed();
        }

        // The blast radius is AOE.range, not a collider - a SphereCollider read found nothing.
        AOE? blast = null;
        float injury = -1f;
        foreach (AOE aoe in prefab.GetComponentsInChildren<AOE>(includeInactive: true))
        {
            if (aoe.range <= 0f || !Affects(aoe))
            {
                continue;
            }

            blast ??= aoe;

            // Delivered, not advertised: a cooking explosion goes off in the holder's hand.
            if (aoe.statusType == CharacterAfflictions.STATUSTYPE.Injury && aoe.statusAmount > 0f)
            {
                injury = Mathf.Max(injury, Blast.Delivered(aoe, aoe.statusAmount));
            }
        }

        if (blast == null)
        {
            StatusField? field = prefab.GetComponentInChildren<StatusField>(includeInactive: true);
            return field != null ? Explodes(field.radius) : Destroyed();
        }

        return Explodes(blast.range, injury);
    }

    private static bool Affects(AOE aoe) =>
        aoe.statusAmount != 0f
        || (aoe.addtlStatus != null && aoe.addtlStatus.Length > 0)
        || (aoe.hasAffliction && aoe.affliction != null);
}
