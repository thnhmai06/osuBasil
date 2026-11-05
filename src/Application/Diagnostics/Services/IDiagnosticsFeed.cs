using Basil.Application.Common.Diagnostics;

namespace Basil.Application.Diagnostics.Services;

/// <summary>Distributes the sample of each second to whoever watches; it keeps no history.</summary>
public interface IDiagnosticsFeed
{
	/// <summary>Follows the samples from now on.</summary>
	/// <param name="category">The category to follow, or <see langword="null" /> for every category.</param>
	/// <param name="cancellationToken">A token that ends the watching.</param>
	/// <returns>
	///     The samples as they are taken. A watcher that falls behind skips to the newest sample and never delays the
	///     others.
	/// </returns>
	/// <exception cref="KeyNotFoundException"><paramref name="category" /> is not one of the categories.</exception>
	IAsyncEnumerable<DiagnosticsSample> Watch(string? category = null, CancellationToken cancellationToken = default);
}