using System.Collections.Generic;
using System.Reflection;

namespace STS2.Agent.Sim;

/// <summary>
/// SimPowerType index → declared const field name, built once via reflection so any power
/// SimMonsterMoveEffects (or anything else) starts referencing later shows up automatically in
/// debug text — no hand-maintained name table to keep in sync as new powers get registered.
/// Display/debug use only; never part of the blob or a hot path.
/// </summary>
internal static class SimPowerTypeNames
{
    private static readonly Dictionary<int, string> s_names = Build();

    private static Dictionary<int, string> Build()
    {
        var names = new Dictionary<int, string>();
        foreach (FieldInfo field in typeof(SimPowerType).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            if (field.FieldType != typeof(int)) continue;
            names[(int)field.GetValue(null)!] = field.Name;
        }
        return names;
    }

    public static string GetName(int powerType) => s_names.GetValueOrDefault(powerType, $"Power[{powerType}]");
}
