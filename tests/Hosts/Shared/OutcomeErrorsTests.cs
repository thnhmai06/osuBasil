using Basil.Application.Servers.Objects;

namespace Basil.Host.Shared.Tests;

/// <summary>A refused operation is reported with a stable camelCase code and a classification.</summary>
public sealed class OutcomeErrorsTests
{
	[Theory]
	[InlineData(ServerOutcome.NameTaken, "nameTaken", ErrorKind.Conflict)]
	[InlineData(ServerOutcome.Locked, "locked", ErrorKind.Locked)]
	public void KnownOutcome_NamedAndClassified(ServerOutcome outcome, string code, ErrorKind kind)
	{
		Assert.Equal(new ErrorInfo(code, kind), OutcomeErrors.Describe(outcome));
	}

	[Fact]
	public void MissingTable_Throws()
	{
		Assert.Throws<ArgumentException>(() => OutcomeErrors.Describe(DayOfWeek.Monday));
	}
}