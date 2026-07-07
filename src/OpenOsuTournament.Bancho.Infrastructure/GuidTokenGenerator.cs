using OpenOsuTournament.Bancho.Application.Abstractions.Users;

namespace OpenOsuTournament.Bancho.Infrastructure;

/// <inheritdoc cref="ITokenGenerator" />
public sealed class GuidTokenGenerator : ITokenGenerator
{
    public string GenerateToken()
    {
        return Guid.NewGuid().ToString();
    }
}