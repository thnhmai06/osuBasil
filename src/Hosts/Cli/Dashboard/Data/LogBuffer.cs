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
	private readonly int _capacity;
	private readonly TimeProvider _clock;
	private readonly Lock _gate = new();
	private readonly Queue<LogLine> _lines = new();
	private long _total;

	/// <summary>Creates a buffer.</summary>
	/// <param name="clock">The clock that stamps the lines.</param>
	/// <param name="capacity">How many lines are kept.</param>
	public LogBuffer(TimeProvider clock, int capacity = 2000)
	{
		_clock = clock;
		_capacity = capacity;
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

	/// <summary>Gets the lines, oldest first.</summary>
	public IReadOnlyList<LogLine> Lines()
	{
		lock (_gate)
		{
			return [.. _lines];
		}
	}

	/// <summary>Gets the lines added after the first <paramref name="seen" /> ones, which the buffer still holds.</summary>
	/// <param name="seen">How many lines the caller has already taken (the <paramref name="total" /> of its last call).</param>
	/// <param name="total">How many lines have been added in all.</param>
	public IReadOnlyList<LogLine> Since(long seen, out long total)
	{
		lock (_gate)
		{
			total = _total;
			var fresh = (int)Math.Min(_total - seen, _lines.Count);
			return fresh <= 0 ? [] : [.. _lines.Skip(_lines.Count - fresh)];
		}
	}

	/// <summary>Adds a line.</summary>
	public void Add(LogLine line)
	{
		lock (_gate)
		{
			_total++;
			_lines.Enqueue(line);
			while (_lines.Count > _capacity) _lines.Dequeue();
		}
	}

	private sealed class BufferLogger(LogBuffer buffer, string category, TimeProvider clock) : ILogger
	{
		/// <inheritdoc />
		public IDisposable? BeginScope<TState>(TState state) where TState : notnull
		{
			return null;
		}

		/// <inheritdoc />
		public bool IsEnabled(LogLevel logLevel)
		{
			return logLevel != LogLevel.None;
		}

		/// <inheritdoc />
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

/// <summary>Keeps the most recent chat messages of each channel since the server started, for the chat tab.</summary>
/// <param name="capacity">How many messages each channel keeps.</param>
/// <param name="maxChannels">How many channels are kept; beyond it the one that was quiet the longest is forgotten.</param>
public sealed class ChatFeed(int capacity = 500, int maxChannels = 200)
{
	private readonly Dictionary<string, Channel> _channels = new(StringComparer.OrdinalIgnoreCase);
	private readonly Lock _gate = new();
	private long _stamp;
	private long _revision;

	/// <summary>Gets a number that changes whenever a channel gains its first message or is forgotten.</summary>
	public long Revision => Interlocked.Read(ref _revision);

	/// <summary>Gets the names of the channels that have messages.</summary>
	public IReadOnlyList<string> Channels()
	{
		lock (_gate)
		{
			return [.. _channels.Keys.Order()];
		}
	}

	/// <summary>Gets the messages of a channel added after the first <paramref name="seen" /> ones that it still holds.</summary>
	/// <param name="channel">The channel name.</param>
	/// <param name="seen">How many messages the caller has taken (the <paramref name="total" /> of its last call).</param>
	/// <param name="total">How many messages the channel has had in all.</param>
	public IReadOnlyList<ChatLine> Since(string channel, long seen, out long total)
	{
		lock (_gate)
		{
			if (!_channels.TryGetValue(channel, out var entry))
			{
				total = 0;
				return [];
			}

			total = entry.Total;
			var fresh = (int)Math.Min(entry.Total - seen, entry.Lines.Count);
			return fresh <= 0 ? [] : [.. entry.Lines.Skip(entry.Lines.Count - fresh)];
		}
	}

	/// <summary>Adds a message.</summary>
	public void Add(ChatLine line)
	{
		lock (_gate)
		{
			if (!_channels.TryGetValue(line.Channel, out var entry))
			{
				if (_channels.Count >= maxChannels)
					_channels.Remove(_channels.MinBy(pair => pair.Value.Stamp).Key);

				_channels[line.Channel] = entry = new Channel();
				Interlocked.Increment(ref _revision);
			}

			entry.Stamp = ++_stamp;
			entry.Total++;
			entry.Lines.Enqueue(line);
			while (entry.Lines.Count > capacity) entry.Lines.Dequeue();
		}
	}

	private sealed class Channel
	{
		public Queue<ChatLine> Lines { get; } = new();

		public long Total { get; set; }

		public long Stamp { get; set; }
	}
}