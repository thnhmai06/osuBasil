using Basil.Server.Shared.Eventing;

namespace Basil.Server.Features.Diagnostics;

/// <summary>
///     The <see cref="ILiveEventHub" /> stream identifying each diagnostic live endpoint. Diagnostics
///     has no per-entity id the way a match's streams do, so every key shares the same
///     <see cref="StreamKey.Id" /> and is distinguished only by <see cref="StreamKey.Name" />.
/// </summary>
internal static class DiagnosticStreams
{
	private const string Category = "diagnostic";

	public static readonly StreamKey Process = new(Category, 0, "process");
	public static readonly StreamKey Gc = new(Category, 0, "gc");
	public static readonly StreamKey ThreadPool = new(Category, 0, "threadpool");
	public static readonly StreamKey Runtime = new(Category, 0, "runtime");
	public static readonly StreamKey Exceptions = new(Category, 0, "exceptions");
	public static readonly StreamKey Http = new(Category, 0, "http");
	public static readonly StreamKey Application = new(Category, 0, "application");
	public static readonly StreamKey Overview = new(Category, 0, "overview");
}
