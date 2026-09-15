using UnityEngine;

namespace VeeItemInfo;

/// <summary>
/// What an explosion actually delivers to the person who set it off: the AOE's amount scaled
/// by distance to the torso, then floored to whole status steps. See docs/internals_game.md,
/// "Blasts, fields and emitters".
/// </summary>
internal static class Blast
{
    /// <summary>
    /// How far your own blast goes off from the chest it is measured to, in Unity units.
    /// Empirical; the readings are in docs/internals_game.md, "The point-blank distance".
    /// </summary>
    private const float PointBlankDistance = 0.64f;

    /// <summary>Whether a radius measured from the character's centre can be entered on foot.</summary>
    internal static bool Reachable(float radius) => radius > PointBlankDistance;

    /// <summary>
    /// Not for Petrify: <paramref name="amount"/> is assumed to be a 0-1 fraction, and petrify
    /// is whole points.
    /// </summary>
    internal static float Delivered(AOE aoe, float amount)
    {
        float factor = Factor(aoe);
        if (factor <= 0f)
        {
            return 0f;
        }

        // Floor the magnitude, not the signed value: healing arrives negative, and flooring
        // that directly rounds away from zero.
        float scaled = Mathf.Abs(amount) * factor;
        float delivered = Mathf.Floor(scaled * GameValues.StepsPerBar) * GameValues.StatusStep;

        // Floored per firing, because the game discards the remainder on each payout.
        return (amount < 0f ? -delivered : delivered) * Firings(aoe);
    }

    /// <summary>Start fires the AOE when auto is set; OnEnable fires it again when onEnable is.</summary>
    private static int Firings(AOE aoe) => (aoe.auto ? 1 : 0) + (aoe.onEnable ? 1 : 0);

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
