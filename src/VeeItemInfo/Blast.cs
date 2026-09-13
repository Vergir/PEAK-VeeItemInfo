using UnityEngine;

namespace VeeItemInfo;

/// <summary>
/// What an explosion actually does to the person who set it off, rather than what its
/// <c>statusAmount</c> says.
///
/// <c>AOE.statusAmount</c> is a figure nobody is ever given. Two things stand between it and
/// the number on your bar, and both take a bite:
///
/// <list type="number">
/// <item><b>Distance.</b> <c>AOE.Explode</c> scales every amount by
/// <c>GetFactor(dist) = (1 - dist/range)^factorPow</c>, and <c>dist</c> is measured to
/// <c>character.Center</c> - your torso, not your feet. So the factor cannot reach 1 wherever
/// you stand.</item>
/// <item><b>The step.</b> Whatever survives is handed to <c>AdjustStatus</c>, which banks it
/// and pays out <c>FloorToInt(banked / STATUS_INCREMENT)</c> whole steps, <b>discarding the
/// remainder</b>. Floor, not round - that is the game's own arithmetic in both
/// <c>AddStatus</c> and <c>SubtractStatus</c>, read from its code rather than inferred.</item>
/// </list>
///
/// <b>The constant here is a distance, not a factor.</b> That is the whole point. A factor
/// belongs to one blast: it is computed from that blast's <c>range</c> and <c>factorPow</c>,
/// and the mod carried a single `0.9` fitted to Remedy Fungus as though it belonged to all of
/// them. It does not. Remedy Fungus has <c>range = 5</c> and dynamite has <c>range = 12</c>,
/// so the same standing position gives 0.9 at one and 0.96 at the other - the flat constant
/// understated dynamite by a full status step.
///
/// A distance is a property of the *character*, so it does transfer. The one number left is
/// how high your torso sits above your feet, and every AOE derives its own factor from it.
/// </summary>
internal static class Blast
{
    /// <summary>
    /// How far your own blast goes off from the chest it is measured to, in Unity units.
    ///
    /// <b>Empirical, and the one number in the mod that no field in the game backs up.</b>
    /// Four in-game measurements bracket it. Because the game floors, each one says the
    /// delivered amount fell in a whole-step window, which is a range of distances rather
    /// than a point:
    ///
    /// <code>
    ///                      range  advertised  measured  steps       gives
    ///   Remedy Fungus        5        20        17.5     7 of  8   d &lt;= 1.172
    ///   Portable Stovetop    3        20        17.5     7 of  8   d &lt;= 0.703  &lt;- ceiling
    ///   Faerie Lantern       3        25        20.0     8 of 10   d &gt;  0.570  &lt;- floor
    ///   Dynamite            12        30        27.5    11 of 12   d &lt;= 1.917
    ///
    ///   intersection: 0.570 &lt; d &lt;= 0.703, and 0.64 sits in the middle of it
    /// </code>
    ///
    /// <b>The Faerie Lantern is the only one that bounds it from below</b>, and the reason is
    /// that it is the only reading that landed *two* steps short of what its blast advertises.
    /// A reading one step short says `amount x factor` fell between n and n+1 steps, and where
    /// n+1 is the blast's own maximum the upper half of that is just `factor &lt; 1`, which every
    /// distance satisfies. Three measurements in a row said nothing but "smaller than".
    ///
    /// It also caught a wrong value: at the previous 0.5 this predicted 22.5 for that lantern
    /// and the game gives 20.
    ///
    /// <b>It is not simply your chest height.</b> An item that goes off in your hand explodes
    /// at hand height, not at your feet, and three of the four measurements above are held
    /// explosions - which is why the bracket sits well under the metre and a half a torso
    /// actually stands at. One constant covers both cases because nothing yet distinguishes a
    /// blast you are holding from one at your feet, and no measurement so far separates them.
    ///
    /// It is readable in principle: <c>Character.Center</c> is the torso bodypart's position.
    /// But that is a live ragdoll position that moves as you crouch and climb, and sampling it
    /// would make the overlay's numbers drift while you hold an item, which nothing else in it
    /// does - so this stays a constant on purpose rather than for want of noticing.
    /// </summary>
    private const float PointBlankDistance = 0.64f;

    /// <summary>
    /// Whether a radius measured from the character's centre can be entered on foot at all.
    /// A stovetop's HotRadius is half a unit - smaller than the gap between your chest and
    /// anything on the ground - so nobody standing is ever inside it, and describing it
    /// would promise heat the fire never gives.
    /// </summary>
    internal static bool Reachable(float radius) => radius > PointBlankDistance;

    /// <summary>
    /// What <paramref name="amount"/> of an AOE's status is actually delivered to somebody
    /// standing on top of it.
    ///
    /// An AOE flagged <c>ignoreFactor</c> hands over its full amount by its own admission -
    /// <c>GetFactor</c> returns 1 outright - so only the step applies there. An AOE whose
    /// factor falls below its own <c>minFactor</c> gives nothing at all: <c>Explode</c> skips
    /// that character entirely rather than handing them a reduced share.
    ///
    /// <b>Not for Petrify.</b> The step assumes <paramref name="amount"/> is a 0-1 fraction
    /// like every other status, and petrify is whole points on the 0-100 scale -
    /// <c>VFX_ExplosionGhost</c> carries <c>Petrify = 20</c>, which flooring against the step
    /// would read as eight hundred of them. Nothing routes a petrify AOE here in 2.1.a; a
    /// caller that starts to needs the scale sorted out rather than a guard bolted on.
    /// </summary>
    internal static float Delivered(AOE aoe, float amount)
    {
        float factor = Factor(aoe);
        if (factor <= 0f)
        {
            return 0f;
        }

        // Floor the magnitude and put the sign back, because the game banks the size of the
        // change and pays out whole steps of it. Healing arrives here negative, and flooring a
        // negative directly would round it *away* from zero - turning a 17.5 heal into 20.
        float scaled = Mathf.Abs(amount) * factor;
        float delivered = Mathf.Floor(scaled * GameValues.StepsPerBar) * GameValues.StatusStep;

        // Once per firing, and floored per firing: the remainder is thrown away each time the
        // game pays out, so two firings of 0.062 are two payouts of 0.05, not one of 0.124.
        return (amount < 0f ? -delivered : delivered) * Firings(aoe);
    }

    /// <summary>
    /// How many times an AOE goes off when it is spawned. <c>Start</c> calls Explode when
    /// <c>auto</c> is set and <c>OnEnable</c> calls it again when <c>onEnable</c> is - and a
    /// prefab can set both. The Snowball's impact does, and delivers its cold twice: the
    /// overlay said 5 and the bar said 10 until this was read off the prefab. Every other
    /// blast in 2.4.b is <c>auto</c> alone, which is why four in-game measurements never
    /// hinted at it. Zero means the AOE only ever fires from a TimeEvent, and nothing lands on
    /// the spawn itself.
    /// </summary>
    private static int Firings(AOE aoe) => (aoe.auto ? 1 : 0) + (aoe.onEnable ? 1 : 0);

    /// <summary>
    /// <c>AOE.GetFactor</c> at point-blank range, or zero where the blast would not reach the
    /// character at all.
    /// </summary>
    private static float Factor(AOE aoe)
    {
        if (aoe.ignoreFactor)
        {
            return 1f;
        }

        // A range of zero means Explode returns before doing anything.
        if (aoe.range <= 0f || PointBlankDistance >= aoe.range)
        {
            return 0f;
        }

        float factor = Mathf.Pow(1f - (PointBlankDistance / aoe.range), aoe.factorPow);
        return factor < aoe.minFactor ? 0f : factor;
    }
}
