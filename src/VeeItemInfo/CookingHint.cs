using UnityEngine;

namespace VeeItemInfo;

/// <summary>
/// The one line that says what cooking does to an item.
///
/// Deliberately not a cook-stage counter. Cooking mutates the very component fields the rest
/// of the overlay reads - <see cref="ItemCooking.ChangeStatsCooked"/> rewrites
/// <c>Action_RestoreHunger.restorationAmount</c> and <c>Action_GiveExtraStamina.amount</c> in
/// place, and the cooking behaviours switch actions on and off - so a cooked item's numbers
/// are already correct everywhere else without any help from here. All this line answers
/// is: <b>should I put it on the fire again?</b>
///
/// That question is about the <i>next</i> cook, not the cooking the item has already had.
/// Getting that wrong is what made a Big Lollipop already at stage 1 promise a gain for a
/// second cook that changes nothing at all.
///
/// Shapes, and no blast icon anywhere:
///   +&lt;fire&gt;      the next cook improves it.
///   -&lt;fire&gt;      the next cook makes it worse - burnt, poisoned.
///   ---&lt;fire&gt;    the next cook destroys it - wrecked, popped, melted.
///   &lt;fire&gt; -&gt; 4.8m [+20 &lt;injury&gt;]   cooking turns it into a blast over that radius.
///   (nothing)     cooking is impossible, or the next cook changes nothing worth saying.
///
/// A dynamite glyph was tried for the explosion case and rejected: it reads as danger, but
/// most of these explosions help - Antidote, Cure-All and Faerie Lantern all become a cloud
/// of what they do. Where an explosion does hurt, the injury figure says so outright, which
/// is honest in a way a symbol is not. "It becomes a cloud of what it does" is the whole
/// message; the cloud's own effect is not repeated on the line.
/// </summary>
internal static class CookingHint
{
    private static string Fire => StatusIcons.Tag("Cook");

    /// <summary>Cooking helps.</summary>
    internal static string Good(int strength = 1) =>
        EffectColors.Positive + new string('+', Mathf.Clamp(strength, 1, 3)) + "</color> "
        + EffectColors.Get("Cook") + Fire + "</color>";

    /// <summary>Cooking hurts. Three minuses is the item gone altogether.</summary>
    internal static string Bad(int strength = 1) =>
        EffectColors.Negative + new string('-', Mathf.Clamp(strength, 1, 3)) + "</color> "
        + EffectColors.Get("Cook") + Fire + "</color>";

    /// <summary>The item is destroyed outright - wrecked, popped, melted.</summary>
    private static string Destroyed() => Bad(3);

    /// <summary>
    /// Cooking sets it off. <paramref name="radius"/> is in Unity units; pass a negative
    /// <paramref name="injury"/> to leave the damage figure off, which is what a purely
    /// helpful explosion wants.
    /// </summary>
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
    /// What one more turn on the fire would do to this item, or null when there is nothing
    /// worth saying.
    ///
    /// The stat ladder in <c>ChangeStatsCooked</c> is not linear, which is why the stage
    /// matters. Reaching stage 1 doubles hunger restoration and multiplies bonus stamina -
    /// or hands out ten where there was none, so <i>every</i> consumable gains at stage 1;
    /// reaching stage 2 changes <i>nothing</i>; stage 3 and beyond burn it, taking 5 off the
    /// hunger restored each time, zeroing bonus stamina, and from stage 4 adding poison.
    ///
    /// Behaviours are judged by what kind they are, never by which item carries them: an
    /// explosion is a blast or a destruction depending on what its prefab does, enabling an
    /// action is a gain, lengthening an affliction is a gain, wrecking is destruction.
    /// Disabling an action is not judged at all - on every food it comes with the ladder's
    /// gain anyway, and on the one item where it stands alone the game's data is missing the
    /// replacement it enables, so nothing is claimed either way.
    /// </summary>
    /// <remarks>
    /// <paramref name="cooking"/> is null for a prefab that carries no ItemCooking. That is
    /// most foods: Item.Awake does GetOrAddComponent, so every live item has one, and a prefab
    /// without one behaves as the default - cookable, no behaviours, the plain stat ladder.
    /// Describing the absence as that default is what keeps the showcase honest about the 45
    /// consumables whose prefab never mentions cooking.
    /// </remarks>
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

        // timesCookedLocal is set from the item's data on a live instance and already folds
        // preCooked in; on a prefab it is still zero, so preCooked is the floor. Cooked Bird
        // ships at stage 1, and its prefab was promising a gain for the cook that changes nothing.
        int cooked = Mathf.Max(cooking.timesCookedLocal, cooking.preCooked);
        int next = cooked + 1;
        if (next > GameValues.CookingMax)
        {
            return null;
        }

        // A wreck-on-cook item is destroyed the first time and cannot be cooked again.
        if (cooking.wreckWhenCooked)
        {
            return cooked > 0 ? null : Destroyed();
        }

        // How much the behaviours improve the item: 0 for not at all, 1 for a gain, 3 for the
        // one gain nothing else in the game offers - a cook that makes an item cure curse.
        int improvement = 0;
        foreach (AdditionalCookingBehavior behaviour in cooking.additionalCookingBehaviors)
        {
            if (behaviour == null || next < behaviour.cookedAmountToTrigger)
            {
                continue;
            }

            // A once-only behaviour that has already had its cook is spent.
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

                // Cooking makes it another item - a Frog into Frog Legs. Plain "+" rather than
                // naming the result: what it becomes is the player's to find out.
                case CookingBehavior_ReplaceItem:
                    improvement = Mathf.Max(improvement, 1);
                    break;

                // Switching an action on that is currently off is a gain - the Sports Drink's
                // second stamina action, Mandrake's curse cure. One already on is nothing.
                case CookingBehavior_EnableScripts enable:
                    improvement = Mathf.Max(improvement, EnableStrength(enable));
                    break;

                // Fortified Milk: ten more seconds of shield.
                case CookingBehavior_ChangeAfflictionTime change:
                    improvement = Mathf.Max(improvement, change.change > 0f ? 1 : 0);
                    break;
            }
        }

        // An item that opts out of the ladder is described by its behaviours alone.
        if (cooking.ignoreDefaultCookBehavior)
        {
            return improvement > 0 ? Good(improvement) : null;
        }

        // The ladder's own gain and a behaviour's gain are the same "+" - except the curse
        // cure, which outranks whatever the ladder says.
        string? ladder = Ladder(item, consumable, next);
        if (improvement > 1)
        {
            return Good(improvement);
        }

        return ladder ?? (improvement > 0 ? Good() : null);
    }

    /// <summary>
    /// The default cooking every item gets, for a given next stage: better at 1, unchanged
    /// at 2, worse from 3. Null where the ladder has nothing to act on.
    /// </summary>
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

        // Reaching stage 2 changes nothing at all. Saying "+" here told players to cook a
        // Big Lollipop from Cooked to Well Done for a benefit that does not exist.
        if (next == 2)
        {
            return null;
        }

        return Bad();
    }

    /// <summary>
    /// Whether the stat ladder changes anything the player gets. Every consumable gains at
    /// stage 1, because ChangeStatsCooked adds a stamina action where there is none. A
    /// non-consumable gains only if a hunger or stamina action of its fires on use rather than
    /// on consumption - Scout Cookies - since cooking rewrites those fields regardless of how
    /// the item is used.
    /// </summary>
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

    /// <summary>
    /// Whether the item can be eaten. Two actions do that: Action_Consume, and
    /// Action_ConsumeAndSpawn, which is a Berrynana being eaten and leaving its peel.
    /// </summary>
    internal static bool IsConsumable(GameObject item) =>
        item.GetComponent<Action_Consume>() != null
        || item.GetComponent<Action_ConsumeAndSpawn>() != null;

    /// <summary>
    /// How much switching these scripts on gains: 0 where none is an action currently off,
    /// 1 for an action, 3 where that action removes curse. Curse is the status almost
    /// nothing in the game takes off, so a cook that makes an item cure it is the one cook
    /// worth three plusses - Mandrake in 2.4.b, but asked of the action rather than the name.
    /// </summary>
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

    /// <summary>
    /// A cooking explosion is a blast where the prefab does something to whoever it reaches,
    /// and plain destruction where it does not - a balloon pops, a snowball melts, and the
    /// "explosion" is a puff with nothing in it.
    /// </summary>
    private static string DescribeExplosion(ItemCooking cooking)
    {
        GameObject? prefab = cooking.explosionPrefab;
        if (prefab == null)
        {
            return Destroyed();
        }

        // The blast radius is AOE.range, not a collider. Reading a SphereCollider was the
        // first attempt and silently found nothing, which is why every exploding item was
        // falling through to the plain red minus.
        AOE? blast = null;
        float injury = -1f;
        foreach (AOE aoe in prefab.GetComponentsInChildren<AOE>(includeInactive: true))
        {
            if (aoe.range <= 0f || !Affects(aoe))
            {
                continue;
            }

            blast ??= aoe;

            // Only injury is called out. A helpful explosion says nothing beyond its reach.
            //
            // Delivered, not advertised. RPC_CookingExplode explicitly handles the local
            // character still holding the item, so a cooking explosion going off point-blank is
            // the ordinary case rather than an edge one - a stovetop advertising 20 injury
            // gives 17.5.
            if (aoe.statusType == CharacterAfflictions.STATUSTYPE.Injury && aoe.statusAmount > 0f)
            {
                injury = Mathf.Max(injury, Blast.Delivered(aoe, aoe.statusAmount));
            }
        }

        if (blast == null)
        {
            // A field you stand in rather than a blast - still something the item becomes.
            StatusField? field = prefab.GetComponentInChildren<StatusField>(includeInactive: true);
            return field != null ? Explodes(field.radius) : Destroyed();
        }

        return Explodes(blast.range, injury);
    }

    /// <summary>Whether an AOE does anything to a character it catches.</summary>
    private static bool Affects(AOE aoe) =>
        aoe.statusAmount != 0f
        || (aoe.addtlStatus != null && aoe.addtlStatus.Length > 0)
        || (aoe.hasAffliction && aoe.affliction != null);
}
