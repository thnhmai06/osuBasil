using Basil.Application.Abstractions.Users;

namespace Basil.Infrastructure;

/// <inheritdoc cref="ITokenGenerator" />
public sealed class GuidTokenGenerator : ITokenGenerator
{
    public string GenerateToken()
    {
        return Guid.NewGuid().ToString();
    }
}