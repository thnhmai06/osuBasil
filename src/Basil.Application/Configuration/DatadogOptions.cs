namespace Basil.Application.Configuration;

/// <summary>
///     Ports DATADOG_API_KEY / DATADOG_APP_KEY from app/settings.py. bancho.py treats Datadog as
///     enabled only when both keys are non-empty strings (see app/state/services.py) — <see cref="IsEnabled" />
///     preserves that exact behavior.
/// </summary>
public sealed class DatadogOptions
{
    public const string SectionName = "Datadog";

    public string ApiKey { get; init; } = string.Empty;
    public string AppKey { get; init; } = string.Empty;

    public bool IsEnabled => !string.IsNullOrEmpty(ApiKey) && !string.IsNullOrEmpty(AppKey);
}