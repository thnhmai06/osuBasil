namespace Bancho.Application.Configuration;

/// <summary>Ports DISCORD_AUDIT_LOG_WEBHOOK / DISCORD_INVITE from app/settings.py.</summary>
public sealed class DiscordOptions
{
    public const string SectionName = "Discord";

    public required string AuditLogWebhookUrl { get; init; }
    public required string InviteUrl { get; init; }
}