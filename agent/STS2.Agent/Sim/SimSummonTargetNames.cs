using System.Collections.Generic;
using System.Reflection;

namespace STS2.Agent.Sim;

/// <summary>SimSummonTargetId index → declared const field name, built once via reflection —
/// mirrors <see cref="SimPowerTypeNames"/>. Display/debug use only.</summary>
internal static class SimSummonTargetNames
{
    private static readonly Dictionary<int, string> s_names = Build();

    private static Dictionary<int, string> Build()
    {
        var names = new Dictionary<int, string>();
        foreach (FieldInfo field in typeof(SimSummonTargetId).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            if (field.FieldType != typeof(ushort)) continue;
            names[(ushort)field.GetValue(null)!] = field.Name;
        }
        return names;
    }

    public static string GetName(int summonTargetId) => s_names.GetValueOrDefault(summonTargetId, $"Monster[{summonTargetId}]");
}
