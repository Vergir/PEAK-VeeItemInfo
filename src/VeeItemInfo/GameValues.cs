using System;
using System.Reflection;

namespace VeeItemInfo;

/// <summary>
/// Numbers the game declares as compile-time constants, read at runtime instead. C# inlines
/// a <c>const</c> at the call site, so naming one directly would bake the value into this
/// assembly; <c>GetRawConstantValue</c> reads it out of the loaded game assembly and does
/// follow a patch. Each reading still names the member through <c>nameof</c>, so a rename
/// breaks the build, and passes the compiled value as the fallback. Fallback, never throw:
/// an exception on the build path blanks the overlay, but a silent fallback is not allowed
/// either, so each one warns once by name.
/// </summary>
internal static class GameValues
{
    /// <summary>
    /// The smallest change the status bars can record; the game banks a running total per
    /// status and only spends it in whole units of this.
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

    /// <summary>How many times an item can be cooked.</summary>
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
