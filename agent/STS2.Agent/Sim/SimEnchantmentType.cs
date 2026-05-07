namespace STS2.Agent.Sim;

/// <summary>
/// Compile-time integer indices for every concrete EnchantmentModel subclass
/// in the game. Mirrors the pattern of <see cref="SimPowerType"/> but in a
/// single byte: there are only ~22 real enchantments today, so byte (0..255)
/// has 10x growth headroom.
///
/// Index <c>0</c> is the <b>None</b> sentinel — stored in
/// <see cref="SimCard.EnchantmentId"/> when a card has no enchantment.
/// Real types start at index 1 to keep zero unambiguous.
///
/// Index assignment: ordinal sort of class names. Stable across rebuilds for
/// the same game version. Includes <c>DeprecatedEnchantment</c> defensively
/// (loaded by ModelDb even though saves shouldn't reference it). Excludes
/// the <c>*.Enchantments.Mocks</c> namespace (unit-test fixtures, never seen
/// in real combat).
///
/// Hand-maintained alongside <see cref="SimEnchantmentRegistry"/>: a missing
/// typeof() reference there fails at compile time, which is the canary.
/// </summary>
internal static class SimEnchantmentType
{
    public const byte None              = 0;

    public const byte Adroit            = 1;
    public const byte Clone             = 2;
    public const byte Corrupted         = 3;
    public const byte Deprecated        = 4;
    public const byte Glam              = 5;
    public const byte Goopy             = 6;
    public const byte Imbued            = 7;
    public const byte Inky              = 8;
    public const byte Instinct          = 9;
    public const byte Momentum          = 10;
    public const byte Nimble            = 11;
    public const byte PerfectFit        = 12;
    public const byte RoyallyApproved   = 13;
    public const byte Sharp             = 14;
    public const byte Slither           = 15;
    public const byte SlumberingEssence = 16;
    public const byte SoulsPower        = 17;
    public const byte Sown              = 18;
    public const byte Spiral            = 19;
    public const byte Steady            = 20;
    public const byte Swift             = 21;
    public const byte TezcatarasEmber   = 22;
    public const byte Vigorous          = 23;

    /// <summary>One past the highest real index. <c>Count - 1</c> = real
    /// enchantment count; index 0 (None) is reserved.</summary>
    public const int Count = 24;
}
