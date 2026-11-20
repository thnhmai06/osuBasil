namespace Basil.Application.Servers.Objects;

public sealed record ServerRecovery(int OpenedChannels, bool BotOnline, int ClosedMatches, int ClosedConnections);