using Basil.Application.Beatmaps.Messages;
using Basil.Application.Beatmaps.Objects;
using Basil.Application.Beatmaps.Services;
using Basil.Application.Common;
using Mediator;

namespace Basil.Infrastructure.Beatmaps.Handlers;

/// <summary>Handles <see cref="GetRulesetVersion" /> with the privileges of the sending actor.</summary>
internal sealed class GetRulesetVersionHandler(Rulesets rulesets)
	: IRequestHandler<GetRulesetVersion, (Result<RulesetOutcome> Result, RulesetVersions? Versions)>
{
	/// <inheritdoc />
	public ValueTask<(Result<RulesetOutcome> Result, RulesetVersions? Versions)> Handle(GetRulesetVersion request,
		CancellationToken ct)
	{
		return ValueTask.FromResult(rulesets.ScopeTo(request.Actor).Version());
	}
}