namespace STS2.Agent.Sim;

/// <summary>
/// Pure block-gain calculation. Mirrors the game's Hook.ModifyBlock order — confirmed by reading
/// it directly (game_source/Hooks/Hook.cs), same structure as <see cref="SimDamage"/>:
///   1. Additive (Dexterity contributes +Amount for the card-owning creature's own block gains)
///   2. Multiplicative (Frail ×0.75 on the creature gaining block; NoBlock ×0 for card-sourced
///      block specifically)
///
/// Source-of-truth references (inspected in game_source):
///   DexterityPower.cs — additive +Amount when the gaining creature (or the played card's owner)
///     matches the power's owner, for card/monster-move-sourced block.
///   FrailPower.cs      — ×0.75 when target == owner, for card/monster-move-sourced block.
///   NoBlockPower.cs    — ×0 when a cardSource is involved (i.e. always for card-played block).
///   Hook.cs ModifyBlock — confirms additive-then-multiplicative order, same as ModifyDamage.
///
/// Only these 3 of 6 block-modifying powers in the game are covered (FastenPower, ShadowmeldPower,
/// UnmovablePower are not) — add them if a card/monster interaction ever needs them.
/// </summary>
internal static class SimBlock
{
    public static int Compute(int raw, int dexterity, bool frail, bool noBlock)
    {
        decimal d = raw + dexterity;
        if (d < 0m) d = 0m;
        if (frail)   d *= 0.75m;
        if (noBlock) d *= 0m;
        return (int)d;
    }
}
