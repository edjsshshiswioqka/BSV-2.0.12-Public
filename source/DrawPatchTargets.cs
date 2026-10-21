using System.Reflection;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;
namespace BetterStardewValley;

// A — exact 1.6.15 target, including the final drawShadow parameter.
// Inherited Item convenience overloads must not participate in resolution.
internal static class DrawPatchTargets
{
    internal static MethodInfo ObjectMenu() => Require(typeof(StardewValley.Object), "drawInMenu", typeof(void),
        typeof(SpriteBatch), typeof(Vector2), typeof(float), typeof(float), typeof(float),
        typeof(StackDrawType), typeof(Color), typeof(bool));

    internal static MethodInfo Require(Type type, string name, Type returns, params Type[] parameters)
    {
        var matches = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(m => m.Name == name && !m.IsGenericMethod && m.ReturnType == returns
                && m.GetParameters().Select(p => p.ParameterType).SequenceEqual(parameters)).ToArray();
        if (matches.Length != 1)
            throw new MissingMethodException($"BSV rendering target rejected: {type.FullName}.{name}({string.Join(", ", parameters.Select(p => p.FullName))}) -> {returns.FullName}; expected exactly one declared match, found {matches.Length}. No patch installed for this target.");
        return matches[0];
    }
}
