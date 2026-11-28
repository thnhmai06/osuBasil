using Microsoft.Extensions.Primitives;

namespace Basil.Configuration;

public sealed class ConfigurationWatcher(IConfiguration configuration, ILogger<ConfigurationWatcher> logger)
	: IHostedService, IDisposable
{
	public static readonly IReadOnlyList<string> RestartRequired =
	[
		"Kestrel", "ConnectionStrings", "OpenTelemetry", "Logging:File", "Basil:Server", "Basil:Database",
		"Basil:Storage", "Basil:Assets"
	];

	public static readonly IReadOnlyList<string> Live = ["Logging:LogLevel", "Basil:Updates"];

	private readonly Dictionary<string, string> _applied = [];
	private readonly Lock _gate = new();
	private readonly SortedSet<string> _pending = [];
	private IDisposable? _registration;

	public IReadOnlyList<string> PendingRestart
	{
		get
		{
			lock (_gate)
			{
				return [.. _pending];
			}
		}
	}

	public void Dispose()
	{
		_registration?.Dispose();
	}

	public Task StartAsync(CancellationToken ct)
	{
		lock (_gate)
		{
			foreach (var section in RestartRequired.Concat(Live)) _applied[section] = Snapshot(section);
		}

		_registration = ChangeToken.OnChange(configuration.GetReloadToken, Changed);
		return Task.CompletedTask;
	}

	public Task StopAsync(CancellationToken ct)
	{
		Dispose();
		return Task.CompletedTask;
	}

	private void Changed()
	{
		lock (_gate)
		{
			foreach (var section in RestartRequired)
			{
				var current = Snapshot(section);
				if (current == _applied[section])
				{
					_pending.Remove(section);
					continue;
				}

				if (_pending.Add(section))
					logger.LogWarning(
						"The {Section} configuration changed; the change is applied after the server restarts",
						section);
			}

			foreach (var section in Live)
			{
				var current = Snapshot(section);
				if (current == _applied[section]) continue;

				_applied[section] = current;
				logger.LogInformation("The {Section} configuration changed and applies now", section);
			}
		}
	}

	private string Snapshot(string section)
	{
		return string.Join('\n', configuration.GetSection(section).AsEnumerable(true)
			.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => pair.Key + "=" + pair.Value));
	}
}