using System.Collections.Concurrent;
using Basil.Application.Abstractions.Settings;

namespace Basil.IntegrationTests;

/// <summary>
///     A real, stateful <see cref="ISettingsRepository" /> backed by a dictionary, for tests that
///     need read-your-writes behavior (e.g. <c>PUT /adminkey</c> followed by <c>GET /adminkey</c>)
///     rather than the fixed canned responses <see cref="TestDoubles" />'s substitutes return.
/// </summary>
public sealed class InMemorySettingsRepository : ISettingsRepository
{
	private readonly ConcurrentDictionary<string, string?> _values = new();

	public InMemorySettingsRepository Seed(string key, string? value)
	{
		_values[key] = value;
		return this;
	}

	public Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
	{
		return Task.FromResult(_values.GetValueOrDefault(key));
	}

	public Task SetAsync(string key, string? value, CancellationToken cancellationToken = default)
	{
		_values[key] = value;
		return Task.CompletedTask;
	}
}
