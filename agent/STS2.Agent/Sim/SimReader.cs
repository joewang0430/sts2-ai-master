using System.Linq;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;

namespace STS2.Agent.Sim;

/// <summary>
/// Boundary helpers: extract minimal sim-relevant scalars from real game objects.
/// Kept here (not on SimCombatState) so the pure data class stays game-agnostic.
///
/// Power matching is by class-name string to avoid hard-linking against every
/// PowerModel subclass. STS2 power class names are stable across patches.
/// </summary>
internal static class SimReader
{
    public static int GetPowerAmount(Creature c, string powerClassName)
    {
        foreach (PowerModel p in c.Powers)
        {
            if (p.GetType().Name == powerClassName)
                return (int)p.Amount;
        }
        return 0;
    }

    public static int  Strength(Creature c)   => GetPowerAmount(c, "StrengthPower");
    public static bool Vulnerable(Creature c) => GetPowerAmount(c, "VulnerablePower") > 0;
    public static bool Weak(Creature c)       => GetPowerAmount(c, "WeakPower") > 0;
}
