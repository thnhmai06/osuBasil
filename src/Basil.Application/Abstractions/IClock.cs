namespace Basil.Application.Abstractions;

/// <summary>Abstracts wall-clock time for testability. Ported from Python's time.time()/datetime.now() call sites.</summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}