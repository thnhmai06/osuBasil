using Bancho.Application.Abstractions.Users;

namespace Bancho.Infrastructure;

/// <inheritdoc cref="ITokenGenerator" />
public sealed class GuidTokenGenerator : ITokenGenerator
{
    public string GenerateToken()
    {
        return Guid.NewGuid().ToString();
    }
}