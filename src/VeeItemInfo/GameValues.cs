using System;
using System.Reflection;

namespace VeeItemInfo;

/// <summary>
/// The game's compile-time constants, read out of the loaded assembly at runtime. Naming a
/// <c>const</c> directly would inline it into this assembly and miss a patch; each reading
/// still names the member through <c>nameof</c> so a rename breaks the build. Falls back to
/// the compiled value with a warning rather than throwing on the build path.
/// </summary>
internal static class GameValues
{
    /// <summary>The smallest change the status bars record.</summary>
    internal static float StatusStep { get; } = Constant(
        typeof(CharacterAfflictions),
        nameof(CharacterAfflictions.STATUS_INCREMENT),
        CharacterAfflictions.STATUS_INCREMENT,
        positive: true);

    internal static float StepsPerBar => 1f / StatusStep;

    internal static int CookingMax { get; } = Constant(
        typeof(ItemCooking),
        nameof(ItemCooking.COOKING_MAX),
        ItemCooking.COOKING_MAX);

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

    /// <param name="positive">
    /// Reject a non-positive reading: a zero step would divide by zero, and a garbage value
    /// is worse than a stale one.
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
