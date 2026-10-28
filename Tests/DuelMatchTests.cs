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

    // The players record in alternating steps; each defender guesses right when block[defender] is true. Then FIGHT.
    private static void PlayRound(DuelMatch match, bool blockA, bool blockB)
    {
        bool[] block = { blockA, blockB };
        int first = match.FirstAttacker, second = 1 - first;
        Check(match.Phase == DuelPhase.Record && match.Step == 0, "Recording starts at step 0");
        Check(!match.Record(second, GestureSlot.Attack, AttackDirection.Up), "Only the current player can record");
        Check(!match.Record(first, GestureSlot.Defense, AttackDirection.Up), "Only the current slot can be recorded");
        Check(!match.ConfirmStep(), "Cannot confirm without a gesture");
        var order = new[] { (first, GestureSlot.Attack), (second, GestureSlot.Defense), (second, GestureSlot.Attack), (first, GestureSlot.Defense) };
        for (int step = 0; step < order.Length; step++)
        {
            var (player, slot) = order[step];
            Check(match.Step == step && match.CurrentPlayer == player && match.CurrentSlot == slot, "Alternating step order");
            // Attacks always go Up; a matching defense is Up, a wrong one Down
            AttackDirection direction = slot == GestureSlot.Attack || block[player] ? AttackDirection.Up : AttackDirection.Down;
            Check(match.Record(player, slot, AttackDirection.Left), "Record gesture");
            Check(match.Record(player, slot, direction), "Redo replaces the gesture");
            Check(match.Phase == DuelPhase.Record, "Recording does not fight");
            Check(match.ConfirmStep(), "Confirm step");
        }
        Check(match.Phase == DuelPhase.Fight, "Wait for the FIGHT button");
        Check(!match.Record(first, GestureSlot.Attack, AttackDirection.Down), "No recording after all steps");
        match.Fight();
        Check(match.Phase == DuelPhase.Clash, "Clash plays after FIGHT");
        Check(match.Exchanges.Count >= 1 && match.Exchanges[0].attacker == first, "First attacker strikes first");
        match.FinishClash();
        Check(match.Phase == DuelPhase.Result, "Result after the clash");
    }

    private static void StrokeTests()
    {
        // Portrait 0.5 aspect: the shorter side is the width
        Check(PenStroke.Classify(.5f, .5f, .5f, .5f, .5f, .15f, 1.3f) == null, "No travel, no gesture");
        Check(PenStroke.Classify(.5f, .5f, .5f, .55f, .5f, .15f, 1.3f) == null, "Short move ignored");
        Check(PenStroke.Classify(.5f, .5f, .5f, .6f, 2f, .15f, 1.3f) == null, "Short vertical move in landscape ignored");
        Check(PenStroke.Classify(.5f, .5f, .5f, .62f, .5f, .15f, 1.3f) == AttackDirection.Up, "Up (y grows)");
        Check(PenStroke.Classify(.5f, .5f, .5f, .38f, .5f, .15f, 1.3f) == AttackDirection.Down, "Down");
        Check(PenStroke.Classify(.5f, .5f, .7f, .5f, .5f, .15f, 1.3f) == AttackDirection.Right, "Right");
        Check(PenStroke.Classify(.5f, .5f, .3f, .5f, .5f, .15f, 1.3f) == AttackDirection.Left, "Left");
        Check(PenStroke.Classify(.5f, .5f, .7f, .66f, 1f, .15f, 1.3f) == null, "Diagonal is ambiguous");
        Check(PenStroke.Classify(.5f, .5f, .7f, .6f, 1f, .15f, 1.3f) == AttackDirection.Right, "Mostly sideways counts as sideways");
        Check(PenStroke.Classify(.5f, .5f, .5f, .7f, 2f, .15f, 1.3f) == AttackDirection.Up, "Landscape up");
        Check(PenStroke.Classify(.5f, .5f, .58f, .5f, 2f, .15f, 1.3f) == AttackDirection.Right, "Landscape: width counts more");
    }

    public static int Main()
    {
        StrokeTests();
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
        Check(DuelMatch.FirstAttackerOf(1, 100, 100) == 0, "Round 1 A first");
        Check(DuelMatch.FirstAttackerOf(2, 100, 100) == 1, "Round 2 B first");
        Check(DuelMatch.FirstAttackerOf(3, 50, 60) == 0, "Round 3 lower HP first");
        Check(DuelMatch.FirstAttackerOf(3, 60, 50) == 1, "Round 3 lower HP first B");
        Check(DuelMatch.FirstAttackerOf(3, 50, 50) == 1, "Round 3 tie B first");
        Check(DuelMatch.DecideWinner(60, 50, 0, 3) == 0, "HP takes priority");
        Check(DuelMatch.DecideWinner(50, 50, 2, 1) == 0, "Blocks tiebreak A");
        Check(DuelMatch.DecideWinner(50, 50, 1, 2) == 1, "Blocks tiebreak B");
        Check(DuelMatch.DecideWinner(50, 50, 1, 1) == -1, "Draw");

        var match = new DuelMatch();
        Check(match.Health(0) == 100 && match.Health(1) == 100, "Initial HP");
        Check(!match.Record(0, GestureSlot.Attack, AttackDirection.Up), "No gestures during pick");
        Check(!match.Equip(1, sword), "Wrong deck rejected");
        string[] ids = { "sword", "dagger", "hammer" };
        for (int round = 1; round <= 3; round++)
        {
            Check(match.Round == round, "Round progression");
            Check(match.Equip(0, Card(ids[round - 1], WeaponClass.Light, 20, .4)), "Equip A");
            Check(match.Phase == DuelPhase.Pick, "Wait for both weapons");
            Check(!match.Equip(0, axe), "Cannot replace locked weapon");
            Check(match.Equip(1, Card(ids[round - 1] + "2", WeaponClass.Light, 20, .4)), "Equip B");
            Check(match.FirstAttacker == (round == 1 ? 0 : 1), "Actual first attacker");
            PlayRound(match, true, true);
            Check(match.Exchanges.Count == 2, "Both players attack");
            Check(match.Exchanges[0].blocked && match.Exchanges[0].damage == 8, "Blocked result");
            Check(match.Exchanges[1].attacker == 1 - match.FirstAttacker, "Second attacker swaps");
            match.Continue();
            Check(match.IsUsed(ids[round - 1]) && match.IsUsed(ids[round - 1] + "2"), "Both cards consumed");
            Check(!match.CanEquip(0, ids[round - 1]), "Used card rejected");
        }
        Check(match.Phase == DuelPhase.GameOver && match.Winner == -1, "Three rounds draw");
        Check(match.Health(0) == 76 && match.Blocks(0) == 3, "HP and blocks accumulate");
        Check(!match.Record(0, GestureSlot.Attack, AttackDirection.Up), "No input after game over");

        var ko = new DuelMatch();
        ko.Equip(0, Card("sword", WeaponClass.Light, 150, .4));
        ko.Equip(1, Card("sword2", WeaponClass.Light, 20, .4));
        PlayRound(ko, false, false);
        Check(ko.Health(1) == 0 && ko.Health(0) == 100, "KO cancels the counterattack");
        Check(ko.Exchanges.Count == 1 && ko.Exchanges[0].defenderHealth == 0, "Only the KO exchange plays");
        ko.Continue();
        Check(ko.Phase == DuelPhase.GameOver && ko.Winner == 0 && ko.Round == 1, "KO ends immediately");

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
            PlayRound(tiebreak, true, false);
            tiebreak.Continue();
        }
        Check(tiebreak.Phase == DuelPhase.GameOver && tiebreak.Winner == 0, "Complete match decided by blocks");
        Check(tiebreak.Blocks(0) == 3 && tiebreak.Blocks(1) == 0, "Blocks credited to defender only");
        Check(WarCryRules.Power(-80) == 0 && WarCryRules.Power(-35) == 0, "Silence and threshold give no cry bonus");
        Check(Math.Abs(WarCryRules.Power(-21.5f) - 0.5f) < 0.001f, "Decibels scale cry power linearly");
        Check(WarCryRules.Power(0) == 1 && WarCryRules.Power(float.NaN) == 0, "Cry power bounded and NaN safe");
        Check(DuelMatch.CalculateDamage(sword, sword, false, 1) == 2 * DuelMatch.CalculateDamage(sword, sword, false), "Full cry doubles damage");
        Check(DuelMatch.CalculateDamage(Card("sword", WeaponClass.Light, 25, .5),
            Card("sword2", WeaponClass.Light, 25, .5), true, 1) == 25, "Cry and block applied before rounding");
        var cry = new DuelMatch();
        cry.Equip(0, Card("sword", WeaponClass.Light, 20, .5));
        cry.Equip(1, Card("sword2", WeaponClass.Light, 20, .5));
        Check(!cry.Record(1, GestureSlot.Attack, AttackDirection.Up, 1) && cry.WarCryPower(1) == 0, "Out of turn cannot charge cry");
        Check(cry.Record(0, GestureSlot.Attack, AttackDirection.Up, 1), "Store attack cry");
        cry.Record(0, GestureSlot.Attack, AttackDirection.Up, .5f);
        Check(cry.WarCryPower(0) == .5f, "Redo replaces cry power");
        cry.ConfirmStep();
        cry.Record(1, GestureSlot.Defense, AttackDirection.Down, 1);
        Check(cry.WarCryPower(1) == 0, "Defense cannot gain cry power");
        cry.ConfirmStep();
        cry.Record(1, GestureSlot.Attack, AttackDirection.Down, 1);
        cry.ConfirmStep();
        cry.Record(0, GestureSlot.Defense, AttackDirection.Up);
        cry.ConfirmStep();
        cry.Fight();
        Check(cry.Exchanges[0].damage == 30 && cry.Exchanges[1].damage == 40, "Independent cries affect actual exchanges");
        cry.FinishClash();
        cry.Continue();
        Check(cry.WarCryPower(0) == 0 && cry.WarCryPower(1) == 0, "Cry resets between rounds");

        Console.WriteLine($"PASS: {assertions} assertions");
        return 0;
    }
}
