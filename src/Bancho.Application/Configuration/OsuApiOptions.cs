namespace Bancho.Application.Configuration;

/// <summary>Ports OSU_API_KEY from app/settings.py — optional; osu!api features are disabled when null.</summary>
public sealed class OsuApiOptions
{
    public const string SectionName = "OsuApi";

    public string? ApiKey { get; init; }
}
