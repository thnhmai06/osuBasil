namespace Basil.Application.Servers.Runtime;

/// <summary>What the server's startup recovery did, for the host to report.</summary>
/// <param name="OpenedChannels">The general channels that were opened.</param>
/// <param name="BotOnline">Whether BasilBot is online once the recovery is done.</param>
/// <param name="ClosedMatches">The matches left unfinished that were ended.</param>
public sealed record ServerRecovery(int OpenedChannels, bool BotOnline, int ClosedMatches);