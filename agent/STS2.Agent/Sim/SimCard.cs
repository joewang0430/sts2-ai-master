using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace STS2.Agent.Sim;

/// <summary>
/// Per-card-instance hot data carried through every pile (Hand / Draw / Disc /
/// Exhaust). All mutable card-side fields plus the hot enchantment / affliction state game-side <see cref="MegaCrit.Sts2.Core.Models.CardModel"/>
/// can change mid-combat are captured here so a card laundered through
/// <c>Disc → shuffled into Draw → drawn back into Hand</c> retains identity.
///
/// <para><b>Layout</b> (Pack=1, exactly 11 bytes):</para>
/// <code>
/// offset  size  field
///   0      2    CardId             // bit15=upgraded, bits0..14=SimCardId
///   2      1    BaseStarCost       // sbyte; -1 = no star cost (mirrors CanonicalStarCost default)
///   3      1    LastStarsSpent     // byte; 0..255
///   4      1    BaseReplayCount    // byte; Echo Form &amp; co (0 = default 1 play)
///   5      2    Flags              // bit0=ExhaustOnNextPlay, bit1=ShouldRetainThisTurn,
///                                  // bit2=IsSlyThisTurn, bit3=EnchantmentDisabled,
///                                  // bit4=AfflictionAppliedExhaust,
///                                  // bit5=AfflictionAppliedEthereal,
///                                  // bit6=HasExhaustKeyword, bit7=HasRetainKeyword,
///                                  // bit8=HasInnateKeyword, bit9=HasEternalKeyword,
///                                  // bit10=HasEtherealKeyword, bits11..15=reserved
///   7      1    EnchantmentId      // 0 = None; otherwise SimEnchantmentType.*
///   8      1    EnchantmentAmount  // byte; clamped non-negative stack count
///   9      1    AfflictionId       // 0 = None; otherwise SimAfflictionType.*
///  10      1    AfflictionAmount   // byte; clamped non-negative stack count
/// </code>
///
/// <para>Why 11 bytes:</para>
/// <list type="bullet">
///   <item>The smallest direct extension that stops dropping all afflictions
///         from the combat snapshot while keeping per-card state contiguous.</item>
///   <item>4 piles × (10 + 200 + 200 + 200) × 11 B = 6710 B per snapshot — still
///         within a modest slice of L1d/L2 budget.</item>
///   <item>Default-state cards (no star cost, no replay, no flags, no
///         enchantment, no affliction) compress to <c>{ CardId, -1, 0, 0, 0, 0, 0, 0, 0 }</c> i.e.
///         only CardId+(-1) is non-zero — friendly for SIM DIFF noise filtering.</item>
/// </list>
///
/// <para><b>No reference fields</b>: this is unmanaged, so <c>SimCard[]</c>
/// can be cloned via <see cref="System.Array.Copy(System.Array,System.Array,int)"/>
/// (compiles to a vectorized memmove) and the whole <see cref="SimCombatState"/>
/// remains GC-trace-free.</para>
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1, Size = 11)]
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
    /// bit2 IsSlyThisTurn, bit3 EnchantmentDisabled,
    /// bit4 AfflictionAppliedExhaust, bit5 AfflictionAppliedEthereal,
    /// bit6 HasExhaustKeyword, bit7 HasRetainKeyword,
    /// bit8 HasInnateKeyword, bit9 HasEternalKeyword,
    /// bit10 HasEtherealKeyword. See
    /// <see cref="FlagExhaustOnNextPlay"/> &amp; co.</summary>
    public ushort Flags;

    /// <summary>0 = no enchantment; otherwise an index from <see cref="SimEnchantmentType"/>.</summary>
    public byte EnchantmentId;

    /// <summary>Enchantment stack count (<see cref="MegaCrit.Sts2.Core.Models.EnchantmentModel.Amount"/>),
    /// clamped to byte; 0 if no enchantment.</summary>
    public byte EnchantmentAmount;

    /// <summary>0 = no affliction; otherwise an index from <see cref="SimAfflictionType"/>.</summary>
    public byte AfflictionId;

    /// <summary>Affliction stack count (<see cref="MegaCrit.Sts2.Core.Models.AfflictionModel.Amount"/>),
    /// clamped to byte; 0 if no affliction.</summary>
    public byte AfflictionAmount;

    public const ushort FlagExhaustOnNextPlay = 1 << 0;
    public const ushort FlagShouldRetainThisTurn = 1 << 1;
    public const ushort FlagIsSlyThisTurn = 1 << 2;
    public const ushort FlagEnchantmentDisabled = 1 << 3;
    public const ushort FlagAfflictionAppliedExhaust = 1 << 4;
    public const ushort FlagAfflictionAppliedEthereal = 1 << 5;
    public const ushort FlagHasExhaustKeyword = 1 << 6;
    public const ushort FlagHasRetainKeyword = 1 << 7;
    public const ushort FlagHasInnateKeyword = 1 << 8;
    public const ushort FlagHasEternalKeyword = 1 << 9;
    public const ushort FlagHasEtherealKeyword = 1 << 10;

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

    /// <summary>True iff the attached enchantment has transitioned out of its active state.</summary>
    public readonly bool IsEnchantmentDisabled
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => (Flags & FlagEnchantmentDisabled) != 0;
    }

    /// <summary>True iff the attached Devoured affliction added Exhaust itself.</summary>
    public readonly bool IsAfflictionAppliedExhaust
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => (Flags & FlagAfflictionAppliedExhaust) != 0;
    }

    /// <summary>True iff the attached Hexed affliction added Ethereal itself.</summary>
    public readonly bool IsAfflictionAppliedEthereal
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => (Flags & FlagAfflictionAppliedEthereal) != 0;
    }

    /// <summary>True iff the card currently has the persistent Exhaust keyword.</summary>
    public readonly bool HasExhaustKeyword
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => (Flags & FlagHasExhaustKeyword) != 0;
    }

    /// <summary>True iff the card currently has the persistent Retain keyword.</summary>
    public readonly bool HasRetainKeyword
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => (Flags & FlagHasRetainKeyword) != 0;
    }

    /// <summary>True iff the card currently has the Innate keyword.</summary>
    public readonly bool HasInnateKeyword
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => (Flags & FlagHasInnateKeyword) != 0;
    }

    /// <summary>True iff the card currently has the Eternal keyword.</summary>
    public readonly bool HasEternalKeyword
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => (Flags & FlagHasEternalKeyword) != 0;
    }

    /// <summary>True iff the card currently has the Ethereal keyword.</summary>
    public readonly bool HasEtherealKeyword
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => (Flags & FlagHasEtherealKeyword) != 0;
    }
}
