using System;

public static class DuelMatchTests
{
    private static int assertions;
    private static void Check(bool value, string message)
    {
        assertions++;
        if (!value) throw new Exception(message);
    }
    private static DuelMatch.Card Card(string id, WeaponClass type, int damage, double block) => new DuelMatch.Card(id, type, damage, block);
    private static void Attack(DuelMatch match, bool blocked)
    {
        Check(match.Phase == DuelPhase.Defense, "Defense must come first");
        Check(!match.Submit(match.Attacker, AttackDirection.Up), "Attacker cannot choose defense");
        Check(match.Submit(match.Defender, AttackDirection.Up), "Defender chooses defense");
        Check(match.Phase == DuelPhase.DefenseChosen, "Handover required");
        Check(!match.Submit(match.Attacker, AttackDirection.Up), "No attack during handover");
        match.Continue();
        Check(!match.Submit(match.Defender, AttackDirection.Up), "Defender cannot attack");
        Check(match.Submit(match.Attacker, blocked ? AttackDirection.Up : AttackDirection.Down), "Attacker attacks");
        Check(match.Phase == DuelPhase.Result, "Result waits for confirmation");
        Check(!match.Submit(match.Attacker, AttackDirection.Down), "Duplicate gesture ignored");
    }
    public static int Main()
    {
        var axe = Card("axe", WeaponClass.Heavy, 25, .5);
        var hammer = Card("hammer", WeaponClass.Heavy, 25, .5);
        var sword = Card("sword", WeaponClass.Light, 20, .4);
        var shield = Card("shield", WeaponClass.Defense, 10, .1);
        Check(DuelMatch.CalculateDamage(axe, shield, false) == 38, "Axe vs shield = 38");
        Check(DuelMatch.CalculateDamage(sword, shield, true) == 1, "Sword vs shield blocked = 1");
        Check(DuelMatch.CalculateDamage(shield, sword, false) == 15, "Shield vs sword = 15");
        Check(DuelMatch.CalculateDamage(hammer, axe, true) == 13, "Half damage rounds up to 13");
        foreach (WeaponClass a in Enum.GetValues(typeof(WeaponClass)))
        foreach (WeaponClass d in Enum.GetValues(typeof(WeaponClass)))
        {
            int expected = a == d ? 20 : ((int)a + 1) % 3 == (int)d ? 30 : 12;
            Check(DuelMatch.CalculateDamage(Card("a", a, 20, .4), Card("d", d, 20, .5), false) == expected, "Class matrix");
        }
        Check(DuelMatch.FirstAttacker(1, 100, 100) == 0, "Round 1 A first");
        Check(DuelMatch.FirstAttacker(2, 100, 100) == 1, "Round 2 B first");
        Check(DuelMatch.FirstAttacker(3, 50, 60) == 0, "Round 3 lower HP first");
        Check(DuelMatch.FirstAttacker(3, 60, 50) == 1, "Round 3 lower HP first B");
        Check(DuelMatch.FirstAttacker(3, 50, 50) == 1, "Round 3 tie B first");
        Check(DuelMatch.DecideWinner(60, 50, 0, 3) == 0, "HP takes priority");
        Check(DuelMatch.DecideWinner(50, 50, 2, 1) == 0, "Blocks tiebreak A");
        Check(DuelMatch.DecideWinner(50, 50, 1, 2) == 1, "Blocks tiebreak B");
        Check(DuelMatch.DecideWinner(50, 50, 1, 1) == -1, "Draw");
        var match = new DuelMatch();
        Check(match.Health(0) == 100 && match.Health(1) == 100, "Initial HP");
        Check(!match.Submit(0, AttackDirection.Up), "No gestures during pick");
        Check(!match.Equip(1, sword), "Wrong deck rejected");
        string[] ids = { "sword", "dagger", "hammer" };
        for (int round = 1; round <= 3; round++)
        {
            Check(match.Round == round, "Round progression");
            Check(match.Equip(0, Card(ids[round - 1], WeaponClass.Light, 20, .4)), "Equip A");
            Check(match.Phase == DuelPhase.Pick, "Wait for both weapons");
            Check(!match.Equip(0, axe), "Cannot replace locked weapon");
            Check(match.Equip(1, Card(ids[round - 1] + "2", WeaponClass.Light, 20, .4)), "Equip B");
            int first = match.Attacker;
            Check(first == (round == 1 ? 0 : 1), "Actual first attacker");
            Attack(match, true);
            Check(match.LastBlocked && match.LastDamage == 8, "Blocked result");
            match.Continue();
            Check(match.Attacker == 1 - first, "Swap attacker");
            Attack(match, true);
            match.Continue();
            Check(match.IsUsed(ids[round - 1]) && match.IsUsed(ids[round - 1] + "2"), "Both cards consumed");
            Check(!match.CanEquip(0, ids[round - 1]), "Used card rejected");
        }
        Check(match.Phase == DuelPhase.GameOver && match.Winner == -1, "Three rounds draw");
        Check(match.Health(0) == 76 && match.Blocks(0) == 3, "HP and blocks accumulate");
        Check(!match.Submit(0, AttackDirection.Up), "No input after game over");
        var ko = new DuelMatch();
        ko.Equip(0, Card("sword", WeaponClass.Light, 150, .4));
        ko.Equip(1, Card("sword2", WeaponClass.Light, 20, .4));
        Attack(ko, false);
        Check(ko.Health(1) == 0, "HP clamped to zero");
        ko.Continue();
        Check(ko.Phase == DuelPhase.GameOver && ko.Winner == 0 && ko.Round == 1, "KO ends immediately without counterattack");
        var fresh = new DuelMatch();
        Check(fresh.Health(0) == 100 && fresh.CanEquip(0, "sword"), "Rematch resets health and used cards");
        Check(!fresh.CanEquip(0, "sword2") && !fresh.CanEquip(1, "sword"), "Deck ownership both ways");
        Check(!fresh.CanEquip(-1, "sword") && !fresh.CanEquip(2, "sword2"), "Invalid player rejected");
        Check(!fresh.CanEquip(0, null), "Invalid card rejected");
        // Equal HP over a complete match, but only A blocks: the block tiebreak must win.
        var tiebreak = new DuelMatch();
        for (int round = 0; round < 3; round++)
        {
            tiebreak.Equip(0, Card(ids[round], WeaponClass.Light, 0, .4));
            tiebreak.Equip(1, Card(ids[round] + "2", WeaponClass.Light, 0, .4));
            Attack(tiebreak, tiebreak.Defender == 0);
            tiebreak.Continue();
            Attack(tiebreak, tiebreak.Defender == 0);
            tiebreak.Continue();
        }
        Check(tiebreak.Phase == DuelPhase.GameOver && tiebreak.Winner == 0, "Complete match decided by blocks");
        Check(tiebreak.Blocks(0) == 3 && tiebreak.Blocks(1) == 0, "Blocks credited to defender only");
        Console.WriteLine($"PASS: {assertions} assertions");
        return 0;
    }
}
