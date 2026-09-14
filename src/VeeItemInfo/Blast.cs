using UnityEngine;

namespace VeeItemInfo;

/// <summary>
/// What an explosion actually delivers to the person who set it off. <c>AOE.statusAmount</c>
/// is scaled by distance to the torso and then floored to whole status steps, so it is a
/// figure nobody is ever given. The constant here is a distance, not a factor: a factor
/// belongs to one blast, a distance transfers between them. See docs/internals_game.md,
/// "Blasts, fields and emitters".
/// </summary>
internal static class Blast
{
    /// <summary>
    /// How far your own blast goes off from the chest it is measured to, in Unity units.
    /// Empirical - bracketed by four in-game readings, tabulated in docs/internals_game.md,
    /// "The point-blank distance". Readable from <c>Character.Center</c> in principle, and left
    /// a constant so the overlay does not drift while you hold an item.
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
    /// <b>Not for Petrify.</b> The step assumes <paramref name="amount"/> is a 0-1 fraction,
    /// and petrify is whole points on the 0-100 scale. Nothing routes a petrify AOE here; a
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
    /// How many times an AOE goes off when it is spawned: <c>Start</c> fires it when
    /// <c>auto</c> is set and <c>OnEnable</c> fires it again when <c>onEnable</c> is, and the
    /// Snowball sets both. Zero means it only ever fires from a TimeEvent.
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
