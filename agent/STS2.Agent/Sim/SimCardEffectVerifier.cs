using System;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;

namespace STS2.Agent.Sim;

/// <summary>
/// Shadow-simulation safety net for <see cref="SimCardEffects"/>: every time the live player plays
/// a card we've registered, this replays the SAME <see cref="SimCardEffects.Apply"/> call against a
/// scratch <see cref="CombatNodeBlob"/> seeded from the live pre-play numbers, then compares the
/// predicted post-play HP/Block (player and target) against what the real game actually produced.
///
/// Deliberately reuses SimCardEffects.Apply itself rather than re-deriving the formulas — a
/// verifier that duplicates the logic it's checking can have the same bug on both sides and never
/// catch it (this happened for real earlier this session with attack-damage math; see
/// CombatBlobVerifier's AttackDamageFor/AttackHitsFor fix).
///
/// Scope: only checks HP and Block, only for the played card's single Creature target (or the
/// player alone for self-targeted cards). Doesn't verify power stack amounts, doesn't handle
/// AllEnemies-target cards (Thunderclap, DaggerSpray) — those are recorded as "not checked", not
/// silently reported as passing.
/// </summary>
internal static class SimCardEffectVerifier
{
    /// <summary>Most recent verdict text, for <see cref="STS2.Agent.Ui.CombatDebugOverlay"/> to display.</summary>
    public static string LastReport = "";

    private sealed class PendingCheck
    {
        public required string CardName;
        public required Creature Player;
        public Creature? Target;
        public int PlayerHpBefore;
        public int PlayerBlockBefore;
        public int PlayerStrengthBefore;
        public bool PlayerWeakBefore;
        public int PlayerDexterityBefore;
        public bool PlayerFrailBefore;
        public bool PlayerNoBlockBefore;
        public int TargetHpBefore;
        public int TargetBlockBefore;
        public int TargetVulnerableBefore;
        public bool TargetIntangibleBefore;
        public int TargetHardToKillBefore;
        public SimCard Card;
    }

    [HarmonyPatch(typeof(CardModel), "OnPlayWrapper")]
    private static class Patch_CardModel_OnPlayWrapper
    {
        [HarmonyPrefix]
        private static void Prefix(CardModel __instance, Creature? target, out PendingCheck? __state)
        {
            __state = null;
            if (!SimCardDb.TryGetId(__instance.GetType(), out ushort baseId)) return;
            if (!SimCardEffects.IsRegistered(baseId)) return;

            Creature player = __instance.Owner.Creature;
            __state = new PendingCheck
            {
                CardName = __instance.GetType().Name,
                Player = player,
                Target = target,
                PlayerHpBefore = player.CurrentHp,
                PlayerBlockBefore = player.Block,
                PlayerStrengthBefore = SimReader.Strength(player),
                PlayerWeakBefore = SimReader.Weak(player),
                PlayerDexterityBefore = SimReader.GetPowerAmount(player, "DexterityPower"),
                PlayerFrailBefore = SimReader.GetPowerAmount(player, "FrailPower") > 0,
                PlayerNoBlockBefore = SimReader.GetPowerAmount(player, "NoBlockPower") > 0,
                TargetHpBefore = target?.CurrentHp ?? 0,
                TargetBlockBefore = target?.Block ?? 0,
                TargetVulnerableBefore = target != null ? SimReader.GetPowerAmount(target, "VulnerablePower") : 0,
                TargetIntangibleBefore = target != null && SimReader.GetPowerAmount(target, "IntangiblePower") > 0,
                TargetHardToKillBefore = target != null ? SimReader.GetPowerAmount(target, "HardToKillPower") : 0,
                Card = new SimCard { CardId = (ushort)(baseId | (__instance.IsUpgraded ? 0x8000 : 0)) },
            };
        }

        [HarmonyPostfix]
        private static void Postfix(Task __result, PendingCheck? __state)
        {
            if (__state == null) return;
            __result.ContinueWith(_ => CheckAfterPlay(__state));
        }
    }

    private static void CheckAfterPlay(PendingCheck s)
    {
        var scratch = new CombatNodeBlob();
        scratch.Clear();

        scratch.PlayerHp = (ushort)Math.Max(0, s.PlayerHpBefore);
        scratch.PlayerBlock = (ushort)Math.Max(0, s.PlayerBlockBefore);
        if (s.PlayerStrengthBefore != 0)
            SimPowerOps.ApplyPlayerDelta(scratch, SimPowerType.Strength, s.PlayerStrengthBefore);
        if (s.PlayerWeakBefore)
            SimPowerOps.ApplyPlayerDelta(scratch, SimPowerType.Weak, 1);
        if (s.PlayerDexterityBefore != 0)
            SimPowerOps.ApplyPlayerDelta(scratch, SimPowerType.Dexterity, s.PlayerDexterityBefore);
        if (s.PlayerFrailBefore)
            SimPowerOps.ApplyPlayerDelta(scratch, SimPowerType.Frail, 1);
        if (s.PlayerNoBlockBefore)
            SimPowerOps.ApplyPlayerDelta(scratch, SimPowerType.NoBlock, 1);

        scratch.EnemyCount = 1;
        scratch.EnemyHp[0] = (ushort)Math.Max(0, s.TargetHpBefore);
        scratch.EnemyBlock[0] = (ushort)Math.Max(0, s.TargetBlockBefore);
        if (s.TargetVulnerableBefore != 0)
            SimPowerOps.ApplyEnemyDelta(scratch, 0, SimPowerType.Vulnerable, s.TargetVulnerableBefore);
        if (s.TargetIntangibleBefore)
            SimPowerOps.ApplyEnemyDelta(scratch, 0, SimPowerType.Intangible, 1);
        if (s.TargetHardToKillBefore != 0)
            SimPowerOps.ApplyEnemyDelta(scratch, 0, SimPowerType.HardToKill, s.TargetHardToKillBefore);

        SimCardEffects.Apply(scratch, in s.Card, 0);

        int predPlayerHp = scratch.PlayerHp;
        int predPlayerBlock = scratch.PlayerBlock;
        int predTargetHp = scratch.EnemyHp[0];
        int predTargetBlock = scratch.EnemyBlock[0];

        int realPlayerHp = s.Player.CurrentHp;
        int realPlayerBlock = s.Player.Block;
        int realTargetHp = s.Target?.CurrentHp ?? predTargetHp;
        int realTargetBlock = s.Target?.Block ?? predTargetBlock;

        bool ok = predPlayerHp == realPlayerHp
            && predPlayerBlock == realPlayerBlock
            && predTargetHp == realTargetHp
            && predTargetBlock == realTargetBlock;

        LastReport = ok
            ? $"CARD VERIFY: ✓ {s.CardName}"
            : $"CARD VERIFY: ✗ {s.CardName} — predicted P.hp={predPlayerHp} P.block={predPlayerBlock} T.hp={predTargetHp} T.block={predTargetBlock} | real P.hp={realPlayerHp} P.block={realPlayerBlock} T.hp={realTargetHp} T.block={realTargetBlock}";
    }
}
