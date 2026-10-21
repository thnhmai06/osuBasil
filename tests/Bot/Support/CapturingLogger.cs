using Microsoft.Extensions.Logging;

namespace Basil.Bot.Tests.Support;

/// <summary>A logger that keeps what it is told, to check what an operator would be told.</summary>
/// <typeparam name="T">The category of the logger.</typeparam>
internal sealed class CapturingLogger<T> : ILogger<T>
{
	/// <summary>Gets the level and rendered message of everything logged, in order.</summary>
	public List<(LogLevel Level, string Message)> Entries { get; } = [];

	/// <inheritdoc />
	public IDisposable? BeginScope<TState>(TState state) where TState : notnull
	{
		return null;
	}

	/// <inheritdoc />
	public bool IsEnabled(LogLevel logLevel)
	{
		return true;
	}

	/// <inheritdoc />
	public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
		Func<TState, Exception?, string> formatter)
	{
		Entries.Add((logLevel, formatter(state, exception)));
	}
}
