using Microsoft.Extensions.Logging;

namespace Basil.Host.Cli.Dashboard.Data;

/// <summary>One line of the server's log as the dashboard keeps it.</summary>
/// <param name="Time">When it was logged.</param>
/// <param name="Level">How serious it is.</param>
/// <param name="Category">What logged it.</param>
/// <param name="Message">The text, with the exception after it when there is one.</param>
public sealed record LogLine(DateTimeOffset Time, LogLevel Level, string Category, string Message);

/// <summary>Keeps the most recent log lines for the dashboard; a logging provider, so it sees what every part logs.</summary>
public sealed class LogBuffer : ILoggerProvider
{
	private readonly TimeProvider _clock;
	private readonly int _capacity;
	private readonly Queue<LogLine> _lines = new();
	private readonly Lock _gate = new();

	/// <summary>Creates a buffer.</summary>
	/// <param name="clock">The clock that stamps the lines.</param>
	/// <param name="capacity">How many lines are kept.</param>
	public LogBuffer(TimeProvider clock, int capacity = 2000)
	{
		_clock = clock;
		_capacity = capacity;
	}

	/// <summary>Gets the lines, oldest first.</summary>
	public IReadOnlyList<LogLine> Lines()
	{
		lock (_gate) return [.. _lines];
	}

	/// <summary>Adds a line.</summary>
	public void Add(LogLine line)
	{
		lock (_gate)
		{
			_lines.Enqueue(line);
			while (_lines.Count > _capacity) _lines.Dequeue();
		}
	}

	/// <inheritdoc />
	public ILogger CreateLogger(string categoryName)
	{
		return new BufferLogger(this, categoryName, _clock);
	}

	/// <inheritdoc />
	public void Dispose()
	{
	}

	private sealed class BufferLogger(LogBuffer buffer, string category, TimeProvider clock) : ILogger
	{
		public IDisposable? BeginScope<TState>(TState state) where TState : notnull
		{
			return null;
		}

		public bool IsEnabled(LogLevel logLevel)
		{
			return logLevel != LogLevel.None;
		}

		public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
			Func<TState, Exception?, string> formatter)
		{
			var text = formatter(state, exception);
			if (exception is not null) text += " " + exception.GetType().Name + ": " + exception.Message;
			buffer.Add(new LogLine(clock.GetUtcNow(), logLevel, category, text));
		}
	}
}

/// <summary>One chat message as the chat tab shows it.</summary>
/// <param name="Time">When it was posted.</param>
/// <param name="Channel">The channel it was posted in.</param>
/// <param name="Author">Who posted it.</param>
/// <param name="Text">The text.</param>
public sealed record ChatLine(DateTimeOffset Time, string Channel, string Author, string Text);

/// <summary>Keeps the most recent chat messages for the chat tab.</summary>
public sealed class ChatFeed(int capacity = 1000)
{
	private readonly Queue<ChatLine> _lines = new();
	private readonly Lock _gate = new();

	/// <summary>Gets the messages, oldest first.</summary>
	public IReadOnlyList<ChatLine> Lines()
	{
		lock (_gate) return [.. _lines];
	}

	/// <summary>Adds a message.</summary>
	public void Add(ChatLine line)
	{
		lock (_gate)
		{
			_lines.Enqueue(line);
			while (_lines.Count > capacity) _lines.Dequeue();
		}
	}
}
