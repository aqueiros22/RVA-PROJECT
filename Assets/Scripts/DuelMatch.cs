using System;
using System.Collections.Generic;

public enum WeaponClass { Light, Heavy, Defense }
public enum AttackDirection { Up, Down, Left, Right }
public enum DuelPhase { Pick, Defense, DefenseChosen, Attack, Result, GameOver }

// Pure rules: tracking, UI and animation cannot change the turn order or damage.
public sealed class DuelMatch
{
    public sealed class Card
    {
        public readonly string id;
        public readonly WeaponClass weaponClass;
        public readonly int damage;
        public readonly double block;
        public Card(string id, WeaponClass weaponClass, int damage, double block)
        {
            this.id = id; this.weaponClass = weaponClass; this.damage = damage; this.block = block;
        }
    }

    private readonly int[] health = { 100, 100 };
    private readonly int[] blocks = { 0, 0 };
    private readonly Card[] cards = new Card[2];
    private readonly HashSet<string> used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private AttackDirection defense;
    private int attacks;
    public int Round { get; private set; } = 1;
    public int Attacker { get; private set; }
    public int Defender => 1 - Attacker;
    public int InputPlayer => Phase == DuelPhase.Defense ? Defender : Attacker;
    public DuelPhase Phase { get; private set; } = DuelPhase.Pick;
    public int LastDamage { get; private set; }
    public bool LastBlocked { get; private set; }
    public int Winner { get; private set; } = -1;
    public int Health(int player) => health[player];
    public int Blocks(int player) => blocks[player];
    public Card Equipped(int player) => cards[player];
    public bool IsUsed(string id) => used.Contains(id);

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
            Attacker = FirstAttacker(Round, health[0], health[1]);
            Phase = DuelPhase.Defense;
        }
        return true;
    }

    public bool Submit(int player, AttackDirection direction)
    {
        if (player != InputPlayer) return false;
        if (Phase == DuelPhase.Defense)
        {
            defense = direction;
            Phase = DuelPhase.DefenseChosen;
            return true;
        }
        if (Phase != DuelPhase.Attack) return false;
        LastBlocked = defense == direction;
        LastDamage = CalculateDamage(cards[Attacker], cards[Defender], LastBlocked);
        if (LastBlocked) blocks[Defender]++;
        health[Defender] = Math.Max(0, health[Defender] - LastDamage);
        attacks++;
        Phase = DuelPhase.Result;
        return true;
    }

    public void Continue()
    {
        if (Phase == DuelPhase.DefenseChosen) { Phase = DuelPhase.Attack; return; }
        if (Phase != DuelPhase.Result) return;
        if (health[0] == 0 || health[1] == 0 || (Round == 3 && attacks == 2))
        {
            ConsumeCards();
            Winner = DecideWinner(health[0], health[1], blocks[0], blocks[1]);
            Phase = DuelPhase.GameOver;
        }
        else if (attacks == 1)
        {
            Attacker = 1 - Attacker;
            Phase = DuelPhase.Defense;
        }
        else
        {
            ConsumeCards();
            Round++;
            attacks = 0;
            Phase = DuelPhase.Pick;
        }
    }

    private void ConsumeCards()
    {
        for (int i = 0; i < 2; i++)
        {
            if (cards[i] != null) used.Add(cards[i].id);
            cards[i] = null;
        }
    }

    public static int FirstAttacker(int round, int hpA, int hpB) => round == 1 ? 0 : round == 2 ? 1 : hpA < hpB ? 0 : 1;
    public static int DecideWinner(int hpA, int hpB, int blocksA, int blocksB)
    {
        if (hpA != hpB) return hpA > hpB ? 0 : 1;
        if (blocksA != blocksB) return blocksA > blocksB ? 0 : 1;
        return -1;
    }

    public static int CalculateDamage(Card attacker, Card defender, bool blocked)
    {
        double multiplier = attacker.weaponClass == defender.weaponClass ? 1 :
            ((int)attacker.weaponClass + 1) % 3 == (int)defender.weaponClass ? 1.5 : 0.6;
        // Round halves up: 25 * 0.5 = 13, as specified in the rules.
        return (int)Math.Round(attacker.damage * multiplier * (blocked ? defender.block : 1), MidpointRounding.AwayFromZero);
    }
}
