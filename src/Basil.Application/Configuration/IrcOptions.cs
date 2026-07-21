namespace Basil.Application.Configuration;

/// <summary>Embedded IRC gateway (chat routed through the same core as bancho — see docs/architecture.md).</summary>
public sealed class IrcOptions
{
    public const string SectionName = "Basil:Irc";

    public string Name { get; init; } = "Basil";
    
    public int Port { get; init; } = 6667;
}
