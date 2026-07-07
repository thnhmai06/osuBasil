namespace Bancho.Application.Configuration;

/// <summary>Ports PP_CACHED_ACCS from app/settings.py.</summary>
public sealed class PerformanceOptions
{
    public const string SectionName = "Performance";

    public IReadOnlyList<int> CachedAccuracies { get; init; } = [];
}