namespace Basil.Application.Configuration;

/// <summary>
///     Ports CAPTCHA_PROVIDER / CAPTCHA_SECRET from app/settings.py. Provider is one of
///     "recaptcha", "hcaptcha", "turnstile", or null when captcha verification is disabled.
/// </summary>
public sealed class CaptchaOptions
{
    public const string SectionName = "Captcha";

    public string? Provider { get; init; }
    public string? Secret { get; init; }
}