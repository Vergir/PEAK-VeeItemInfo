using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace VeeItemInfo;

/// <summary>
/// Dumps what an item is actually made of, so an unrecognised item can be identified
/// without decompiling first. Spawn the item, hold it with Debug Logging on, and the log
/// names every component the description chain could have matched.
/// </summary>
internal static class ItemDebug
{
    private static string lastLogged = "";

    /// <summary>
    /// Logs the held item once per item, not once per refresh - the poll would otherwise
    /// repeat the same block every second.
    /// </summary>
    internal static void LogItem(Item item)
    {
        if (item.gameObject.name == lastLogged)
        {
            return;
        }

        lastLogged = item.gameObject.name;
        Plugin.Log.LogInfo(Components(item));
    }

    /// <summary>
    /// The item's name, tags and every component on it, with the fields that drive the
    /// description. Shared by the held-item log and the whole-database dump, so the two can
    /// never disagree about what a component is worth printing.
    /// </summary>
    internal static string Components(Item item)
    {
        GameObject itemGameObj = item.gameObject;
        StringBuilder report = new();
        report.Append($"[item] {itemGameObj.name} name=\"{item.UIData?.itemName}\""
            + $" carryWeight={item.carryWeight} tags={item.itemTags}");

        Texture2D? icon = item.UIData?.GetIcon();
        report.Append($" icon={(icon == null ? "<none>" : icon.name)}");

        foreach (Component component in itemGameObj.GetComponents(typeof(Component)))
        {
            if (component == null)
            {
                continue;
            }

            // Transform and the renderer/collider furniture are on everything and say
            // nothing about what the item does.
            System.Type type = component.GetType();
            if (type == typeof(Transform) || type == typeof(RectTransform))
            {
                continue;
            }

            report.Append("\n[item]   ").Append(type.FullName);

            // Duplicated components with different values are the hardest bug to see from a
            // type list alone - two Action_GiveExtraStamina on one item look identical here
            // but produce two lines in the overlay. Printing the value each one carries, and
            // whether it is switched on, makes that obvious without a decompile.
            if (component is Behaviour behaviour && !behaviour.enabled)
            {
                report.Append(" [DISABLED]");
            }

            report.Append(Values(component));
        }

        return report.ToString();
    }

    private static string lastAudited = "";

    /// <summary>
    /// Reports anything in a finished description that would render in the overlay's base
    /// colour rather than a chosen one.
    ///
    /// The rule this enforces is that **every visible thing sits inside a colour tag**. It
    /// used not to be checkable by eye: a missed tag rendered pure white, which reads as
    /// "bright" rather than "wrong", and on a `tint=1` sprite it looked like a slightly
    /// crisper icon. Two lines had been leaking for as long as they had existed - the item
    /// duplication arrow and the low-gravity balloon.
    ///
    /// Reads the string that is actually handed to TextMeshPro, so it cannot be fooled by
    /// how the line was assembled, and it catches leaks nobody thought to look for.
    /// Whitespace between tags is fine and expected; it is invisible in any colour.
    /// </summary>
    internal static void LogUntagged(string description)
    {
        if (description == lastAudited)
        {
            return;
        }

        lastAudited = description;

        List<string> leaks = new();
        int depth = 0;
        int i = 0;

        while (i < description.Length)
        {
            if (description[i] == '<')
            {
                int close = description.IndexOf('>', i);
                if (close < 0)
                {
                    leaks.Add("unterminated tag");
                    break;
                }

                string tag = description.Substring(i + 1, close - i - 1);
                if (tag.StartsWith("#") || tag.StartsWith("color="))
                {
                    depth++;
                }
                else if (tag == "/color")
                {
                    if (depth > 0)
                    {
                        depth--;
                    }
                    else
                    {
                        leaks.Add("</color> closing nothing");
                    }
                }
                else if (tag.StartsWith("sprite") && depth == 0)
                {
                    leaks.Add("<" + tag + ">");
                }

                i = close + 1;
                continue;
            }

            if (depth == 0 && !char.IsWhiteSpace(description[i]))
            {
                int start = i;
                while (i < description.Length && description[i] != '<')
                {
                    i++;
                }

                leaks.Add("\"" + description.Substring(start, i - start).Trim() + "\"");
                continue;
            }

            i++;
        }

        if (depth != 0)
        {
            leaks.Add($"{depth} colour tag(s) left open");
        }

        if (leaks.Count > 0)
        {
            Plugin.Log.LogWarning("[color] untagged, renders in the base colour: "
                + string.Join(", ", leaks));
        }
    }

    /// <summary>The fields worth seeing, for the components that drive the description.</summary>
    private static string Values(Component component) => component switch
    {
        Action_RestoreHunger a => $" restorationAmount={a.restorationAmount} onConsumed={a.OnConsumed}",
        Action_GiveExtraStamina a => $" amount={a.amount} onConsumed={a.OnConsumed}",
        Action_ModifyStatus a => $" {a.statusType}={a.changeAmount} onConsumed={a.OnConsumed}"
            + $" ifSkeleton={a.ifSkeleton}",
        Action_InflictPoison a => $" perSecond={a.poisonPerSecond} time={a.inflictionTime} delay={a.delay}",
        Action_AddOrRemoveThorns a => $" thornCount={a.thornCount}",
        // Derived types first: a pattern switch takes the first match, and both of these
        // are Action_ApplyAffliction too.
        Peak.Action_SuperJumpAmulet a => $" petrifyPerUse={a.petrifyPerUse}"
            + $" affliction={Affliction(a.affliction)} extra=[{Afflictions(a.extraAfflictions)}]",
        Action_ApplyMassAffliction a => $" radius={a.radius} ignoreCaster={a.ignoreCaster}"
            + $" affliction={Affliction(a.affliction)} extra=[{Afflictions(a.extraAfflictions)}]",
        Action_ApplyAffliction a => $" affliction={Affliction(a.affliction)}"
            + $" extra=[{Afflictions(a.extraAfflictions)}]",
        Action_RaycastDart a => $" maxDistance={a.maxDistance} onHit=[{Afflictions(a.afflictionsOnHit)}]",
        Action_MoraleBoost a => $" radius={a.boostRadius} baseline={a.baselineStaminaBoost}"
            + $" perScout={a.staminaBoostPerAdditionalScout}",
        Action_Numb a => $" numbAmount={a.numbAmount}",
        Action_RandomMushroomEffect a => $" mushroomTypeIndex={a.mushroomTypeIndex}",
        Action_ClearAllStatus a => $" excludeCurse={a.excludeCurse}"
            + $" otherExclusions=[{string.Join(", ", a.otherExclusions)}]",
        StickyItemComponent a => $" thorns={a.addThornsToStuckPlayer} weight={a.addWeightToStuckPlayer}"
            + $" throwCharge={a.throwChargeRequirement}",
        ShelfShroom a => BreaksInto(a),
        VineShooter a => $" maxLength={a.maxLength}u -> {a.maxLength * CharacterStats.unitsToMeters}m",
        MagicBean a => Beanstalk(a),
        Action_Spawn a => Spawns(a),
        RopeShooter a => $" shootRange={a.maxLength}u ropeSegments={a.length}"
            + $" ropeSpacing={RopeSpacingOf(a)}u segmentLength={RopeSegmentLengthOf(a)}u"
            + $" ropeUnits={a.length * RopeSegmentLengthOf(a)}u"
            + $" unitsToMeters={CharacterStats.unitsToMeters}"
            + $" achievementMetres={Rope.GetLengthInMeters(a.length)}"
            + RopeScales(a),
        // RopeFuel goes through GetData, which needs a live instance - on a database
        // prefab it throws. A prefab has no scene, which is how the two are told apart.
        RopeSpool a => (a.gameObject.scene.IsValid()
                ? $" fuel={a.RopeFuel} metres={Rope.GetLengthInMeters(a.RopeFuel)}"
                : " fuel=<prefab>")
            + $" startFuel={a.ropeStartFuel} anti={a.isAntiRope}",
        MagicBugle a => $" totalTootTime={a.totalTootTime} initialTootCost={a.initialTootCost}",
        Peak.RitualDaggerFeedBehavior a => $" bonusStamina={a.bonusStamina}"
            + $" infiniteStaminaTime={a.infiniteStaminaTime}",
        ItemCooking c => $" canBeCooked={c.canBeCooked} wreck={c.wreckWhenCooked} preCooked={c.preCooked}"
            + $" ignoreDefault={c.ignoreDefaultCookBehavior} ignorePoison={c.ignoreDefaultPoisonBehavior}"
            + Prefab(" explosionPrefab", c.explosionPrefab)
            + Behaviours(c),
        Scorpion a => $" totalPoisonTime={a.totalPoisonTime}",
        Dynamite a => $" fuse={a.startingFuseTime} lightFuseRadius={a.lightFuseRadius}"
            + Prefab(" explodes", a.explosionPrefab),
        Constructable a => $" maxConstructDistance={a.maxConstructDistance}"
            + Prefab(" builds", a.constructedPrefab),
        Lantern a => $" startingFuel={a.startingFuel}" + Field(a.gameObject),
        Candle a => $" startingFuel={a.startingFuel}" + Field(a.gameObject),
        Peak.Action_HealingGem a => $" heal={Affliction(a.healingAffliction)}"
            + $" shield={Affliction(a.invincibilityAffliction)}"
            + $" ratio={a.healingToPetrifyRatio} petrify={a.minPetrify}-{a.maxPetrify}",
        Peak.Action_CloneSelectedItem a => $" petrify={a.petrify} mystical={a.petrifyMystical} range={a.range}",
        Peak.AmuletBase a => $" amuletIndex={a.amuletIndex} startActive={a.startActive}",
        _ => "",
    };

    /// <summary>
    /// Every cooking behaviour on the item, with what it would do. The type list alone said
    /// "behaviours=1" for Fortified Milk and Bing Bong alike, which is no help deciding what
    /// the hint should say.
    /// </summary>
    private static string Behaviours(ItemCooking cooking)
    {
        if (cooking.additionalCookingBehaviors == null || cooking.additionalCookingBehaviors.Length == 0)
        {
            return "";
        }

        StringBuilder text = new();
        foreach (AdditionalCookingBehavior behaviour in cooking.additionalCookingBehaviors)
        {
            if (behaviour == null)
            {
                text.Append("\n[item]     <null behaviour>");
                continue;
            }

            text.Append("\n[item]     ").Append(behaviour.GetType().Name)
                .Append($" at={behaviour.cookedAmountToTrigger} once={behaviour.onlyOnce}");
            text.Append(behaviour switch
            {
                CookingBehavior_DisableScripts b => $" disables=[{Names(b.scriptsToDisable)}]",
                CookingBehavior_EnableScripts b => $" enables=[{Names(b.scriptsToEnable)}]",
                CookingBehavior_RunActions b => $" runs=[{Names(b.actionsToRun)}]",
                CookingBehavior_ReplaceItem b => $" replaceWith={(b.replaceWithItem == null ? "<none>" : b.replaceWithItem.name)} cookNew={b.cookNewItem}",
                CookingBehavior_ChangeAfflictionTime b => $" change={b.change} on={(b.action == null ? "<none>" : Affliction(b.action.affliction))}",
                CookingBehavior_AddPoisonOnUse b => $" onUse={b.onUse} onConsume={b.onConsume}",
                CookingBehavior_AdjustStatusInstantly b => $" {b.statusType}={b.amount}",
                CookingBehavior_Explode b => $" dontRunIfOutOfFuel={b.dontRunIfOutOfFuel}",
                CookingBehavior_MessUpAudio b => $" pitch-={b.pitchReductionPerCooking} volume-={b.volumeReductionPerCooking} max={b.max}",
                CookingBehavior_ModifyBugleWobble b => $" wobble+={b.changePerCooking} max={b.maxCooking}",
                CookingBehavior_ModifyAudioSourcePitch b => $" pitch+={b.changePerCooking}",
                CookingBehavior_EnableDisableObjects b => $" enable={b.objectsToEnable?.Count ?? 0} disable={b.objectsToDisable?.Count ?? 0}",
                CookingBehavior_ModifyEtcStats b => $" canPocket={b.canPocket} canBackpack={b.canBackpack}",
                _ => "",
            });
        }

        return text.ToString();
    }

    private static string Names(Component[]? components)
    {
        if (components == null)
        {
            return "";
        }

        List<string> names = new();
        foreach (Component component in components)
        {
            names.Add(component == null ? "<null>" : component.GetType().Name);
        }

        return string.Join(", ", names);
    }

    /// <summary>One affliction's type and the numbers on it, or "&lt;none&gt;".</summary>
    private static string Affliction(Peak.Afflictions.Affliction? affliction)
    {
        if (affliction == null)
        {
            return "<none>";
        }

        string detail = affliction switch
        {
            Peak.Afflictions.Affliction_FasterBoi a => $" drowsyOnEnd={a.drowsyOnEnd}",
            Peak.Afflictions.Affliction_InfiniteStamina a => $" climbDelay={a.climbDelay}"
                + $" drowsy={Affliction(a.drowsyAffliction)}",
            Peak.Afflictions.Affliction_AddBonusStamina a => $" stamina={a.staminaAmount}",
            Peak.Afflictions.Affliction_AdjustStatus a => $" {a.statusType}={a.statusAmount}",
            Peak.Afflictions.Affliction_AdjustDrowsyOverTime a => $" perSecond={a.statusPerSecond}",
            Peak.Afflictions.Affliction_AdjustColdOverTime a => $" perSecond={a.statusPerSecond}",
            Peak.Afflictions.Affliction_AdjustStatusOverTime a => $" perSecond={a.statusPerSecond}",
            Peak.Afflictions.Affliction_PoisonOverTime a => $" perSecond={a.statusPerSecond} delay={a.delayBeforeEffect}",
            Peak.Afflictions.Affliction_ClearAllStatus a => $" excludeCurse={a.excludeCurse}",
            Peak.Afflictions.Affliction_HealAll a => $" maxHealing={a.maxHealing}",
            Peak.Afflictions.Affliction_RadiateInfiniteStam a => $" radius={a.radius}",
            Peak.Afflictions.Affliction_MassSuperJump a => $" radius={a.radius}"
                + $" lowGrav={a.lowGravAmount} lowGravTime={a.lowGravTime}",
            Peak.Afflictions.Affliction_LowGravity a => $" lowGrav={a.lowGravAmount}",
            _ => "",
        };

        return $"{affliction.GetAfflictionType()}(totalTime={affliction.totalTime}{detail})";
    }

    private static string Afflictions(Peak.Afflictions.Affliction[]? afflictions)
    {
        if (afflictions == null)
        {
            return "";
        }

        List<string> parts = new();
        foreach (Peak.Afflictions.Affliction affliction in afflictions)
        {
            parts.Add(Affliction(affliction));
        }

        return string.Join(", ", parts);
    }

    /// <summary>A prefab the component instantiates, walked for what it holds.</summary>
    private static string Prefab(string label, GameObject? prefab)
    {
        if (prefab == null)
        {
            return $"{label}=<none>";
        }

        StringBuilder tree = new($"{label}={prefab.name}");
        foreach (Component component in prefab.GetComponents(typeof(Component)))
        {
            if (component != null && !(component is Transform))
            {
                tree.Append(" | ").Append(component.GetType().Name).Append(PrefabValues(component));
            }
        }

        Describe(tree, prefab.transform, MaxPrefabDepth);
        return tree.ToString();
    }

    /// <summary>
    /// The status field a lit lantern or candle switches on, found the way DescribeLantern
    /// finds it - so the dump shows exactly what that reader would see.
    /// </summary>
    private static string Field(GameObject item)
    {
        StatusField? field = item.GetComponentInChildren<StatusField>(true);
        return field == null ? " field=<none>" : $" field={field.name}" + PrefabValues(field);
    }

    /// <summary>
    /// What this run dealt the berry in hand, and the whole table behind it.
    ///
    /// A Shroomberry's effect and its stamina are both decided once at level generation and
    /// held in MushroomManager, not rolled when you eat one. Printing the slot this berry
    /// reads plus the full table is what makes a claim like "no stamina arrives" checkable:
    /// a berry dealt 0 and a berry that is broken look identical from the bar alone.
    /// </summary>
    private static string Rolls(Action_RandomMushroomEffect effect)
    {
        MushroomManager? manager = MushroomManager.instance;
        if (manager == null || manager.mushroomEffects == null || manager.mushroomEffects.Length == 0)
        {
            return " <no MushroomManager>";
        }

        int index = effect.mushroomTypeIndex % manager.mushroomEffects.Length;
        string stam = manager.mushroomStamAmt != null && index < manager.mushroomStamAmt.Length
            ? (manager.mushroomStamAmt[index] * 5).ToString()
            : "?";

        return $" slot={index} effect={manager.mushroomEffects[index]} stamina={stam}"
            + $" minGood={manager.minGoodEffects} minBad={manager.minBadEffects}"
            + $" effects=[{string.Join(", ", manager.mushroomEffects)}]"
            + $" stamAmts=[{string.Join(", ", manager.mushroomStamAmt ?? new int[0])}]";
    }

    /// <summary>
    /// What a Magic Bean grows into, and why its height is not simply maxLength.
    ///
    /// MagicBeanVine.Grow assigns <c>localScale = (w, currentLength, w)</c>, so maxLength is a
    /// **scale factor on Y**, not a distance. The vine's real height is that scale times the
    /// mesh's own height times whatever the ancestors are scaled by - the same shape as the
    /// rope, where reading the field alone was wrong three times running.
    /// </summary>
    private static string Beanstalk(MagicBean bean)
    {
        if (bean.plantPrefab == null)
        {
            return " plantPrefab=<none>";
        }

        MagicBeanVine vine = bean.plantPrefab;
        StringBuilder text = new($" maxLength={vine.maxLength} maxWidth={vine.maxWidth}"
            + $" plantScale={vine.transform.localScale}");

        Transform? origin = vine.vineOriginTransform;
        if (origin == null)
        {
            text.Append(" vineOrigin=<none>");
            return text.ToString();
        }

        text.Append($" originScale={origin.localScale} originLossy={origin.lossyScale}");

        MeshFilter? filter = origin.GetComponentInChildren<MeshFilter>(true);
        if (filter != null && filter.sharedMesh != null)
        {
            Vector3 size = filter.sharedMesh.bounds.size;
            text.Append($" mesh={filter.name} meshSize={size}")
                .Append($" heightIfUnitMesh={vine.maxLength * size.y}u");
        }
        else
        {
            text.Append(" mesh=<none>");
        }

        return text.ToString();
    }

    /// <summary>The rope prefab's own segment spacing, for checking the maths against a ping.</summary>
    private static float RopeSpacingOf(RopeShooter shooter)
    {
        Rope? rope = RopeOf(shooter);
        return rope != null ? rope.spacing : 0f;
    }

    /// <summary>Spacing after the segment prefab's Y scale, which is the real gap.</summary>
    private static float RopeSegmentLengthOf(RopeShooter shooter)
    {
        Rope? rope = RopeOf(shooter);
        if (rope == null)
        {
            return 0f;
        }

        return rope.spacing * (rope.ropeSegmentPrefab == null
            ? 1f
            : rope.ropeSegmentPrefab.transform.localScale.y);
    }

    /// <summary>
    /// The Rope the cannon's anchor spawns, two prefab hops away.
    /// </summary>
    private static Rope? RopeOf(RopeShooter shooter)
    {
        RopeAnchorWithRope? anchor = shooter.ropeAnchorWithRopePref == null
            ? null
            : shooter.ropeAnchorWithRopePref.GetComponent<RopeAnchorWithRope>();
        return anchor == null || anchor.ropePrefab == null
            ? null
            : anchor.ropePrefab.GetComponent<Rope>();
    }

    /// <summary>
    /// Scales along the chain that turns `spacing` into a real distance.
    ///
    /// `spacing` is written into a ConfigurableJoint as `connectedAnchor`, which is measured
    /// in the connected body's **local** space - so anything scaled below one along the way
    /// shortens every gap, and the rope with it. 30 segments at 0.75 should span 22.5 units;
    /// pinging one end from the other measures about 12, and a scale near 0.53 is what would
    /// account for the difference.
    /// </summary>
    private static string RopeScales(RopeShooter shooter)
    {
        Rope? rope = RopeOf(shooter);
        if (rope == null)
        {
            return " ropePrefab=<none>";
        }

        string segment = rope.ropeSegmentPrefab == null
            ? "<none>"
            : rope.ropeSegmentPrefab.transform.localScale.ToString();

        return $" ropeScale={rope.transform.localScale} segmentScale={segment}"
            + $" maxSegments={Rope.MaxSegments}" + SegmentJoint(rope);
    }

    /// <summary>
    /// The joint that actually decides how far apart two rope segments sit.
    ///
    /// Rope writes <c>connectedAnchor = (0, -spacing, 0)</c>, but that is only the rest
    /// position and only if the joint is not auto-configuring its own anchor - and a
    /// ConfigurableJoint with a linear limit will stretch past it under the weight of the
    /// segments below. 30 segments at the computed rest gap should span 7.9 units; measuring
    /// from the anchor straight down gives 12 to 14, so something here is giving.
    /// </summary>
    private static string SegmentJoint(Rope rope)
    {
        if (rope.ropeSegmentPrefab == null)
        {
            return " segmentPrefab=<none>";
        }

        StringBuilder joint = new($"\n[item]     segment={rope.ropeSegmentPrefab.name}");
        foreach (Component component in rope.ropeSegmentPrefab.GetComponents(typeof(Component)))
        {
            if (component == null || component is Transform)
            {
                continue;
            }

            joint.Append(" | ").Append(component.GetType().Name);
            if (component is ConfigurableJoint cj)
            {
                joint.Append($" anchor={cj.anchor} connectedAnchor={cj.connectedAnchor}")
                    .Append($" autoConfigure={cj.autoConfigureConnectedAnchor}")
                    .Append($" motion=({cj.xMotion},{cj.yMotion},{cj.zMotion})")
                    .Append($" linearLimit={cj.linearLimit.limit}")
                    .Append($" spring={cj.linearLimitSpring.spring}/{cj.linearLimitSpring.damper}")
                    .Append($" mass={(cj.GetComponent<Rigidbody>() == null ? -1f : cj.GetComponent<Rigidbody>().mass)}");
            }
            else if (component is Rigidbody rb)
            {
                joint.Append($" mass={rb.mass} drag={rb.linearDamping} useGravity={rb.useGravity}");
            }
        }

        Describe(joint, rope.ropeSegmentPrefab.transform, 2);
        return joint.ToString();
    }

    /// <summary>
    /// What an Action_Spawn puts into the world.
    ///
    /// Sunscreen carries nothing but Action_ReduceUses and Action_Spawn - the protection, its
    /// duration and the cloud's lifetime are all on the thing it sprays - so a component list
    /// of the item alone explains none of it.
    /// </summary>
    private static string Spawns(Action_Spawn action)
    {
        if (action.objectToSpawn == null)
        {
            return " spawns=<none>";
        }

        StringBuilder tree = new($" spawns={action.objectToSpawn.name}");
        foreach (Component component in action.objectToSpawn.GetComponents(typeof(Component)))
        {
            if (component != null && !(component is Transform))
            {
                tree.Append(" | ").Append(component.GetType().Name).Append(PrefabValues(component));
            }
        }

        Describe(tree, action.objectToSpawn.transform, MaxPrefabDepth);
        return tree.ToString();
    }

    /// <summary>
    /// The subtree a breakable item turns into, with the numbers on it.
    ///
    /// A component list alone cannot explain a Remedy Fungus, because everything it does
    /// lives on the prefab it leaves behind rather than on the item you are holding - and
    /// that subtree is reached by four hardcoded child names, any of which goes null the day
    /// the game renames one. Printing the real tree is how those names get corrected.
    /// </summary>
    private static string BreaksInto(ShelfShroom shroom)
    {
        if (shroom.instantiateOnBreak == null)
        {
            return " breaksInto=<none>";
        }

        StringBuilder tree = new($" breaksInto={shroom.instantiateOnBreak.name}");
        Describe(tree, shroom.instantiateOnBreak.transform, MaxPrefabDepth);
        return tree.ToString();
    }

    /// <summary>How far down a break-prefab to walk before the particle systems take over.</summary>
    private const int MaxPrefabDepth = 4;

    /// <summary>
    /// Walks a prefab subtree, naming each child, **every** component on it, and the figures
    /// on the ones that describe an effect.
    ///
    /// Listing every component is the point. The first version of this printed only AOE,
    /// TimeEvent and RemoveAfterSeconds, which hid the very thing it was written to find: a
    /// Remedy Fungus heals over time from a lingering field, and the child holding it looked
    /// empty because a StatusField is none of those three.
    /// </summary>
    private static void Describe(StringBuilder tree, Transform parent, int depth)
    {
        if (depth == 0)
        {
            return;
        }

        foreach (Transform child in parent)
        {
            tree.Append("\n[item]     ").Append(new string(' ', (MaxPrefabDepth - depth) * 2))
                .Append(child.name);

            foreach (Component component in child.GetComponents(typeof(Component)))
            {
                if (component == null || component is Transform)
                {
                    continue;
                }

                tree.Append(" | ").Append(component.GetType().Name).Append(PrefabValues(component));
            }

            Describe(tree, child, depth - 1);
        }
    }

    /// <summary>The fields worth seeing on a component inside a spawned prefab.</summary>
    private static string PrefabValues(Component component) => component switch
    {
        AOE a => $" {a.statusType}={a.statusAmount} range={a.range} minFactor={a.minFactor}"
            + (a.hasAffliction && a.affliction != null
                ? $" affliction={a.affliction.GetAfflictionType()} totalTime={a.affliction.totalTime}"
                : "")
            + $" factorPow={a.factorPow} ignoreFactor={a.ignoreFactor}"
            + (string.IsNullOrEmpty(a.illegalStatus) ? "" : $" illegalStatus={a.illegalStatus}")
            + (a.cooksItems ? " cooksItems" : "")
            + (a.addtlStatus != null && a.addtlStatus.Length > 0
                ? $" addtl=[{string.Join(", ", a.addtlStatus)}]"
                    + $" overrides=[{string.Join(", ", a.addlStatusAmountOverrides ?? new List<float>())}]"
                : ""),
        Peak.StatusFieldBase f => $" {f.statusType}={f.statusAmountPerSecond}/s"
            + $" onEntry={f.statusAmountOnEntry} delay={f.delayBeforeStatusOverTime}"
            + $" addtl=[{DescribeAdditional(f)}]"
            + (f is StatusField s ? $" radius={s.radius} tickBased={s.tickBased}"
                + $" timeBetweenTicks={s.timeBetweenTicks}" : ""),
        TimeEvent t => $" rate={t.rate} repeating={t.repeating}",
        RemoveAfterSeconds r => $" seconds={r.seconds}",
        Campfire c => $" burnsFor={c.burnsFor} moraleRadius={c.moraleBoostRadius}",
        _ => "",
    };

    private static string DescribeAdditional(Peak.StatusFieldBase field)
    {
        if (field.additionalStatuses == null)
        {
            return "";
        }

        List<string> parts = new();
        foreach (Peak.StatusFieldBase.StatusFieldStatus status in field.additionalStatuses)
        {
            parts.Add($"{status.statusType}={status.statusAmountPerSecond}/s");
        }

        return string.Join(", ", parts);
    }
}
