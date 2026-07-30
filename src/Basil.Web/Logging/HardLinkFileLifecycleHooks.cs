using System.Text;
using Serilog.Sinks.File;
using HardLink = Basil.Infrastructure.System.HardLink;

namespace Basil.Web.Logging;

/// <summary>
///     Recreates a fixed "latest" hardlink pointing at whichever file Serilog's rolling file sink
///     just opened. Runs once per file-open (daily rollover or process start), not per log line —
///     a hardlink can't be updated in place, only deleted and recreated, which <see cref="Infrastructure.System.HardLink.Create" />
///     already does via its default <c>force: true</c>.
/// </summary>
public sealed class HardLinkFileLifecycleHooks(string latestLinkPath) : FileLifecycleHooks
{
	public override Stream OnFileOpened(string path, Stream underlyingStream, Encoding encoding)
	{
		HardLink.Create(latestLinkPath, path);
		return base.OnFileOpened(path, underlyingStream, encoding);
	}
}