using Basil.Application.Models.Configurations;

namespace Basil.Application.Contracts.Repositories;

/// <summary>
///     The single source of truth for runtime-configurable settings that can change without a
///     restart.
/// </summary>
/// <remarks>
///     A section that has never been saved reads back as its own default value, never
///     <see langword="null" />.
/// </remarks>
public interface ISettingsRepository
{
	/// <summary>Gets the current value of a settings section.</summary>
	/// <typeparam name="T">The settings type to read.</typeparam>
	/// <param name="cancellationToken">A token that cancels the read.</param>
	/// <returns>The stored value, or <typeparamref name="T" />'s default value when never saved.</returns>
	Task<T> GetAsync<T>(CancellationToken cancellationToken = default) where T : IConfiguration;

	/// <summary>Durably stores a settings section, replacing any existing value.</summary>
	/// <typeparam name="T">The settings type to write.</typeparam>
	/// <param name="value">The value to store.</param>
	/// <param name="cancellationToken">A token that cancels the write.</param>
	Task SaveAsync<T>(T value, CancellationToken cancellationToken = default) where T : IConfiguration;
}