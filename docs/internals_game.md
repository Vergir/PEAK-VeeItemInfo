# PEAK, as the overlay reads it

What the mod knows about the game and how each fact is established. Everything here is
something the code depends on: a formula it models, a field it reads, a trap in the way the
game stores a value. It is not a wiki. Where the wiki and the game disagree, this says what
the game does and how that was checked.

Every fact holds on the current game build. When a game update changes one, the fact changes
here.

Companion documents: [internals_infra.md](internals_infra.md) is how the mod itself is put
together; [design.md](design.md) is what the overlay chooses to say and why.

---

## Reading a value out of the game

Decompiled code is easy to misread. These are the rules for turning it into a fact.

**A field initialiser is not the value on the prefab.** `public float bonusStamina = 0.5f` is
only a fallback for a prefab that does not serialize the field. The Ritual Dagger's class
declares `0.5f` and `10f`; the prefab ships `1` and `0`, and the game gives +100 stamina and
no infinite stamina at all. Never quote a default as the answer - add the field to the `[item]`
dump and read it off a held item.

**A serialized field can exist and be ignored.** `StatusFieldStatus.statusAmountPerSecond` is
in the inspector on every lantern, and `StatusFieldBase.IncreaseStatus` never reads it: it
passes the *main* field's amount to every additional status. Read the method that consumes a
field before believing the field.

**A `const` is inlined at the call site.** Writing `CharacterAfflictions.STATUS_INCREMENT`
bakes `0.025` into the mod's own assembly, so a patch that changed it would leave a shipped
build saying the old thing. `FieldInfo.GetRawConstantValue` reads the literal out of the
loaded assembly's metadata and does follow a patch; `GameValues` does this for the status step
and the cooking maximum, naming each through `nameof` so a rename still breaks the build.

**A prefab asset is never active.** Every prefab the mod walks - a break spawn, an explosion,
a constructed stovetop - lives in the asset database rather than a scene, so nothing in it is
active in any hierarchy. The no-argument `GetComponentInParent<T>()` and
`GetComponentsInChildren<T>()` skip inactive objects and quietly return nothing.
`includeInactive` is mandatory.

**The component list is not the whole behaviour.** First Aid Kit, Antidote and Medicinal Root
carry nothing about spores and cure spores anyway, because
`CharacterAfflictions.SubtractStatus` recurses into Spores whenever Poison is reduced on
purpose. `CharacterAfflictions` layers rules on top of what the prefab carries; grep there
before declaring an item does not do something.

**Read the whole method.** The first half of `GenerateEffectList` shuffles Shroomberry effects
every run; the second half spends its good and bad quotas on the first slots in order, so a
berry's valence is stable across runs.

**Ask for the component, not the name.** A name goes stale silently: `Transform.Find` returns
null straight into a `GetComponent`, and the first sign is a line quietly missing.
`Constructable` asks whether the thing it builds has a `Campfire`; `Action_Spawn` walks the
spawned prefab for what it holds; the lantern reader walks for a `StatusField`; the anti-rope
cannon is the one carrying `Antigrav` and the anti-rope spool says `isAntiRope` itself. The
one exception is a mushroom's poisonous twin, found by shared *display name*, because the
game itself names both "Bugle Shroom" - there the name is the game's own key.

**A list the game also keeps is a list to read, not to copy.** What the healing amulet treats
is `Affliction_HealAll.statusesToHeal`; what a clear-all removes is whatever `StatusIsCurable`
says. Both are read, so a status becoming curable is followed rather than remembered.
`Assembly-CSharp` is publicized at build time, which is what makes the private ones readable
and what lets `nameof(CharacterItems.Update)` name a private Unity message as a patch target.

**Verify against the game, not just the source.** The assembly contains code no live item
reaches - Scout's Tenacity carries a pocket behaviour that would heal gradually while carried,
and the amulet works instantly through `Peak.Action_HealingGem`. A component's presence is
not proof that it runs. The wiki (`peak.wiki.gg`) is a better guide to *how a thing behaves*
than a single class, even where its numbers are loose. Read code to find which fields hold the
values; trust testing for which code path actually runs.

---

## Status arithmetic

**Every status is a 0-1 fraction**, and `AddStatus(STATUSTYPE, float)` takes one. The overlay
multiplies by the configured status scale (100 by default) to display.

**The step.** `CharacterAfflictions.STATUS_INCREMENT` is `0.025`, the smallest change the bars
record. It is the same number behind the weight per carry unit and the status per thorn
increment.

**The game floors, it does not round.** Both `AddStatus` and `SubtractStatus` bank the amount
and pay out `FloorToInt(banked / STATUS_INCREMENT)` whole steps, **discarding the remainder**
each time they pay:

```
currentDecrementalStatuses[t] += amount;
if (acc >= 0.025) { currentStatuses[t] -= floor(acc / 0.025) * 0.025; acc = 0; }
```

Anything modelling what a player receives has to floor - and floor the *magnitude*, because
healing arrives negative and flooring that directly rounds away from zero, turning a 17.5 heal
into 20.

**So a repeating effect is not `amount / period`.** Remedy Fungus hands over `0.015` every half
second. That is under the step, so nothing lands on the first tick and `0.025` comes off on the
second: one step per second, 2.5 display units, against a raw 3. A repeating effect is worth
`0.025 / (ticks-to-reach-a-step x period)`; `ItemDescriptionBuilder.TickedRate` models it.

**The last payout never lands.** Payouts fall at one step, two steps, and so on, while
`RemoveAfterSeconds` destroys the spawn at the duration itself, so a payout on that boundary
never happens. A 15 second fungus field pays 14 times, which is what in-game testing shows.
`TickedTotal` counts payouts rather than multiplying out.

**Petrify is whole points, not a fraction.** `CharacterData.petrifyAmount` is an `int`, and
every route into it - `AddStatus`, `SetStatus`, `SubtractStatus` - runs
`Mathf.FloorToInt(amount * 100f)` before calling `AddPetrify(int)`. A `petrifyPerUse` of
`0.075` is **7**. Floor to whole points on the game's 0-100 scale first, then put the result on
the display scale. Getting the fraction and the points backwards is a 100x error:
`VFX_ExplosionGhost` carries `Petrify = 20` in points, which flooring against the step would
read as eight hundred.

**Weight.** `UpdateWeight` sums `Item.CarryWeight` across the inventory and does
`SetStatus(Weight, STATUS_INCREMENT * total)`, so weight is a status fraction like any other.
`CarryWeight` is a property and must not be reimplemented: it returns **0** when the ascent's
item-weight modifier is `-1`, not one less.

**Thorns.** `UpdateWeight` sets Thorns to one step per *increment*, and a thorn is not one
increment: `GetTotalThornStatusIncrements` sums `ThornOnMe.GetThornDamage()`, which is the
thorn's `thornDamage` scaled by `Ascents.etcDamageMultiplier`. At ascent zero that is two
increments per thorn: a Prickleberry's two thorns read 10, not 5. The thorns belong to the
character, a pool of `ThornOnMe` objects under the ragdoll in
`refs.afflictions.physicalThorns`, so the figure is read off one of them (arrows share the
pool and are skipped by `isThorn`). Two custom-run switches make the answer **zero**, and zero
is a real answer: `Hazard_Thorns` off makes `AddThorn` return before doing anything, and
`EtcDamage` at zero makes the multiplier zero. With no character to read, the fallback is the
measured two increments.

The Cactus is different: `StickyItemComponent.addThornsToStuckPlayer` is added straight to the
*increment* count in `UpdateWeight`, so it is worth one step per unit, not two.

**A poison cure is a spores cure.** In `SubtractStatus`:

```
if (statusType == Poison && !decreasedNaturally && character.IsLocal)
    SubtractStatus(Spores, amount);
```

Every deliberate poison reduction takes the same amount off Spores. One-way - adding poison
adds no spores - and not applied to the passive per-second decay. A lantern that cures poison
cures spores at the same rate, and so does a healing blast, because `AOE.Explode` goes through
`AdjustStatus`.

**`statusSum` sums every status including Weight**, and weight comes from the whole
inventory, so any formula using it depends on what the player is carrying. The Scorpion's is
the one the overlay shows: `InflictAttack` does an instant `AddStatus(Poison, 0.025)` and then
a poison-over-time totalling `max(0.5, (1 - statusSum) + 0.05)` - 50 at full status, 105 at
none, more damage the healthier you are. Both bounds are literals in the method body;
`totalPoisonTime` beside them is a field and is read. The poison is shown even when the
scorpion is dead, because mob state does not update on equip.

**Afflictions over time are exactly rate times time.** `Affliction_AdjustDrowsyOverTime` and
`Affliction_AdjustColdOverTime` apply `statusPerSecond * deltaTime` every frame, so the total
is `statusPerSecond * totalTime`. The Heat Pack's total is 2160 on a scale that stops at 100,
which is why the overlay states some effects as a rate instead - see [design.md](design.md).

---

## Clearing all status

There are **two clear-alls, and they do not ask the same question.**

`CharacterAfflictions.ClearAllStatus` - the Ritual Dagger's feed effect, and
`Affliction_ClearAllStatus` - keeps no list. It walks every `STATUSTYPE` and asks
`StatusIsCurable`, which refuses Weight, Thorns and Arrow outright and defers Curse and
Petrify to its callers.

`Action_ClearAllStatus` - the component on **Napberry** and the **Book of Bones**, and nothing
else - asks nothing. It keeps a private `defaultExclusions` of Weight, Petrify, Arrow and
Thorns, plus a per-item `otherExclusions`, and calls `SubtractStatus` directly.

The two rules agree on every status a live item can carry, so one derived list serves both.

**Cure-All is not a clear-all.** It carries `Action_AddOrRemoveThorns` with a count of **-5**,
which genuinely removes five thorns and is easily mistaken for clearing everything. Pandora's
Lunchbox and the Blowgun dart are not clear-alls either.

---

## Blasts, fields and emitters

### `AOE.statusAmount` is a figure nobody is ever given

Two things stand between it and the number on the bar. **Distance:** `AOE.Explode` scales
every amount by `GetFactor(dist) = (1 - dist/range)^factorPow`, and `dist` is measured to
`character.Center` - the torso, not the feet - so the factor cannot reach 1 wherever you
stand. **The step:** whatever survives is floored as above. An AOE flagged `ignoreFactor` hands
over its full amount (`GetFactor` returns 1); one whose factor falls below its own `minFactor`
gives nothing at all, because `Explode` skips that character rather than handing them a
reduced share. A `range` of zero means `Explode` returns before doing anything.

### An AOE can fire twice on spawn

`Start` calls `Explode` when `auto` is set and `OnEnable` calls it again when `onEnable` is,
and a prefab can set both. The Snowball's impact does, and delivers its cold twice: 10 on the
bar from an amount of 5. Each firing is floored on its own - two payouts of 0.05, not one of
0.124. Every other blast is `auto` alone. Zero firings means the AOE only ever goes off from a
`TimeEvent`.

### The point-blank distance is 0.64 units, and it is empirical

How far your own blast goes off from the chest it is measured to. No field backs it up; four
in-game readings bracket it. Because the game floors, each reading says the delivered amount
fell in a whole-step window - a range of distances rather than a point:

```
                     range  advertised  measured  steps       gives
  Remedy Fungus        5        20        17.5     7 of  8   d <= 1.172
  Portable Stovetop    3        20        17.5     7 of  8   d <= 0.703  <- ceiling
  Faerie Lantern       3        25        20.0     8 of 10   d >  0.570  <- floor
  Dynamite            12        30        27.5    11 of 12   d <= 1.917

  intersection: 0.570 < d <= 0.703, and 0.64 sits in the middle of it
```

**A measurement is only as sharp as its step count, and only bounds from below if it falls two
steps short.** A reading one step below a blast's maximum bounds nothing from below, because
the upper half of that window is `factor < 1`, which every distance satisfies. The Faerie
Lantern is the only reading that lands *two* steps short of its advertised amount, and the
only one that gives a floor. When another figure needs pinning down, look for many steps, a
small radius, and a reading well clear of the maximum. `AOE_Cold`, with a radius of 2 and
three steps, can only ever say `d <= 1.11`.

**It is not simply chest height.** An item that goes off in your hand explodes at hand height,
and three of the four readings are held explosions, which is why the bracket sits well under
the metre and a half a torso stands at. One constant covers both cases because nothing yet
distinguishes a held blast from one at your feet, and no measurement separates them.

**It is readable in principle and left a constant on purpose.** `Character.Center` is the torso
bodypart's position - a live ragdoll position that moves as you crouch and climb. Sampling it
would make a number drift while you hold an item, which nothing else in the overlay does.

### A distance transfers between blasts; a factor does not

A factor belongs to one blast. Remedy Fungus is `range = 5` and dynamite is `range = 12`, both
`factorPow = 0.5`, so one standing position gives 0.90 at one and 0.96 at the other - a full
status step apart. A distance is a property of the character, so it does transfer; `Blast`
holds the distance and derives each AOE's factor from its own `range`, `factorPow`,
`minFactor` and `ignoreFactor`. Only the one-off half of a blast needs the distance at all:
any factor between 0.5 and 1 lands a repeating 0.015-a-tick blast on the same one step per
second.

### A repeating `TimeEvent` turns a burst into a field

An AOE with a repeating `TimeEvent` on the same object re-fires every `rate` seconds for as
long as the spawn lives, and the lifetime is the nearest `RemoveAfterSeconds` at or above it.
Remedy Fungus is both: one blast that heals as it goes off, and two AOEs re-firing every half
second. Zero lifetime reads as "no duration to state".

### `StatusField` - lanterns and candles

A lit lantern or candle keeps a `StatusField` switched on: a radius, a main
`statusAmountPerSecond`, and `additionalStatuses` that all move at the **main** rate (their own
per-second fields are ignored, see above). `tickBased` changes nothing: it applies
`statusAmountPerSecond * timeBetweenTicks` once per tick, the same rate. A field naming the
same status twice moves it twice as fast. `IncreaseStatus` goes through `AdjustStatus`, which
sends anything negative to `SubtractStatus`, so the spores coupling applies. The Candle removes
drowsiness within its reach exactly as a lantern warms.

### `StatusEmitter` - the stovetop

`Update` hands `amount x tickTime` (tick 0.5s) to Add/SubtractStatus every tick, so `amount`
is a per-second rate, but a banked one paid out in whole steps - the rate a player measures is
`TickedRate(amount x tickTime, tickTime)`. The inner fade scales an *added* status down towards
`minAmount` by distance; a removed one is never faded, and the campfire removes.

**A built prefab carries more emitters than it uses.** The stovetop has three: the warmth (Cold
-0.05/s, radius 2); a Hot emitter with a radius of half a unit around the flame itself, which
no standing character's chest can enter; and a HealRadius whose object ships inactive and is
never switched on - the stove does not heal. Only the first is described. Ancestors of an
emitter are switched on by the thing that builds it (`EnableWhenLit` by `Campfire`), so the
walk includes inactive objects but skips an emitter whose *own* object is off.

### Cooking explosions

`RPC_CookingExplode` explicitly handles the local character still holding the item, so a
cooking explosion going off point-blank is the ordinary case: a stovetop advertising 20 injury
gives 17.5. The blast radius is `AOE.range`, not a collider. Where the prefab holds no AOE that
affects a character but does hold a `StatusField`, cooking makes a field you stand in rather
than a blast; where it holds neither, the "explosion" is a puff with nothing in it and the
item is simply gone (a balloon pops, a snowball melts).

---

## Units and distances

**1 Unity unit is `CharacterStats.unitsToMeters` metres** - 1.6, public and static, the same
figure that turns hip height into the altitude on the end screen. Every radius, range and
raycast length in the game is in Unity units and needs it; printing one with an "m" after it
understates the distance by well over a third.

**A number is not in the unit its name implies until you have read what consumes it.** Five
fields whose names mislead:

| Field | Looks like | Actually is |
|---|---|---|
| `Rope.spacing` | the gap between segments | one half of a joint offset, in **local** space |
| `ConfigurableJoint.anchor` | ignorable | the **other half** of that gap |
| `MagicBeanVine.maxLength` | a length | assigned to `localScale.y` - a **scale** |
| `Rope.GetLengthInMeters` | metres | the rope's length in **units**, near enough |
| `RopeShooter.length` | a length | a **segment count** |

Before using a field as a distance, find its consumer. `Find All References` on the decompiled
type takes a minute.

**A local-space offset is scaled by its transform.** A joint anchor, a child position, a
`connectedAnchor` - all shrink with the object.

### Rope geometry

A `ConfigurableJoint` pins a point on its own body to a point on the connected one, so the gap
between two rope segments is **both** offsets added, not either alone: `anchor.y` (0.5, set on
the segment prefab) plus `Rope.spacing` (0.75, written as `connectedAnchor = (0, -spacing, 0)`),
both local-space and so both scaled by the segment prefab's Y scale of 0.35. `(0.5 + 0.75) x
0.35 = 0.4375` a segment; 30 segments span 13.1 units, 21 metres. Measuring from the anchor
straight down gives 12 to 14 units and climbing one with a height-tracking mod reads about 20
metres. Spacing alone would say 22.5 units, `connectedAnchor` scaled alone 7.9. The joint is
Locked on every axis with a zero linear limit, so nothing stretches - the rope is rigid and the
figure is derivable.

**`Rope.GetLengthInMeters` is not a distance.** It returns `segments * 0.25`; its only caller
is the rope-placed achievement counter, and it is what the spool's own UI shows. A Rope
Cannon's rope and a spool rope of the same 30 segments hang side by side at the same length
and the game calls that 7.5m - almost exactly the rope's length in *units*, which is how the
wiki came to publish 7.5m. The overlay matches it by default so the two never disagree in
front of a player; a setting switches to the derived figure.

**The Rope Cannon has two distances.** How far it shoots is `maxLength`, a raycast in Unity
units; how much rope that leaves is `length`, a segment count. They happen to be 30 and 30.

### Chain Launcher and Magic Bean

`VineShooter.maxLength` is the raycast the Chain Launcher fires along, in Unity units: about 48
units, 77 metres.

`MagicBeanVine.maxLength` is written into the vine's `localScale.y` as it grows, so it reads
like a scale - but scaling the stalk mesh by it gives 94 units, which is nonsense: the mesh's
long axis is Z, not Y, so its bounds are not the height of the thing being scaled. Taken as
plain Unity units it gives 32m, and a height-tracking mod reads 28m of gain on a vine that grew
skewed, so the real length is at least that. That also matches how every other range in the
game is authored.

---

## Cooking

**Every live item has an `ItemCooking`.** `Item.Awake` does `GetOrAddComponent<ItemCooking>`,
which is `GetComponent` - the first one - so a prefab without one (most foods) behaves as the
default: cookable, no behaviours, the plain stat ladder. The Infinite Rescue Claw ships two, a
cannot-be-cooked one first and a wreck-on-cook one second; the game reads the first and the
item cannot be cooked. A disabled `ItemCooking` is a cooked-off script and says nothing.

**The stage** is `max(timesCookedLocal, preCooked)`. `timesCookedLocal` is set from the item's
data on a live instance and already folds `preCooked` in; on a prefab it is still zero, so
`preCooked` is the floor. Cooked Bird ships at stage 1. `ItemCooking.COOKING_MAX` (12) is the
ceiling and is read at runtime because it is a `const`.

**The stat ladder in `ChangeStatsCooked` is not linear.** Reaching stage 1 doubles hunger
restoration and multiplies bonus stamina - or hands out ten where there was none, by adding an
`Action_GiveExtraStamina` to *anything*, amulets included. Reaching stage 2 changes
**nothing**. Stage 3 and beyond burn it: 5 off hunger per stage, stamina zeroed, and poison
added from stage 4. It rewrites `Action_RestoreHunger.restorationAmount` and
`Action_GiveExtraStamina.amount` in place, so a cooked item's numbers are already right
everywhere the overlay reads them.

**Cooking switches actions off rather than removing them.** `CookingBehavior_DisableScripts`
sets `enabled = false`, so reading a disabled component would make a cooked poisonous berry
advertise poison it no longer inflicts. Skip disabled components everywhere.

**The behaviours**, each with `cookedAmountToTrigger` and `onlyOnce`:

- `Explode` - spawns `explosionPrefab`; see cooking explosions above. `dontRunIfOutOfFuel`.
- `Wreck` and `wreckWhenCooked` - the item is destroyed the first time and cannot be cooked
  again.
- `ReplaceItem` - the item becomes `replaceWithItem`; a cooked Frog becomes FrogLegs.
- `EnableScripts` - switches actions on: the Sports Drink's second stamina action, Mandrake's
  curse cure.
- `ChangeAfflictionTime` - lengthens an affliction: Fortified Milk's ten more seconds of shield.
- `DisableScripts` - on every food it comes with the ladder's gain anyway; on the Blowgun, the
  one item where it stands alone, the prefab's replacement is a null.
- `AdjustStatusInstantly` - applies to **the player holding the item at the moment of
  cooking**, not to the item, which is why cooking a Shroomberry does not remove its `+5
  Spores` line.
- `AddPoisonOnUse`, `MessUpAudio`, `ModifyBugleWobble`, `ModifyAudioSourcePitch`,
  `EnableDisableObjects`, `ModifyEtcStats` - cosmetic or unread.

`ignoreDefaultCookBehavior` opts an item out of the ladder entirely.

**An action flagged `OnConsumed` fires when the item is consumed - which is not the same as
being eaten.** `Item.ConsumeDelayed` runs the whole `OnConsumed` chain, and six things call it:
`Action_Consume` and `Action_ConsumeAndSpawn`; `Action_SpawnGuidebookPage`, so the Scroll
consumes itself when it spawns its page; `ScoutStatue.Interact_CastFinished`, which consumes an
amulet as it is inserted; `RitualDaggerFeedBehavior.FeederAction`; and `Action_ReduceUses` where
`consumeOnFullyUsed` is set, on the last use.

This matters because the ladder hands an `OnConsumed` stamina action to **anything**, so an item
read as un-consumable hides a real +10. Which items it actually reaches is narrow: the ladder
needs `canBeCooked`, no wreck, no cooking explosion and `ignoreDefaultCookBehavior` off, and an
item that already carries a non-`OnConsumed` hunger or stamina action shows the gain anyway. What
survives is the **Scroll** and the **Strange Gem** - the one cookable amulet, the other four
carrying `canBeCooked=False`. The Ritual Dagger qualifies too and is left out on purpose; see
[design.md](design.md), "What is deliberately not shown". The remaining candidates - Warp
Compass, Cheat Compass, the auto-parachute - are dead content.

`Action_Guidebook` only toggles the guidebook UI, so the fifteen guidebook pages are not
consumed; only the Scroll's `Action_SpawnGuidebookPage` is. The Book of Bones carries
`consumeOnFullyUsed=False` and is never consumed at all.

---

## Actions and hooks

**`ItemAction.Subscribe` appends to a delegate and `OnEnable` runs down the component list**,
so for two actions on one status the prefab's component order is the sequence the game runs
them in - and with a status clamped at zero, the sequence changes the answer. The Book of Bones
carries `Curse +50` then `Curse -25` and nets +25 from any starting point. As a general rule
component order is arbitrary (the Cactus keeps its `CactusBall` behind a `Rigidbody`).

**`Action_ModifyStatus.ifSkeleton` gates a whole change.** `RunAction` returns before doing
anything unless the character is a skeleton. The Book of Bones is the twist: its
`Action_BecomeSkeleton` runs first - one line flipping `data.isSkeleton` - so its gated curse
lands on a *human* (who is a skeleton by the time the curse is checked) and not on a skeleton
(who has just been turned back). The gate is open when the character's state and the item's
toggle disagree. Fortified Milk's skeleton-only injury cure has no toggle and simply waits for
a skeleton. Any new `Action_*` field that gates `RunAction` wants the same treatment.

**Feeding is a separate hook from using.** `Peak.IExtraFeedBehavior` fires when one player
feeds an item to another and is reachable by no `Action_*`. `RitualDaggerFeedBehavior` is its
only implementor: `RPC_RitualDaggerBuff` runs on every client and skips only the character who
was fed, so everybody else in the lobby - the feeder included - is cleared (curse and petrify
spared), healed and handed stamina. The dagger's `Action_SacrificeFriend` kills whoever it is
*fed to*, never the holder; it carries no `Action_Consume`, so the only path to `RunAction` is
the feed behaviour calling `ConsumeDelayed` once the item has changed hands. Check for new
implementors after a game update.

**`Action_ApplyAffliction` carries a main affliction and `extraAfflictions`.** Only the Cursed
Skull fills the extras. `Action_SuperJumpAmulet` derives from it and its `RunAction` calls
`base.RunAction()` before charging petrify, so it carries a real affliction as well as a cost.
`Action_ApplyMassAffliction` adds a radius and `ignoreCaster` (the Cursed Skull and the Magic
Bugle hand the effect to everyone nearby except you).

**The Magic Bugle re-fires its mass affliction every tenth of a second** for as long as it is
tooted, so the affliction's own half-second is not a duration anybody experiences: the stamina
is infinite while the horn sounds. The Cursed Skull's radius of 900 units means "everyone".

**`Affliction_MassSuperJump` (Scout's Initiative)** does not grant speed. It launches everyone
nearby and drops their gravity; `lowGravAmount` feeds the same float-and-jump formula as the
number of balloons you are holding, so it is a balloon count.

**`Affliction_Sunscreen`**: `AddSunHeat` is skipped for anyone wearing it, so it is immunity to
the *sun* - a campfire will still cook you. The parasol is the other half of that same check.
Everything Sunscreen does lives on the prefab it sprays: the bottle carries only
`Action_ReduceUses` and `Action_Spawn`, and the spawned cloud's AOE is flagged `hasAffliction`
and hands the affliction to whoever it catches, which is where the protection and its duration
actually are. The cloud itself lives four seconds (`RemoveAfterSeconds`).

**`Action_WarpToBiome` does not warp you to a biome.** It teleports you to wherever the thrown
fungus lands.

**`Action_ConsumeAndSpawn`** is a Berrynana being eaten and leaving its own coloured peel
(`itemToSpawn`). Nothing else uses it.

**`Breakable` spawns two kinds of thing**: items (`instantiateOnBreak` - a Coconut's halves, a
nest's egg) and non-item prefabs (`instantiateNonItemOnBreak`), which is where an effect lives
- a Snowball's impact is an AOE of cold. An Antidote shatters into its cloud too, but an
Antidote is for drinking.

**Remedy Fungus (`ShelfShroom`) has no eat-it effect at all.** The only way to use it is to
throw it, and `instantiateOnBreak` is what heals.

**Dynamite costs its holder 0.25 injury before the blast is even spawned.** In
`Dynamite.Update`:

```
if (Character.localCharacter.data.currentItem == item)
    AddStatus(STATUSTYPE.Injury, 0.25f);
```

A stick going off in the hand measures 52.5: a flat 25 for holding it, and 27.5 from a blast
advertising 30. The literal is in a method body with nothing exposing it.

**The Ancient Idol works while merely held.** `BingBongShieldWhileHolding` re-applies a
two-second `Affliction_BingBongShield` every 1.5 seconds for as long as the idol is the current
item, so the shield never lapses.

**The Cactus charges its thorns to whoever it is stuck to - which includes its holder.**
`CharacterData.currentItem`'s setter makes the item in your hand your `currentStickyItem`, so
`UpdateWeight` charges `addThornsToStuckPlayer` while you hold it, not only after someone throws
it at you. `addWeightToStuckPlayer` rides the same path; no item sets it.

**Constructable** builds `constructedPrefab`; its subclasses `ScoutEffigy` and
`CheckpointConstructable` build nothing with a `Campfire`.

**Shroomberries.** Effect and stamina are both decided once at level generation and held in
`MushroomManager`, not rolled when you eat one. `GenerateEffectList` deals the slots in order
and spends its quotas first: the first `minGoodEffects` slots are drawn from `GoodEffects`, the
next `minBadEffects` from `BadEffects`, and only then does it choose freely. So a berry whose
`mushroomTypeIndex` sits in the guaranteed-good range is always good, one in the guaranteed-bad
range always bad, and one past both quotas is a genuine coin flip - the red/yellow-good,
green/blue-bad, purple-either pattern players report. Stamina is dealt as `Random.Range(0, 4)`
per slot and `RunAction` multiplies by `0.05f`, so the span is 0-15 display units. **The wiki's
0-20 is wrong** - that is `Range(0, 4)` read as inclusive; Unity's integer overload is
max-exclusive, confirmed in the IL (`ldc.i4.0; ldc.i4.4; call int32
UnityEngine.Random::Range(int32, int32)`). A 4 in the `stamAmts` table the item dump prints
would disprove it. The stamina does arrive; a roll of 0, a quarter of berries, looks exactly
like it not arriving.

**Other passives, all without a symbol**: `Parasol` (sun protection, slow fall),
`Balloon`/`TiedBalloon`/`CharacterBalloons` (-18% / -54% gravity for two minutes), `Glider`,
`JetpackItem`/`Peak.Jetpack`, `Backpack` slot counts. `Action_Balloon` and `Action_Parasol` are
the *use* halves.

---

## The HUD

Facts the icon and colour scraping depend on.

**Status bars.** Each `BarAffliction` pairs an `Image` icon with its `afflictionType` and
carries three Images: a dark backing on `procedural_ui_image_default_sprite`, and the bright
fill on `DitherStripes` and `UI_Blur_Outlne_Thick`, which always agree. The bright pair is the
status colour. Brightest-wins is wrong for a dark status: Curse is nearly black (`#1B0043`), so
its backing outshines its fill. The petrify bar reports `afflictionType = Injury` while
carrying the petrify icon; key it off `BarAffliction.isPetrify`.

**Extra Stamina has no `BarAffliction`** but does have a bar: the short second stripe whose
fill is `StaminaBar.extraBarStamina`, with `extraStaminaIcon` beside it. Two more indicators
hang off the stamina bar as plain GameObjects, usually inactive: `shield` (invincibility) and
`campfire` (shown while you cannot get hungry, and the campfire artwork the cooking hint
borrows). Both are white silhouettes tinted by their Image, so the tint is the HUD colour.

**The icons are soft because the artwork is soft.** A status icon is pure white in RGB with the
whole shape in its alpha channel, authored for a display far larger than a line of text, and
nearly as many of its pixels are part-transparent as are solid. A copy of the game's own
texture at 1:1, uncropped and unscaled, is exactly as soft as the packed one; atlas resolution
changes nothing. The tell: the text is crisp and the icon beside it is soft, on the same line
at the same size, which rules out render scale, display resolution and canvas scale together.
The textures run to about 500 a side and the sprite rects inside them are neither square nor
whole-numbered - Crab is 295x468, Curse is 435.85x494.85 at a fractional offset.

**Numbness is not a `STATUSTYPE`** and has no bar, so there is nothing to scrape; it is the
one icon the mod ships (`assets/numbness.png`, provenance in `assets/NOTICE.md`).

**Item icons** come from `ItemDatabase.itemLookup` (a loaded ScriptableObject, found through
`Resources`) and `Item.UIData.GetIcon()`, and carry their own colours. `Item.ItemTags.BingBong`
marks the mascot. A `Balloon` component's `isBunch` tells the single balloon from the bunch.

**The HUD object.** `GUIManager.instance` and `GUIManager.hudCanvas` are public fields; the
overlay's font is `heroDayText.font`. Inventory slots are `InventoryItemUI` with `_itemData`
and a `rectTransform`; `temporaryItem` is the extra slot that appears left of the numbered
ones when the inventory is full and you pick something up. `ItemPromptLayout` spans the full
canvas height, so its corners sit at the screen edges.

**Names.** `Item.GetName()` is the localised name in the HUD's upper case (`GRANOLA BAR`);
`UIData.itemName` is the localisation key's source and is a raw key for some items
(`AMULET_CLONE`, `VOIDLAUNCHER`) and lower case for others. Two amulets are spelled with a
typographic apostrophe. `CharacterItems.data.sinceItemAttach` counts up in real time and is
reset to zero on every attach.

---

## Numbers nothing exposes

The hardcoded values, each with the reason it cannot be read. Anything obtainable from the
game is read from the game; these are the remainder.

| Value | Where | Why it is a literal |
|---|---|---|
| Shroomberry stamina `0.05` per roll, three rolls | `ItemDescriptionBuilder.MaxMushroomStamina` | `Random.Range(0, 4)` and `0.05f` are literals in `GenerateEffectList` and `RunAction`; the dealt values in `mushroomStamAmt` are a sample, not the bound |
| Scorpion `0.5` and `1.05` | `DescribeScorpion` | literals inside `InflictAttack`; the instant `0.025` folded in is by value too |
| Point-blank distance `0.64` | `Blast.PointBlankDistance` | empirical, table above; derivable from `Character.Center` but that drifts while held |
| Held dynamite injury `0.25` | `ItemDescriptionBuilder.HeldDynamiteInjury` | literal in `Dynamite.Update` |
| Two increments per thorn | `ThornStatus` fallback | the measurement, used only when no character is there to read a `ThornOnMe` off |
| Colours for Numb, Item, Float | `EffectColors.Colors` | no bar to sample; Numb is taken from the icon's pale stems |
| Cap centre `0.31` em | `StatusIcons.CapCentre` | dialled in against the game; the font's own metrics put it slightly high |

---

## Item index

Where each item appears above.

- **Ancient Idol** - Actions and hooks (held shield).
- **Antidote** - Status arithmetic (spores); Actions and hooks (Breakable).
- **Anti-Rope Cannon / Spool** - Reading a value (component not name).
- **Berrynanas** - Actions and hooks (ConsumeAndSpawn).
- **Blowgun** - Clearing all status (not one); Cooking (DisableScripts alone).
- **Book of Bones** - Clearing all status; Actions and hooks (component order, ifSkeleton).
- **Bugle Shroom / Mushroom Lace** - Reading a value (twins by display name).
- **Cactus** - Status arithmetic (thorn increments); Actions and hooks (sticky while held).
- **Candle** - StatusField.
- **Chain Launcher** - Units and distances.
- **Coconut** - Actions and hooks (Breakable items).
- **Cooked Bird** - Cooking (preCooked).
- **Cure-All** - Clearing all status (not one; -5 thorns).
- **Cursed Skull** - Actions and hooks (extras, ignoreCaster, 900 units).
- **Dynamite** - Blasts (measurement, distance vs factor); Actions and hooks (held 0.25).
- **Energy Drink** - Status arithmetic (drowsy on end, see design.md).
- **Faerie Lantern** - Reading a value (ignored field); Blasts (the floor reading); StatusField.
- **First Aid Kit / Medicinal Root** - Status arithmetic (spores).
- **Fortified Milk** - Actions and hooks (ifSkeleton without toggle); Cooking (ChangeAfflictionTime).
- **Frog** - Cooking (ReplaceItem).
- **Heat Pack** - Status arithmetic (rate x time).
- **Infinite Rescue Claw** - Cooking (two ItemCooking).
- **Magic Bean** - Units and distances.
- **Magic Bugle** - Actions and hooks (re-fires).
- **Mandrake** - Cooking (EnableScripts, curse cure).
- **Napberry** - Clearing all status.
- **Pandora's Lunchbox** - Clearing all status (not one).
- **Portable Stovetop** - Blasts (measurement, three emitters, cooking explosion).
- **Prickleberry** - Status arithmetic (thorns).
- **Remedy Fungus** - Reading a value (includeInactive); Status arithmetic (accumulator, 14 payouts); Blasts (measurement, field); Actions and hooks (thrown only).
- **Ritual Dagger** - Reading a value (field initialiser); Clearing all status; Actions and hooks (feed hook, SacrificeFriend).
- **Rope Cannon / Rope Spool** - Units and distances.
- **Scorpion** - Status arithmetic (statusSum formula).
- **Scout Cookies** - Cooking (ladder on a non-consumable).
- **Scout's Ambition / Tenacity / Initiative** - Reading a value (Tenacity's dead pocket behaviour); Cooking (cooked amulet); Actions and hooks (SuperJumpAmulet, MassSuperJump).
- **Shroomberry** - Reading a value (whole method); Cooking (AdjustStatusInstantly); Actions and hooks (generation, 0-15).
- **Snowball** - Blasts (fires twice); Actions and hooks (Breakable non-item).
- **Sports Drink** - Cooking (EnableScripts).
- **Stone** - ships a null component; see internals_infra.md.
- **Sunscreen** - Actions and hooks.
