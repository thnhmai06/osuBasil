using Microsoft.Extensions.Primitives;

namespace Basil.Host.Configuration;

/// <summary>
///     Notices when the configuration files change: the sections that are read once at start wait for the next start and
///     are reported as pending, the others apply at once.
/// </summary>
/// <param name="configuration">The configuration, which is read again when a file changes.</param>
/// <param name="logger">Reports what a change does.</param>
public sealed class ConfigurationWatcher(IConfiguration configuration, ILogger<ConfigurationWatcher> logger)
	: IHostedService, IDisposable
{
	/// <summary>The sections that are read when the server starts.</summary>
	public static readonly IReadOnlyList<string> RestartRequired = ["Server", "Database", "Storage", "Telemetry"];

	/// <summary>The sections that apply as soon as they change.</summary>
	public static readonly IReadOnlyList<string> Live = ["Logging", "Updates"];

	private readonly Dictionary<string, string> _applied = [];
	private readonly Lock _gate = new();
	private readonly SortedSet<string> _pending = [];
	private IDisposable? _registration;

	/// <summary>Gets the sections whose configured value differs from the one the running server uses.</summary>
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

	/// <inheritdoc />
	public void Dispose()
	{
		_registration?.Dispose();
	}

	/// <inheritdoc />
	public Task StartAsync(CancellationToken cancellationToken)
	{
		lock (_gate)
		{
			foreach (var section in RestartRequired.Concat(Live)) _applied[section] = Snapshot(section);
		}

		_registration = ChangeToken.OnChange(configuration.GetReloadToken, Changed);
		return Task.CompletedTask;
	}

	/// <inheritdoc />
	public Task StopAsync(CancellationToken cancellationToken)
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
		return string.Join('\n', configuration.GetSection("Basil:" + section).AsEnumerable(true)
			.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => pair.Key + "=" + pair.Value));
	}
}