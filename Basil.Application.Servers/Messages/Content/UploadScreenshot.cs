using Basil.Application.Servers.Objects;
using Basil.Application.Sessions.Objects;
using Basil.Domain.Content;
using Mediator;

namespace Basil.Application.Servers.Messages.Content;

public sealed record UploadScreenshot(Session Actor, Stream Image)
	: IRequest<(ContentReference? Screenshot, ServerActionResult Result)>;