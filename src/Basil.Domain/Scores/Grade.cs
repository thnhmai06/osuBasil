namespace Basil.Domain.Scores;

/// <summary>
///     Ported from app/objects/score.py's Grade. Deliberately in the opposite numeric order from
///     osu!'s own grade ordering, so that &lt;/&gt; comparisons read naturally (XH is the highest grade).
/// </summary>
public enum Grade : byte
{
    N = 0,
    F = 1,
    D = 2,
    C = 3,
    B = 4,
    A = 5,
    S = 6, // S
    Sh = 7, // HD S
    X = 8, // SS
    Xh = 9 // HD SS
}