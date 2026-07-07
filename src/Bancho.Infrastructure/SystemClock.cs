using Bancho.Application.Abstractions;

namespace Bancho.Infrastructure;

/// <inheritdoc cref="IClock" />
public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}