// Pure stroke rules for the duel pen: the stroke starts where the pen was held in the square
// and counts once the pen has travelled far enough along one clear axis. No speed limit.
public static class PenStroke
{
    // origin/tip: viewport coordinates (0-1, y up). aspect: screen width / height.
    // distance: minimum travel as a fraction of the shorter screen side.
    // dominance: how much the main axis must exceed the other one.
    public static AttackDirection? Classify(float originX, float originY, float tipX, float tipY,
        float aspect, float distance, float dominance)
    {
        // Express both axes in units of the shorter screen side, so a move up counts like a move sideways
        float dx = (tipX - originX) * (aspect >= 1 ? aspect : 1);
        float dy = (tipY - originY) * (aspect >= 1 ? 1 : 1 / aspect);
        float x = dx < 0 ? -dx : dx, y = dy < 0 ? -dy : dy;
        float main = x > y ? x : y, other = x > y ? y : x;
        if (main < distance || main < other * dominance) return null;
        if (x > y) return dx > 0 ? AttackDirection.Right : AttackDirection.Left;
        return dy > 0 ? AttackDirection.Up : AttackDirection.Down;
    }
}
