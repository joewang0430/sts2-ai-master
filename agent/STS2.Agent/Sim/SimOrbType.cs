namespace STS2.Agent.Sim;

/// <summary>
/// Dense type id for every concrete <c>OrbModel</c> in the game. Indexed by
/// <see cref="SimOrb"/>'s low 3 bits. <c>None=0</c> doubles as "empty slot"
/// in <c>OrbSlots10</c>, which lets <c>Reset()</c> simply zero the inline
/// array instead of writing a sentinel into every entry.
///
/// Wire format is 3 bits (<see cref="SimOrb.TypeMask"/>); current registry
/// uses 6 of 8 codes, leaving headroom for two future orb types before the
/// packed encoding has to widen.
///
/// IMPORTANT: ordering must stay stable — <see cref="SimOrbRegistry"/> maps
/// <c>typeof(LightningOrb)</c> → 1, etc. Changing the values without also
/// editing the registry is a silent corruption bug.
/// </summary>
internal static class SimOrbType
{
    public const byte None      = 0; // empty slot
    public const byte Lightning = 1;
    public const byte Frost     = 2;
    public const byte Dark      = 3;
    public const byte Plasma    = 4;
    public const byte Glass     = 5;

    /// <summary>Number of valid (non-None) orb types. SimCaps verifies the
    /// registry covers every concrete OrbModel.</summary>
    public const int Count = 6;
}
