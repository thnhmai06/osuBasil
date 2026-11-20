namespace Basil.Application.Content.Ports;

public interface IScreenshotInspector
{
	Task<ScreenshotInfo?> InspectAsync(Stream content, CancellationToken ct = default);
}

public sealed record ScreenshotInfo(string Extension, int Width, int Height);