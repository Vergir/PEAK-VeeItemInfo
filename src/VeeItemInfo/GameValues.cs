using System;
using System.Reflection;

namespace VeeItemInfo;

/// <summary>
/// Numbers the game declares as compile-time constants, read at runtime instead.
///
/// Ordinary field and property access - <c>aoe.range</c>, <c>item.CarryWeight</c> - is already
/// dynamic: the value comes off the running game, and a rename breaks our build. A
/// <c>const</c> is the one thing that does not work that way. C# inlines it at the call site,
/// so writing <c>CharacterAfflictions.STATUS_INCREMENT</c> bakes 0.025 into this assembly and
/// a patch that changed the value would leave a shipped build quietly saying the old thing -
/// exactly the failure this mod keeps having with hardcoded numbers.
///
/// <c>FieldInfo.GetRawConstantValue</c> reads the literal out of the loaded assembly's
/// metadata, so it does follow a patch. Each reading here still names the member through
/// <c>nameof</c> and passes the compiled value as the fallback, which keeps both halves: a
/// rename or removal breaks the build, and a value change is picked up without one.
///
/// <b>Fallback, never throw.</b> This is a read-only overlay, and an exception on the build
/// path blanks it - the failure the whole design already avoids, from
/// <see cref="EffectColors.Get"/> never throwing to <see cref="StatusIcons.Tag"/> degrading to
/// plain text. A number that is one patch stale still describes the item usefully; no overlay
/// describes nothing. What must not happen is a *silent* fallback, so every one of them warns
/// once, by name, and the log says which value is being used instead.
/// </summary>
internal static class GameValues
{
    /// <summary>
    /// The smallest change the status bars can record. CharacterAfflictions banks a running
    /// total per status and only ever spends it in whole units of this - and it is the same
    /// number behind the weight per carry unit, the status per thorn increment, and the 1/40
    /// that <c>RoundStatus</c> snaps to.
    /// </summary>
    internal static float StatusStep { get; } = Constant(
        typeof(CharacterAfflictions),
        nameof(CharacterAfflictions.STATUS_INCREMENT),
        CharacterAfflictions.STATUS_INCREMENT,
        positive: true);

    /// <summary>
    /// One status step expressed as a number of steps in a full bar - the divisor the game
    /// uses when it floors an amount onto the scale. Derived rather than declared, so there is
    /// only ever one number to be wrong about.
    /// </summary>
    internal static float StepsPerBar => 1f / StatusStep;

    /// <summary>
    /// How many times an item can be cooked. <c>ItemCooking.COOKING_MAX</c> is a const, so
    /// naming it directly inlines 12 into this assembly and a patch raising the ceiling would
    /// leave the cooking hint going quiet at the old one.
    /// </summary>
    internal static int CookingMax { get; } = Constant(
        typeof(ItemCooking),
        nameof(ItemCooking.COOKING_MAX),
        ItemCooking.COOKING_MAX);

    /// <summary>Integer twin of the float reader below; a non-positive reading is rejected.</summary>
    private static int Constant(Type owner, string name, int compiled)
    {
        try
        {
            FieldInfo? field = owner.GetField(name, BindingFlags.Public | BindingFlags.Static);
            if (field != null && field.IsLiteral && field.GetRawConstantValue() is int read && read > 0)
            {
                return read;
            }

            Warn(owner, name, compiled, field == null ? "no such constant" : "unusable value");
        }
        catch (Exception e)
        {
            Warn(owner, name, compiled, e.Message);
        }

        return compiled;
    }

    /// <summary>
    /// Reads a <c>const</c> field out of the loaded game assembly, falling back to the value
    /// this assembly was compiled against.
    /// </summary>
    /// <param name="positive">
    /// Reject a non-positive reading. A zero step would divide by zero everywhere it is used,
    /// and a garbage value is worse than a stale one because nothing downstream can tell.
    /// </param>
    private static float Constant(Type owner, string name, float compiled, bool positive)
    {
        try
        {
            FieldInfo? field = owner.GetField(name, BindingFlags.Public | BindingFlags.Static);
            if (field != null && field.IsLiteral)
            {
                object? raw = field.GetRawConstantValue();
                if (raw is float read && (!positive || read > 0f))
                {
                    return read;
                }
            }

            Warn(owner, name, compiled, field == null ? "no such constant" : "unusable value");
        }
        catch (Exception e)
        {
            Warn(owner, name, compiled, e.Message);
        }

        return compiled;
    }

    private static void Warn(Type owner, string name, float compiled, string why) =>
        Plugin.Log.LogWarning(
            $"Could not read {owner.Name}.{name} from the game ({why}); "
            + $"using the value this build was compiled against, {compiled}. "
            + "Numbers derived from it may be wrong if the game has changed it.");
}
