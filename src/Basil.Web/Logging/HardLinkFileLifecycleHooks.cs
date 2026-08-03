using System.Text;
using Serilog.Sinks.File;
using HardLink = Basil.Infrastructure.System.HardLink;

namespace Basil.Web.Logging;

/// <summary>
///     Recreates a fixed "latest" hardlink pointing at whichever file Serilog's rolling file sink
///     just opened. Runs once per file-open (daily rollover or process start), not per log line. A
///     hardlink can't be updated in place, only deleted and recreated, which
///     <see cref="Infrastructure.System.HardLink.Create" /> already does via its default
///     <c>force: true</c>.
/// </summary>
/// <param name="latestLinkPath">The fixed path of the "latest" hardlink to recreate on each file open.</param>
public sealed class HardLinkFileLifecycleHooks(string latestLinkPath) : FileLifecycleHooks
{
	/// <summary>
	///     Recreates the configured "latest" hardlink to point at the newly opened log file, then
	///     delegates to the base implementation.
	/// </summary>
	/// <param name="path">The path of the log file the rolling file sink just opened.</param>
	/// <param name="underlyingStream">The stream opened for the log file.</param>
	/// <param name="encoding">The encoding configured for writing to the log file.</param>
	/// <returns>The stream the base implementation returns for continued writing.</returns>
	public override Stream OnFileOpened(string path, Stream underlyingStream, Encoding encoding)
	{
		HardLink.Create(latestLinkPath, path);
		return base.OnFileOpened(path, underlyingStream, encoding);
	}
}