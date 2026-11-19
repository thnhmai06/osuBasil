using Microsoft.Extensions.Logging;

namespace Basil.Host.Cli.Dashboard.Data;

public sealed record LogLine(DateTimeOffset Time, LogLevel Level, string Category, string Message);

public sealed class LogBuffer : ILoggerProvider
{
	private readonly int _capacity;
	private readonly TimeProvider _clock;
	private readonly Lock _gate = new();
	private readonly Queue<LogLine> _lines = new();
	private long _total;

	public LogBuffer(TimeProvider clock, int capacity = 2000)
	{
		_clock = clock;
		_capacity = capacity;
	}

	public ILogger CreateLogger(string categoryName)
	{
		return new BufferLogger(this, categoryName, _clock);
	}

	public void Dispose()
	{
	}

	public IReadOnlyList<LogLine> Lines()
	{
		lock (_gate)
		{
			return [.. _lines];
		}
	}

	public IReadOnlyList<LogLine> Since(long seen, out long total)
	{
		lock (_gate)
		{
			total = _total;
			var fresh = (int)Math.Min(_total - seen, _lines.Count);
			return fresh <= 0 ? [] : [.. _lines.Skip(_lines.Count - fresh)];
		}
	}

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

public sealed record ChatLine(DateTimeOffset Time, string Channel, string Author, string Text);

public sealed class ChatFeed(int capacity = 500, int maxChannels = 200)
{
	private readonly Dictionary<string, Channel> _channels = new(StringComparer.OrdinalIgnoreCase);
	private readonly Lock _gate = new();
	private long _revision;
	private long _stamp;

	public long Revision => Interlocked.Read(ref _revision);

	public IReadOnlyList<string> Channels()
	{
		lock (_gate)
		{
			return [.. _channels.Keys.Order()];
		}
	}

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