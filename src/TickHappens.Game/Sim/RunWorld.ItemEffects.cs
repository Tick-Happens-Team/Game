using System.Numerics;
using TickHappens.Game.Content;

namespace TickHappens.Game.Sim;

public sealed partial class RunWorld
{
    private readonly float[,] _poisonDamage;
    private readonly int[,] _poisonTick, _poisonSource;
    private readonly Dictionary<string, int> _bossRewards = new(StringComparer.Ordinal);

    private void UpdateAllPlayerStats()
    {
        foreach (var p in _players)
        {
            if (!p.Active || p.Hp <= 0) continue;
            UpdateDynamicStats(p);
            p.AuraBlockChance = p.PhysicalAuraBonus = 0;
        }
        foreach (var source in _players)
        {
            if (!source.Active || source.Hp <= 0) continue;
            foreach (int item in source.EffectItems)
            {
                var effect = Content.Items[item].Effect!;
                var stat = effect.Kind switch
                {
                    "armorAura" => Stat.Armor,
                    "attackSpeedAura" => Stat.AttackSpeed,
                    "healthAura" => Stat.MaxHp,
                    "physicalAura" => Stat.PhysicalDamage,
                    "lifestealAura" => Stat.Lifesteal,
                    _ => (Stat?)null,
                };
                if (stat == null) continue;
                float value = effect.Value * source.ItemStacks[item];
                float range = effect.Radius * source.Stats[Stat.Area];
                foreach (var target in _players)
                {
                    if (!target.Active || target.Hp <= 0 || Vector2.DistanceSquared(source.Pos, target.Pos) > range * range) continue;
                    bool percent = stat == Stat.PhysicalDamage;
                    target.Stats.ApplyDynamic(stat.Value, percent ? 0 : value, percent ? value : 0);
                    if (stat == Stat.Armor) target.AuraBlockChance += effect.Chance * source.ItemStacks[item];
                    if (stat == Stat.PhysicalDamage) target.PhysicalAuraBonus += value;
                }
            }
            if (source.Character.Aura is not { } aura) continue;
            float auraRange = CharacterAuraRadius(source, aura);
            float reduction = CharacterAuraValue(source, aura.DamageReduction, aura.DamageReductionPerLevel);
            float regen = CharacterAuraValue(source, aura.Regen, aura.RegenPerLevel);
            foreach (var target in _players)
            {
                if (!target.Active || target.Hp <= 0 || Vector2.DistanceSquared(source.Pos, target.Pos) > auraRange * auraRange) continue;
                float scale = target == source ? aura.SelfMultiplier : 1;
                target.Stats.ApplyDynamic(Stat.DamageTaken, -reduction * scale, 0);
                target.Stats.ApplyDynamic(Stat.Regen, regen * scale, 0);
            }
        }
    }

    private static float CharacterAuraValue(PlayerState source, float value, float perLevel) => value + perLevel * (source.Level - 1);

    private float CharacterAuraRadius(PlayerState source, CharacterAura aura) =>
        CharacterAuraValue(source, aura.Radius, aura.RadiusPerLevel) * source.Stats[Stat.Area];

    private void StepItemEffects(PlayerState p)
    {
        foreach (int item in p.EffectItems)
        {
            var effect = Content.Items[item].Effect!;
            int copies = p.ItemStacks[item];
            switch (effect.Kind)
            {
                case "rearSlow":
                    if (Tick % 5 == 0)
                    {
                        var cone = CombatArea.Cone(p.Pos, effect.Radius * p.Stats[Stat.Area], -p.Facing, 180);
                        for (int enemy = 0; enemy < _enemyHigh; enemy++)
                            if (EAlive[enemy] && cone.Contains(new Vector2(EX[enemy], EY[enemy]), out _))
                                ApplyEnemyStatus(enemy, Math.Min(0.9f, effect.Value * copies), 0.2f, 0);
                    }
                    break;
                case "weldingBlock":
                    p.ItemTimers[item] = Math.Max(0, p.ItemTimers[item] - Dt);
                    break;
                case "lowestHealthShield":
                case "resetCooldown":
                    p.ItemTimers[item] -= Dt;
                    if (p.ItemTimers[item] > 0.001f) break;
                    p.ItemTimers[item] = effect.Duration * p.Stats[Stat.Cooldown];
                    if (effect.Kind == "lowestHealthShield") ShieldLowestHealthAlly(p, effect.Value * copies);
                    else ResetItemCooldown(p, item);
                    break;
                case "devour":
                    p.ItemTimers[item] -= Dt;
                    if (p.ItemTimers[item] <= 0.001f && p.Hp < MaxHealthFor(p) * effect.Threshold)
                    {
                        int enemy = RandomEnemy(p.Pos, float.PositiveInfinity, excludeBoss: true);
                        if (enemy < 0) break;
                        ExecuteEnemy(p, enemy, DamageKind.Pure, triggerItems: false);
                        RestorePlayer(p, MaxHealthFor(p) * effect.Value * copies);
                        p.ItemTimers[item] = effect.Duration * p.Stats[Stat.Cooldown];
                    }
                    break;
            }
        }
    }

    private void ResetItemCooldown(PlayerState p, int clock)
    {
        int chosen = -1, options = 0;
        for (int i = 0; i < p.ItemTimers.Length; i++)
        {
            if (i == clock || p.ItemStacks[i] <= 0) continue;
            bool readySoon = p.ItemTimers[i] <= Dt;
            bool shield = Tick < p.PhysicalBlockTick && Content.Items[i].Mods.Exists(mod => mod.Stat == "physicalBlock");
            bool compote = Tick < p.ShieldRechargeTick && Content.Items[i].Mods.Exists(mod => mod.Stat == "rechargingShield");
            if (readySoon && !shield && !compote) continue;
            if (_rng.Int(++options) == 0) chosen = i;
        }
        if (chosen < 0) return;
        p.ItemTimers[chosen] = 0;
        foreach (var mod in Content.Items[chosen].Mods)
        {
            if (mod.Stat == "physicalBlock") p.PhysicalBlockTick = Tick;
            if (mod.Stat == "rechargingShield")
            {
                p.ShieldRechargeTick = Tick;
                p.Shield = ShieldCapacity(p);
            }
        }
    }

    private void ShieldLowestHealthAlly(PlayerState source, float size)
    {
        PlayerState player = source;
        foreach (var ally in _players)
            if (ally.Active && ally.Hp > 0 && ally.Hp < player.Hp) player = ally;

        int companion = -1;
        float hp = player.Hp;
        for (int i = 0; i < CompanionCapacity; i++)
        {
            ref readonly var ally = ref _companions[i];
            if (!ally.Active || ally.Health <= 0 || ally.Health >= hp) continue;
            (companion, hp) = (i, ally.Health);
        }
        if (companion >= 0) _companions[companion].Shield = Math.Max(_companions[companion].Shield, size);
        else player.BonusShield = Math.Max(player.BonusShield, size);
    }

    private void RestorePlayer(PlayerState p, float amount, bool share = true)
    {
        if (p.Hp <= 0 || amount <= 0) return;
        float restored = Math.Min(amount, Math.Max(0, MaxHealthFor(p) - p.Hp));
        p.Hp += restored;
        if (share && restored > 0) ShareHealing(p.Id, restored, summon: false);
    }

    private void ShareHealing(int target, float amount, bool summon)
    {
        foreach (var source in _players)
        {
            if (!source.Active || source.Hp <= 0 || (!summon && source.Id == target)) continue;
            foreach (int item in source.EffectItems)
            {
                var effect = Content.Items[item].Effect!;
                if (effect.Kind != "healShare") continue;
                float fraction = summon ? effect.Secondary : effect.Value;
                RestorePlayer(source, amount * fraction * source.ItemStacks[item], share: false);
            }
        }
    }

    private void UpdateCompanionAuras(ref Companion ally)
    {
        ally.Armor = ally.BlockChance = ally.AttackSpeed = ally.PhysicalDamage = ally.Lifesteal = ally.DamageReduction = 0;
        float capacity = ally.MaxHealth;
        float regen = 0;
        foreach (var source in _players)
        {
            if (!source.Active || source.Hp <= 0) continue;
            if (source.Character.Aura is { } aura)
            {
                float auraRange = CharacterAuraRadius(source, aura);
                if (Vector2.DistanceSquared(source.Pos, ally.Pos) <= auraRange * auraRange)
                {
                    ally.DamageReduction += CharacterAuraValue(source, aura.DamageReduction, aura.DamageReductionPerLevel);
                    regen += CharacterAuraValue(source, aura.Regen, aura.RegenPerLevel);
                }
            }
            foreach (int item in source.EffectItems)
            {
                var effect = Content.Items[item].Effect!;
                if (effect.Radius <= 0) continue;
                int copies = source.ItemStacks[item];
                float range = effect.Radius * source.Stats[Stat.Area];
                if (Vector2.DistanceSquared(source.Pos, ally.Pos) > range * range) continue;
                switch (effect.Kind)
                {
                    case "healthAura": capacity += effect.Secondary * copies; break;
                    case "summonRegenAura": regen += effect.Value * copies; break;
                    case "armorAura":
                        ally.Armor += effect.Value * copies;
                        ally.BlockChance += effect.Chance * copies;
                        break;
                    case "attackSpeedAura": ally.AttackSpeed += effect.Value * copies; break;
                    case "physicalAura": ally.PhysicalDamage += effect.Value * copies; break;
                    case "lifestealAura": ally.Lifesteal += effect.Value * copies; break;
                }
            }
        }
        if (ally.MaxHealth <= 0) return;
        if (capacity > ally.AuraCapacity) ally.Health += capacity - ally.AuraCapacity;
        ally.AuraCapacity = capacity;
        ally.Health = Math.Min(capacity, ally.Health);
        RestoreCompanion(ref ally, regen * Dt);
    }

    private void RestoreCompanion(ref Companion ally, float amount)
    {
        if (ally.MaxHealth <= 0 || !ally.Active || amount <= 0) return;
        float restored = Math.Min(ally.AuraCapacity - ally.Health, amount);
        ally.Health += restored;
        if (restored > 0) ShareHealing(ally.Owner, restored, summon: true);
    }

    private void OnEnemyHitWithItemEffects(PlayerState player, int enemy, float dealt)
    {
        foreach (int item in player.EffectItems)
        {
            var effect = Content.Items[item].Effect!;
            if (effect.Kind != "poison" || !RollChance(effect.Chance * player.ItemStacks[item])) continue;
            if (_poisonTick[player.Id, enemy] == 0)
            {
                _poisonTick[player.Id, enemy] = Tick + (int)MathF.Ceiling(effect.Duration * TicksPerSecond);
                _poisonSource[player.Id, enemy] = item;
            }
            _poisonDamage[player.Id, enemy] += dealt * effect.Value;
        }
    }

    private void StepEnemyPoison(int enemy)
    {
        for (int id = 0; id < _players.Length; id++)
        {
            int due = _poisonTick[id, enemy];
            if (due == 0 || due > Tick) continue;
            float raw = _poisonDamage[id, enemy];
            _poisonDamage[id, enemy] = 0;
            _poisonTick[id, enemy] = 0;
            if (!_players[id].Active) continue;
            DamageEnemy(enemy, raw, DamageKind.Magical, new DamageSourceRef(id, _poisonSource[id, enemy], SuppressProcs: true),
                EX[enemy], EY[enemy], 0);
            if (!EAlive[enemy]) break;
        }
    }

    private void OnEnemyKilledWithItemEffects(PlayerState player, Vector2 pos)
    {
        foreach (int item in player.EffectItems)
        {
            var effect = Content.Items[item].Effect!;
            if (effect.Kind is not ("icicle" or "illusion") || !RollChance(effect.Chance * player.ItemStacks[item])) continue;
            if (effect.Kind == "illusion")
            {
                if (Tick >= player.IllusionUntilTick)
                {
                    player.IllusionPosition = pos;
                    player.IllusionUntilTick = Tick + (int)MathF.Ceiling(effect.Duration * TicksPerSecond);
                }
                continue;
            }
            float radius = effect.Radius * player.Stats[Stat.Area];
            AreaItemHit(pos, radius, effect.Value, DamageKind.Magical,
                new DamageSourceRef(player.Id, item, SuppressProcs: true), stun: effect.Duration);
            Emit(EventKind.Frost, pos.X, pos.Y, radius);
            DropPickup(PickupKind.Heal, pos.X, pos.Y, (int)MathF.Round(effect.Secondary * 10_000), player.Id);
        }
    }

    private void FireIllusion(PlayerState player)
    {
        if (player.IllusionUntilTick <= Tick) return;
        foreach (int item in player.EffectItems)
        {
            var effect = Content.Items[item].Effect!;
            if (effect.Kind != "illusion") continue;
            var from = player.IllusionPosition + new Vector2(0, -10);
            int target = Nearest(player.IllusionPosition, player.Attack.Range, []);
            if (target < 0) return;
            LaunchAt(from, target, player.Stats[Stat.Damage] * effect.Value, player.Attack.Speed,
                player.Attack.Range, Kind(player.Attack.DamageType), new DamageSourceRef(player.Id, item), ProjectileStyle.Phantom);
            return;
        }
    }

    private bool TryBlockOrRedirect(PlayerState p, int enemy, DamageKind kind, float amount)
    {
        if (kind == DamageKind.Physical && RollChance(p.AuraBlockChance)) return true;
        foreach (int item in p.EffectItems)
        {
            var effect = Content.Items[item].Effect!;
            float chance = effect.Chance * p.ItemStacks[item];
            if (effect.Kind == "weldingBlock" && p.ItemTimers[item] <= 0.001f && RollChance(chance))
            {
                p.ItemTimers[item] = effect.Duration;
                return true;
            }
            if (enemy >= 0 && effect.Kind == "attackSpeedAura" && chance > 0 && kind == DamageKind.Physical &&
                amount > effect.Threshold && RollChance(chance))
            {
                ApplyEnemyHit(enemy, amount, kind, p.Id, p.Pos.X, p.Pos.Y, 0);
                return true;
            }
        }
        return false;
    }

    private bool TryRevive(PlayerState player)
    {
        for (int i = 0; i < player.EffectItems.Count; i++)
        {
            int item = player.EffectItems[i];
            var effect = Content.Items[item].Effect!;
            if (effect.Kind != "revive") continue;
            if (--player.ItemStacks[item] == 0) player.EffectItems.RemoveAt(i);
            player.Hp = MaxHealthFor(player);
            player.InvulnTicks = Math.Max(player.InvulnTicks, (int)MathF.Ceiling(effect.Duration * TicksPerSecond));
            Emit(EventKind.Pulse, player.Pos.X, player.Pos.Y, 25);
            return true;
        }
        return false;
    }

    private void ExecuteEnemy(PlayerState player, int enemy, DamageKind kind, bool triggerItems = true)
    {
        Emit(EventKind.ItemBeam, player.Pos.X, player.Pos.Y, x2: EX[enemy], y2: EY[enemy]);
        ApplyEnemyHit(enemy, EHp[enemy], kind, player.Id, player.Pos.X, player.Pos.Y, 0, triggerItems);
    }
}
