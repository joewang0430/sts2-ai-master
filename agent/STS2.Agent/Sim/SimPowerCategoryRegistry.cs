using System;
using MegaCrit.Sts2.Core.Models;

namespace STS2.Agent.Sim;

/// <summary>
/// <see cref="SimPowerType"/> index → <see cref="SimPowerCategory"/> static lookup table. Built
/// lazily (double-checked lock) from <c>ModelDb.AllPowers</c> on first use — same pattern as
/// <see cref="SimCardTypeRegistry"/>, indexed via <see cref="SimPowerRegistry"/> instead of
/// <see cref="SimCardDb"/>.
///
/// Sole current consumer: <see cref="SimPowerOps"/> needs to know whether a power being applied is
/// a Debuff, to check whether ArtifactPower should block it.
/// </summary>
internal static class SimPowerCategoryRegistry
{
    private static readonly object s_gate = new();
    private static SimPowerCategory[]? s_byPowerType;

    public static bool IsDebuff(int powerType)
    {
        EnsureBuilt();
        return s_byPowerType![powerType] == SimPowerCategory.Debuff;
    }

    private static void EnsureBuilt()
    {
        if (s_byPowerType is not null) return;
        lock (s_gate)
        {
            if (s_byPowerType is not null) return;
            Build();
        }
    }

    private static void Build()
    {
        var table = new SimPowerCategory[SimPowerType.Count];
        foreach (PowerModel power in ModelDb.AllPowers)
        {
            if (!SimPowerRegistry.TryGetIndex(power.GetType(), out int idx))
            {
                throw new InvalidOperationException(
                    $"SimPowerCategoryRegistry: power '{power.GetType().FullName}' is not registered " +
                    "in SimPowerRegistry. Add typeof(...) → SimPowerType.Xxx and bump SimPowerType.Count.");
            }

            table[idx] = (SimPowerCategory)(byte)power.Type;
        }

        s_byPowerType = table;
    }
}
