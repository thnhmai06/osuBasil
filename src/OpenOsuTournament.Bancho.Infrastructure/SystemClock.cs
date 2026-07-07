using OpenOsuTournament.Bancho.Application.Abstractions;

namespace OpenOsuTournament.Bancho.Infrastructure;

/// <inheritdoc cref="IClock" />
public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}