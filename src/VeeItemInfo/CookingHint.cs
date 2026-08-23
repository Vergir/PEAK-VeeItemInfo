using UnityEngine;

namespace VeeItemInfo;

/// <summary>
/// The one line that says what cooking does to an item.
///
/// Deliberately not a cook-stage counter. Cooking mutates the very component fields the rest
/// of the overlay reads - <see cref="ItemCooking.ChangeStatsCooked"/> rewrites
/// <c>Action_RestoreHunger.restorationAmount</c> and <c>Action_GiveExtraStamina.amount</c> in
/// place - so a cooked item's numbers are already correct everywhere else without any help
/// from here. All this line answers is: <b>should I put it on the fire again?</b>
///
/// That question is about the <i>next</i> cook, not the cooking the item has already had.
/// Getting that wrong is what made a Big Lollipop already at stage 1 promise a gain for a
/// second cook that changes nothing at all.
///
/// Shapes, and no blast icon anywhere:
///   +&lt;fire&gt;      the next cook improves it. Repeated plusses mean an outsized gain.
///   -&lt;fire&gt;      the next cook ruins or destroys it.
///   &lt;fire&gt; -&gt; 4.8m [+20 &lt;injury&gt;]   cooking detonates it over that radius.
///   (nothing)     cooking is impossible, or the next cook changes nothing worth saying.
///
/// A dynamite glyph was tried for the explosion case and rejected: it reads as danger, but
/// most of these explosions help - Antidote, Cure-All and Faerie Lantern all heal everyone in
/// range. Where an explosion does hurt, the injury figure says so outright, which is honest
/// in a way a symbol is not.
/// </summary>
internal static class CookingHint
{
    private static string Fire => StatusIcons.Tag("Cook");

    /// <summary>Cooking helps. <paramref name="strength"/> plusses for an outsized gain.</summary>
    internal static string Good(int strength = 1) =>
        EffectColors.Positive + new string('+', Mathf.Clamp(strength, 1, 3)) + "</color> "
        + EffectColors.Get("Cook") + Fire + "</color>";

    /// <summary>Cooking ruins it - incinerated, popped, burnt or broken.</summary>
    internal static string Bad() =>
        EffectColors.Negative + "-</color> " + EffectColors.Get("Cook") + Fire + "</color>";

    /// <summary>
    /// Cooking sets it off. <paramref name="radius"/> is in metres; pass a negative
    /// <paramref name="injury"/> to leave the damage figure off, which is what a purely
    /// helpful explosion wants.
    /// </summary>
    internal static string Explodes(float radius, float injury = -1f)
    {
        string result = EffectColors.Get("Cook") + Fire + "</color>"
            + EffectColors.Neutral + EffectFormatter.Arrow + EffectFormatter.Metres(radius) + "</color>";

        if (injury > 0f)
        {
            result += " " + EffectFormatter.Token(injury, "Injury");
        }

        return result;
    }

    /// <summary>
    /// What one more turn on the fire would do to this item, or null when there is nothing
    /// worth saying.
    ///
    /// The stat ladder in <c>ChangeStatsCooked</c> is not linear, which is why the stage
    /// matters. Reaching stage 1 doubles hunger restoration and multiplies bonus stamina;
    /// reaching stage 2 changes <i>nothing</i>; stage 3 and beyond burn it, taking 5 off the
    /// hunger restored each time, zeroing bonus stamina, and from stage 4 adding poison.
    /// </summary>
    internal static string? Describe(ItemCooking cooking)
    {
        if (!cooking.canBeCooked)
        {
            return null;
        }

        int next = cooking.timesCookedLocal + 1;
        if (next > ItemCooking.COOKING_MAX)
        {
            return null;
        }

        // A wreck-on-cook item is destroyed the first time and cannot be cooked again.
        if (cooking.wreckWhenCooked)
        {
            return cooking.timesCookedLocal > 0 ? null : Bad();
        }

        // An explicit behaviour outranks the stat ladder: exploding or wrecking is the whole
        // story regardless of how many times the item has already been cooked.
        foreach (AdditionalCookingBehavior behavior in cooking.additionalCookingBehaviors)
        {
            switch (behavior)
            {
                case CookingBehavior_Explode explode:
                    if (next >= explode.cookedAmountToTrigger)
                    {
                        // Radius and damage live on the explosion prefab's AOE, not on the
                        // behaviour, so they are read there rather than hardcoded per item.
                        return DescribeExplosion(cooking);
                    }

                    break;

                case CookingBehavior_Wreck wreck:
                    if (next >= wreck.cookedAmountToTrigger)
                    {
                        return Bad();
                    }

                    break;
            }
        }

        // Past here the hint is only about the stat ladder, so an item that opts out of it
        // has nothing to report, and neither does one with no stats for it to touch.
        if (cooking.ignoreDefaultCookBehavior || !HasCookableStats(cooking))
        {
            return null;
        }

        if (next == 1)
        {
            return Good();
        }

        // Reaching stage 2 changes nothing at all. Saying "+" here told players to cook a
        // Big Lollipop from Cooked to Well Done for a benefit that does not exist.
        if (next == 2)
        {
            return null;
        }

        return Bad();
    }

    /// <summary>
    /// Whether the stat ladder has anything to act on. Only actions that fire on consumption
    /// count: cooking rewrites those fields, and an item nobody can eat never runs them.
    /// </summary>
    private static bool HasCookableStats(ItemCooking cooking) =>
        cooking.GetComponent<Action_Consume>() != null
        && (cooking.GetComponent<Action_RestoreHunger>() != null
            || cooking.GetComponent<Action_GiveExtraStamina>() != null
            || cooking.GetComponent<Action_ModifyStatus>() != null);

    private static string DescribeExplosion(ItemCooking cooking)
    {
        GameObject? prefab = cooking.explosionPrefab;
        if (prefab == null)
        {
            return Bad();
        }

        // The blast radius is AOE.range, not a collider. Reading a SphereCollider was the
        // first attempt and silently found nothing, which is why every exploding item was
        // falling through to the plain red minus.
        AOE? aoe = prefab.GetComponentInChildren<AOE>(includeInactive: true);
        if (aoe == null || aoe.range <= 0f)
        {
            return Bad();
        }

        // Only injury is called out. A helpful explosion says nothing beyond its reach,
        // which is the whole point of not using a blast symbol.
        float injury = aoe.statusType == CharacterAfflictions.STATUSTYPE.Injury
            ? aoe.statusAmount
            : -1f;

        return Explodes(aoe.range, injury > 0f ? injury : -1f);
    }
}
