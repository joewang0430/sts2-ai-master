using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace STS2.Agent.Sim;

/// <summary>
/// Per-card-instance hot data carried through every pile (Hand / Draw / Disc /
/// Exhaust). All seven mutable fields game-side <see cref="MegaCrit.Sts2.Core.Models.CardModel"/>
/// can change mid-combat are captured here so a card laundered through
/// <c>Disc → shuffled into Draw → drawn back into Hand</c> retains identity.
///
/// <para><b>Layout</b> (Pack=1, exactly 8 bytes):</para>
/// <code>
/// offset  size  field
///   0      2    CardId             // bit15=upgraded, bits0..14=SimCardId
///   2      1    BaseStarCost       // sbyte; -1 = no star cost (mirrors CanonicalStarCost default)
///   3      1    LastStarsSpent     // byte; 0..255
///   4      1    BaseReplayCount    // byte; Echo Form &amp; co (0 = default 1 play)
///   5      1    Flags              // bit0=ExhaustOnNextPlay, bit1=ShouldRetainThisTurn,
///                                  // bit2=IsSlyThisTurn, bits3..7=reserved
///   6      1    EnchantmentId      // 0 = None; otherwise SimEnchantmentType.*
///   7      1    EnchantmentAmount  // byte; clamped non-negative stack count
/// </code>
///
/// <para>Why 8 bytes:</para>
/// <list type="bullet">
///   <item>One natural alignment unit; <c>SimCard[]</c> reads/writes are a single
///         <c>movq</c> on x64.</item>
///   <item>4 piles × (10 + 200 + 200 + 200) × 8 B = 4880 B per snapshot — fits
///         within typical L1d budget.</item>
///   <item>Default-state cards (no star cost, no replay, no flags, no
///         enchantment) compress to <c>{ CardId, -1, 0, 0, 0, 0, 0 }</c> i.e.
///         only CardId+(-1) is non-zero — friendly for SIM DIFF noise filtering.</item>
/// </list>
///
/// <para><b>No reference fields</b>: this is unmanaged, so <c>SimCard[]</c>
/// can be cloned via <see cref="System.Array.Copy(System.Array,System.Array,int)"/>
/// (compiles to a vectorized memmove) and the whole <see cref="SimCombatState"/>
/// remains GC-trace-free.</para>
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1, Size = 8)]
internal struct SimCard
{
    /// <summary>Encoded card identity: bit 15 = upgraded, bits 0–14 = SimCardId.</summary>
    public ushort CardId;

    /// <summary><see cref="MegaCrit.Sts2.Core.Models.CardModel.BaseStarCost"/>.
    /// Defaults to -1 (no star cost). Star-cost cards range 0..a few.</summary>
    public sbyte BaseStarCost;

    /// <summary><see cref="MegaCrit.Sts2.Core.Models.CardModel.LastStarsSpent"/>
    /// — captured value of stars consumed last time this card was played.</summary>
    public byte LastStarsSpent;

    /// <summary><see cref="MegaCrit.Sts2.Core.Models.CardModel.BaseReplayCount"/>
    /// — additional plays beyond the first (Echo Form, etc.). 0 = default.</summary>
    public byte BaseReplayCount;

    /// <summary>Bitfield: bit0 ExhaustOnNextPlay, bit1 ShouldRetainThisTurn,
    /// bit2 IsSlyThisTurn. See <see cref="FlagExhaustOnNextPlay"/> &amp; co.</summary>
    public byte Flags;

    /// <summary>0 = no enchantment; otherwise an index from <see cref="SimEnchantmentType"/>.</summary>
    public byte EnchantmentId;

    /// <summary>Enchantment stack count (<see cref="MegaCrit.Sts2.Core.Models.EnchantmentModel.Amount"/>),
    /// clamped to byte; 0 if no enchantment.</summary>
    public byte EnchantmentAmount;

    public const byte FlagExhaustOnNextPlay = 1 << 0;
    public const byte FlagShouldRetainThisTurn = 1 << 1;
    public const byte FlagIsSlyThisTurn = 1 << 2;

    /// <summary>True iff bit 15 of <see cref="CardId"/> is set.</summary>
    public readonly bool IsUpgraded
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => (CardId & 0x8000) != 0;
    }

    /// <summary>The 15-bit <see cref="SimCardId"/> portion of <see cref="CardId"/> (upgrade bit stripped).</summary>
    public readonly ushort BaseCardId
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => (ushort)(CardId & 0x7FFF);
    }
}
