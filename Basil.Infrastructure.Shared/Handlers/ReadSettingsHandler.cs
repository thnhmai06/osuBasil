using Basil.Application.Shared.Settings;
using Mediator;

namespace Basil.Infrastructure.Shared.Handlers;

internal sealed class ReadSettingsHandler(ISettingsRepository settings) : IRequestHandler<ReadSettings, ServerSettings>
{
	public async ValueTask<ServerSettings> Handle(ReadSettings request, CancellationToken ct)
	{
		return await settings.GetAsync(ct);
	}
}