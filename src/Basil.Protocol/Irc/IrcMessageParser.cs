namespace Basil.Protocol.Irc;

/// <summary>Parses a single raw IRC line (no trailing CRLF) into an <see cref="IrcMessage" />.</summary>
public static class IrcMessageParser
{
    public static bool TryParse(string line, out IrcMessage? message)
    {
        message = null;
        if (string.IsNullOrEmpty(line)) return false;

        var rest = line;
        string? prefix = null;
        if (rest[0] == ':')
        {
            var spaceIndex = rest.IndexOf(' ');
            if (spaceIndex < 0) return false;

            prefix = rest[1..spaceIndex];
            rest = rest[(spaceIndex + 1)..];
        }

        string? trailing = null;
        var colonIndex = rest.IndexOf(" :", StringComparison.Ordinal);
        if (colonIndex >= 0)
        {
            trailing = rest[(colonIndex + 2)..];
            rest = rest[..colonIndex];
        }
        else if (rest.StartsWith(':'))
        {
            trailing = rest[1..];
            rest = "";
        }

        var tokens = rest.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0) return false;

        var command = tokens[0];
        var parameters = new List<string>(tokens.Length);
        for (var i = 1; i < tokens.Length; i++) parameters.Add(tokens[i]);

        if (trailing is not null) parameters.Add(trailing);

        message = new IrcMessage(prefix, command, parameters);
        return true;
    }

    public static IrcMessage Parse(string line)
    {
        return TryParse(line, out var message)
            ? message!
            : throw new FormatException($"Malformed IRC line: \"{line}\"");
    }
}
