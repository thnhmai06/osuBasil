namespace Bancho.Protocol.Multiplayer;

/// <summary>Wire-shape for a live spectator scoreframe. Ported from ScoreFrame/write_scoreframe in app/packets.py.</summary>
public sealed record ScoreFrameData(
    int Time,
    int Id,
    int Num300,
    int Num100,
    int Num50,
    int NumGeki,
    int NumKatu,
    int NumMiss,
    int TotalScore,
    int MaxCombo,
    int CurrentCombo,
    bool Perfect,
    int CurrentHp,
    int TagByte,
    bool ScoreV2,
    double? ComboPortion = null,
    double? BonusPortion = null);
