using System;
using System.Collections.Generic;

public enum WeaponClass { Light, Heavy, Defense }
public enum AttackDirection { Up, Down, Left, Right }
public enum WeaponAbility { None, Sword, Dagger, Axe, Hammer, Shield }
public enum GestureSlot { Attack, Defense }
// Pick weapons -> each player records in secret their attack and defense (A attack, A defense, B attack, B defense) -> FIGHT -> clash -> result
public enum DuelPhase { Pick, Record, Fight, Clash, Result, GameOver }

// Pure rules: tracking, UI and animation cannot change the turn order or damage.
public sealed class DuelMatch
{
    public sealed class Card
    {
        public readonly string id;
        public readonly WeaponClass weaponClass;
        public readonly int damage;
        public WeaponAbility Ability => AbilityOf(id);
        public Card(string id, WeaponClass weaponClass, int damage)
        {
            this.id = id; this.weaponClass = weaponClass; this.damage = damage;
        }
    }

    // One attack of the clash, already resolved
    public sealed class Exchange
    {
        public readonly int attacker;
        public readonly AttackDirection direction;
        public readonly AttackDirection defense;
        public readonly bool blocked;
        public readonly int damage;
        public readonly int defenderHealth;
        // 1.5 with class advantage (critical), 0.6 at a disadvantage (not very effective), 1 otherwise
        public readonly double classMultiplier;
        public bool counterattack;
        public int strikeIndex;
        public WeaponAbility ability;
        public int Defender => 1 - attacker;
        public Exchange(int attacker, AttackDirection direction, AttackDirection defense, bool blocked, int damage, int defenderHealth, double classMultiplier = 1)
        {
            this.attacker = attacker; this.direction = direction; this.defense = defense;
            this.blocked = blocked; this.damage = damage; this.defenderHealth = defenderHealth;
            this.classMultiplier = classMultiplier;
        }
    }

    private readonly int[] health = { 100, 100 };
    private readonly int[] blocks = { 0, 0 };
    private readonly Card[] cards = new Card[2];
    private readonly float[] warCryPower = new float[2];
    public float WarCryPower(int player) => warCryPower[player];
    private readonly HashSet<string> used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    // [player, slot]: the round's secret gestures
    private readonly AttackDirection?[,] gestures = new AttackDirection?[2, 2];
    private readonly AttackDirection?[] secondaryDirections = new AttackDirection?[2];
    public bool NeedsSecondDirection => Phase == DuelPhase.Record && CurrentSlot == GestureSlot.Attack && HasGesture(CurrentPlayer, GestureSlot.Attack) && cards[CurrentPlayer].Ability == WeaponAbility.Dagger;
    public AttackDirection? PrimaryDirection => gestures[CurrentPlayer, (int)GestureSlot.Attack];
    private readonly List<Exchange> exchanges = new List<Exchange>();
    public int Round { get; private set; } = 1;
    // Attacks first in this round's clash, and records first
    public int FirstAttacker { get; private set; }
    // Record phase: 0-1 first attacker's attack and defense, 2-3 the other's attack and defense
    public int Step { get; private set; }
    public const int Steps = 4;
    public int CurrentPlayer => Step < 2 ? FirstAttacker : 1 - FirstAttacker;
    public GestureSlot CurrentSlot => Step % 2 == 0 ? GestureSlot.Attack : GestureSlot.Defense;
    public DuelPhase Phase { get; private set; } = DuelPhase.Pick;
    public IReadOnlyList<Exchange> Exchanges => exchanges;
    public int Winner { get; private set; } = -1;
    public int Health(int player) => health[player];
    public int Blocks(int player) => blocks[player];
    public Card Equipped(int player) => cards[player];
    public bool IsUsed(string id) => used.Contains(id);
    public bool HasGesture(int player, GestureSlot slot) => gestures[player, (int)slot].HasValue;

    public bool CanEquip(int player, string id)
    {
        return player >= 0 && player < 2 && !string.IsNullOrEmpty(id) &&
            Phase == DuelPhase.Pick && cards[player] == null && !used.Contains(id) &&
            (id.EndsWith("2", StringComparison.Ordinal) ? 1 : 0) == player;
    }

    public bool Equip(int player, Card card)
    {
        if (card == null || !CanEquip(player, card.id)) return false;
        cards[player] = card;
        if (cards[0] != null && cards[1] != null)
        {
            FirstAttacker = FirstAttackerOf(Round, health[0], health[1], FirstAttacker);
            Step = 0;
            Phase = DuelPhase.Record;
        }
        return true;
    }

    // Stores (or replaces, to redo) the gesture of the current step
    public bool Record(int player, GestureSlot slot, AttackDirection direction, float cryPower = 0f, bool secondary = false)
    {
        if (Phase != DuelPhase.Record || player != CurrentPlayer || slot != CurrentSlot) return false;
        if (secondary)
        {
            if (!NeedsSecondDirection || !ValidSecondary(cards[player], PrimaryDirection.Value, direction)) return false;
            secondaryDirections[player] = direction;
            return true;
        }
        if (slot == GestureSlot.Attack) secondaryDirections[player] = null;
        gestures[player, (int)slot] = direction;
        if (slot == GestureSlot.Attack)
            warCryPower[player] = float.IsNaN(cryPower) ? 0f : Math.Max(0f, Math.Min(1f, cryPower));
        return true;
    }

    // The current gesture is confirmed: next step, or ready to fight after the last one
    public bool ConfirmStep()
    {
        if (Phase != DuelPhase.Record || !HasGesture(CurrentPlayer, CurrentSlot)) return false;
        if (NeedsSecondDirection && !secondaryDirections[CurrentPlayer].HasValue) return false;
        if (Step + 1 < Steps) Step++;
        else Phase = DuelPhase.Fight;
        return true;
    }

    // Resolve each strike and immediate sword counter in order; a KO cancels remaining exchanges.
    public IReadOnlyList<Exchange> Fight()
    {
        if (Phase != DuelPhase.Fight) return exchanges;
        exchanges.Clear();
        for (int i = 0; i < 2; i++)
        {
            int attacker = i == 0 ? FirstAttacker : 1 - FirstAttacker;
            int defender = 1 - attacker;
            AttackDirection primary = gestures[attacker, (int)GestureSlot.Attack].Value;
            AttackDirection defense = gestures[defender, (int)GestureSlot.Defense].Value;
            Card weapon = cards[attacker];
            int total = CalculateDamage(weapon, cards[defender], false, warCryPower[attacker]);
            int firstDamage = IsMulti(weapon) ? (weapon.Ability == WeaponAbility.Axe ? RoundDamage(total * .7) : total / 2) : total;
            int count = IsMulti(weapon) ? 2 : 1;
            for (int strike = 0; strike < count; strike++)
            {
                AttackDirection direction = strike == 0 ? primary : weapon.Ability == WeaponAbility.Axe ? Opposite(primary) : secondaryDirections[attacker].Value;
                int normal = strike == 0 ? firstDamage : total - firstDamage;
                bool blocked = BlocksDirection(cards[defender], direction, defense);
                int damage = blocked ? (weapon.Ability == WeaponAbility.Hammer ? RoundDamage(normal * .25) : 0) : normal;
                if (blocked) blocks[defender]++;
                AddExchange(attacker, direction, defense, blocked, damage, weapon.Ability, strike, false);
                if (health[defender] == 0) break;
                if (blocked && cards[defender].Ability == WeaponAbility.Sword)
                {
                    AddExchange(defender, defense, direction, false, RoundDamage(normal * .2), WeaponAbility.Sword, 0, true);
                    if (health[attacker] == 0) break;
                }
            }
            if (health[0] == 0 || health[1] == 0) break;
        }
        Phase = DuelPhase.Clash;
        return exchanges;
    }

    // The clash animation finished playing
    public void FinishClash()
    {
        if (Phase == DuelPhase.Clash) Phase = DuelPhase.Result;
    }

    public void Continue()
    {
        if (Phase != DuelPhase.Result) return;
        ConsumeCards();
        if (health[0] == 0 || health[1] == 0 || Round == 3)
        {
            Winner = DecideWinner(health[0], health[1], blocks[0], blocks[1]);
            Phase = DuelPhase.GameOver;
            return;
        }
        Round++;
        Phase = DuelPhase.Pick;
    }

    private void ConsumeCards()
    {
        for (int i = 0; i < 2; i++)
        {
            if (cards[i] != null) used.Add(cards[i].id);
            cards[i] = null;
            gestures[i, 0] = gestures[i, 1] = null;
            warCryPower[i] = 0;
            secondaryDirections[i] = null;
        }
        exchanges.Clear();
    }

    // Round 1: A. Then the player with less HP, so the one behind strikes first; a tie swaps the previous order
    public static int FirstAttackerOf(int round, int hpA, int hpB, int previousFirst) =>
        round == 1 ? 0 : hpA != hpB ? (hpA < hpB ? 0 : 1) : 1 - previousFirst;
    public static int DecideWinner(int hpA, int hpB, int blocksA, int blocksB)
    {
        if (hpA != hpB) return hpA > hpB ? 0 : 1;
        if (blocksA != blocksB) return blocksA > blocksB ? 0 : 1;
        return -1;
    }

    public static WeaponAbility AbilityOf(string id)
    {
        string name = (id ?? "").TrimEnd('2').ToLowerInvariant();
        switch (name)
        {
            case "sword": return WeaponAbility.Sword;
            case "dagger": return WeaponAbility.Dagger;
            case "axe": return WeaponAbility.Axe;
            case "hammer": return WeaponAbility.Hammer;
            case "shield": return WeaponAbility.Shield;
            default: return WeaponAbility.None;
        }
    }
    private static bool IsMulti(Card card) => card != null && (card.Ability == WeaponAbility.Dagger || card.Ability == WeaponAbility.Axe);
    private static bool Vertical(AttackDirection direction) => direction == AttackDirection.Up || direction == AttackDirection.Down;
    public static AttackDirection Opposite(AttackDirection direction)
    {
        switch (direction)
        {
            case AttackDirection.Up: return AttackDirection.Down;
            case AttackDirection.Down: return AttackDirection.Up;
            case AttackDirection.Left: return AttackDirection.Right;
            default: return AttackDirection.Left;
        }
    }
    public static bool ValidSecondary(Card card, AttackDirection first, AttackDirection second) =>
        card != null && card.Ability == WeaponAbility.Dagger && first != second;
    public static bool BlocksDirection(Card defender, AttackDirection attack, AttackDirection defense) =>
        attack == defense || (defender.Ability == WeaponAbility.Shield && Vertical(attack) == Vertical(defense));
    private static int RoundDamage(double value) => (int)Math.Round(value, MidpointRounding.AwayFromZero);
    private void AddExchange(int attacker, AttackDirection direction, AttackDirection defense, bool blocked, int damage, WeaponAbility ability, int strike, bool counter)
    {
        int defender = 1 - attacker;
        health[defender] = Math.Max(0, health[defender] - damage);
        exchanges.Add(new Exchange(attacker, direction, defense, blocked, damage, health[defender], ClassMultiplier(cards[attacker].weaponClass, cards[defender].weaponClass))
        { ability = ability, strikeIndex = strike, counterattack = counter });
    }

    // Light > Heavy > Defense > Light
    public static double ClassMultiplier(WeaponClass attacker, WeaponClass defender) =>
        attacker == defender ? 1 : ((int)attacker + 1) % 3 == (int)defender ? 1.5 : 0.6;

    public static int CalculateDamage(Card attacker, Card defender, bool blocked, float cryPower = 0f)
    {
        // Hammer retains 25% of its normal damage through a successful block.
        if (blocked) return attacker.Ability == WeaponAbility.Hammer ? RoundDamage(CalculateDamage(attacker, defender, false, cryPower) * .25) : 0;
        double multiplier = ClassMultiplier(attacker.weaponClass, defender.weaponClass);
        // Round halves up: 31.5 -> 32
        return (int)Math.Round(attacker.damage * multiplier * WarCryRules.Multiplier(cryPower), MidpointRounding.AwayFromZero);
    }
}
