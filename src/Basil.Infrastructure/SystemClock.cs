using Basil.Application.Abstractions;

namespace Basil.Infrastructure;

/// <inheritdoc cref="IClock" />
public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}