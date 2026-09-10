namespace Basil.Server.Features.Diagnostics;

/// <summary>How many exceptions have been thrown anywhere in the process.</summary>
/// <param name="TotalThrown">
///     The cumulative number of exceptions thrown since the process started, whether or not they
///     were ultimately caught and handled.
/// </param>
public sealed record ExceptionsSample(long TotalThrown);

/// <summary>Samples the process's cumulative thrown-exception count.</summary>
/// <remarks>
///     There is no way to point-sample this from cold: the runtime only reports it as a push-based
///     meter measurement at the moment each exception is thrown, so the count this reports is
///     whatever <see cref="RuntimeMeterListener" /> has accumulated since it started listening, not
///     necessarily since the process itself started.
/// </remarks>
public sealed class ExceptionsSampler(RuntimeMeterListener meterListener)
{
	/// <summary>Takes a fresh sample of the process's cumulative thrown-exception count.</summary>
	public ExceptionsSample Sample() => new(meterListener.ExceptionsThrown);
}
