namespace XingShuRuntime.Targeting
{
    // Turn IDs can briefly be zero during animation. Keep the observed attack
    // until a real next turn or next round, rather than selecting another tile.
    internal sealed class MonsterAttackWindow
    {
        private int _round = -1;
        private long _turn, _attacker;
        internal void ObserveTurn(int round, long turn)
        {
            if (round != _round) { Clear(); _round = round; }
            if (turn != 0 && turn != _turn) { _turn = turn; _attacker = 0; }
        }
        internal void ObserveAttack(long monster) { if (monster != 0 && monster == _turn) _attacker = monster; }
        internal bool HasAttacked(long monster, int round) { return round == _round && monster != 0 && monster == _attacker; }
        internal void Clear() { _round = -1; _turn = _attacker = 0; }
    }
}
