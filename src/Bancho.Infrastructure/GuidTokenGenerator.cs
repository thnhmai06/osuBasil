using Bancho.Application.Abstractions;
using Bancho.Application.Abstractions.Users;

namespace Bancho.Infrastructure;

/// <inheritdoc cref="ITokenGenerator" />
public sealed class GuidTokenGenerator : ITokenGenerator
{
    public string GenerateToken() => Guid.NewGuid().ToString();
}
