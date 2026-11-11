using Basil.Application.Common.Settings;
using Mediator;

namespace Basil.Infrastructure.Common.Handlers;

/// <summary>Answers <see cref="ReadSettings" /> with the settings in force.</summary>
internal sealed class ReadSettingsHandler(ISettingsRepository settings) : IRequestHandler<ReadSettings, ServerSettings>
{
	/// <inheritdoc />
	public async ValueTask<ServerSettings> Handle(ReadSettings request, CancellationToken ct)
	{
		return await settings.GetAsync(ct);
	}
}