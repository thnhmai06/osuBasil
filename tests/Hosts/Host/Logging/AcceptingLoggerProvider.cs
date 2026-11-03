using Microsoft.Extensions.Logging;

namespace Basil.Host.Tests.Logging;

/// <summary>A logger provider that accepts every level, so that only the configured levels decide.</summary>
internal sealed class AcceptingLoggerProvider : ILoggerProvider
{
	public ILogger CreateLogger(string categoryName)
	{
		return new Accepting();
	}

	public void Dispose()
	{
	}

	private sealed class Accepting : ILogger
	{
		public IDisposable? BeginScope<TState>(TState state) where TState : notnull
		{
			return null;
		}

		public bool IsEnabled(LogLevel logLevel)
		{
			return true;
		}

		public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
			Func<TState, Exception?, string> formatter)
		{
		}
	}
}