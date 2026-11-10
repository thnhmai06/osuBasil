using Basil.Application.Content.Messages;
using Basil.Application.Content.Persistence;
using Basil.Application.Content.Storage;
using Basil.Domain.Content;
using Mediator;

namespace Basil.Infrastructure.Content.Handlers;

/// <summary>Answers <see cref="ReadSettings" /> with the settings in force.</summary>
internal sealed class ReadSettingsHandler(ISettingsRepository settings) : IRequestHandler<ReadSettings, ServerSettings>
{
	/// <inheritdoc />
	public async ValueTask<ServerSettings> Handle(ReadSettings request, CancellationToken ct)
	{
		return await settings.GetAsync(ct);
	}
}