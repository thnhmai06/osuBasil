using Microsoft.Extensions.Options;
using OpenOsuTournament.Bancho.Application.Configuration;
using OpenOsuTournament.Bancho.Application.Sessions;
using OpenOsuTournament.Bancho.Application.Sessions.Multiplayer;

namespace OpenOsuTournament.Bancho.Application.UseCases.Bot;

/// <inheritdoc cref="ICommandDispatcher" />
public sealed class CommandDispatcher(IOptions<ServerBehaviorOptions> serverBehavior, MpCommandService mpCommands)
    : ICommandDispatcher
{
    private const string HelpText =
        "Commands: !roll [max]. In a match's own chat, referees can use !mp <subcommand> — try !mp help.";

    private const int RollMaxCap = 0x7FFF; // matches bancho.py's !roll cap

    public async Task<string?> DispatchAsync(PlayerSession sender, string rawMessage, MatchSession? matchScope,
        CancellationToken cancellationToken = default)
    {
        var prefix = serverBehavior.Value.CommandPrefix;
        if (string.IsNullOrEmpty(prefix) || !rawMessage.StartsWith(prefix, StringComparison.Ordinal)) return null;

        var parts = rawMessage[prefix.Length..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return null;

        var trigger = parts[0].ToLowerInvariant();
        var args = parts[1..];

        if (trigger == "mp")
        {
            if (matchScope is null) return null;

            var subcommand = args.Length > 0 ? args[0].ToLowerInvariant() : "";
            return await mpCommands.HandleAsync(sender, matchScope, subcommand, args[1..], cancellationToken);
        }

        return trigger switch
        {
            "help" => HelpText,
            "roll" => Roll(sender, args),
            _ => null
        };
    }

    private static string Roll(PlayerSession sender, IReadOnlyList<string> args)
    {
        var max = 100;
        if (args.Count > 0 && int.TryParse(args[0], out var parsed) && parsed > 0) max = Math.Min(parsed, RollMaxCap);

        var roll = Random.Shared.Next(0, max + 1);
        return $"{sender.Name} rolls {roll} point(s)";
    }
}
