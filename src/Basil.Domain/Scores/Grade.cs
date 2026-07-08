namespace Basil.Domain.Scores;

/// <summary>
///     Ported from app/objects/score.py's Grade. Deliberately in the opposite numeric order from
///     osu!'s own grade ordering, so that &lt;/&gt; comparisons read naturally (XH is the highest grade).
/// </summary>
public enum Grade
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

/// <summary>Ported from Grade.from_str.</summary>
public static class GradeExtensions
{
    public static Grade Parse(string s)
    {
        return s.ToLowerInvariant() switch
        {
            "xh" => Grade.Xh,
            "x" => Grade.X,
            "sh" => Grade.Sh,
            "s" => Grade.S,
            "a" => Grade.A,
            "b" => Grade.B,
            "c" => Grade.C,
            "d" => Grade.D,
            "f" => Grade.F,
            "n" => Grade.N,
            _ => throw new ArgumentOutOfRangeException(nameof(s), s, "Unknown grade string.")
        };
    }
}