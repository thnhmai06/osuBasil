using Basil.Domain.Users;

namespace Basil.Application.Common.Tests;

public class ResultTests
{
	[Fact]
	public void IsOk_OutcomeOkWithNothingMissing_IsTrue()
	{
		Result<SampleOutcome> result = SampleOutcome.Ok;

		Assert.True(result.IsOk);
	}

	[Fact]
	public void IsOk_DefaultResult_IsFalse()
	{
		Assert.False(default(Result<SampleOutcome>).IsOk);
	}

	[Fact]
	public void IsOk_OtherOutcome_IsFalse()
	{
		Result<SampleOutcome> result = SampleOutcome.NotFound;

		Assert.False(result.IsOk);
	}

	[Fact]
	public void Denied_NamesTheMissingPrivilegesAndIsNotOk()
	{
		var result = Result<SampleOutcome>.Denied(UserPrivileges.Player | UserPrivileges.Player);

		Assert.False(result.IsOk);
		Assert.Equal(UserPrivileges.Player | UserPrivileges.Player, result.Missing);
	}

	[Fact]
	public void Denied_EvenWithOkOutcome_IsNotOk()
	{
		var result = new Result<SampleOutcome>(SampleOutcome.Ok, UserPrivileges.Player);

		Assert.False(result.IsOk);
	}

	[Fact]
	public void Denied_NoPermissions_Throws()
	{
		Assert.Throws<ArgumentOutOfRangeException>(() => Result<SampleOutcome>.Denied(UserPrivileges.None));
	}

	[Fact]
	public void ImplicitConversion_FromOutcome_HasNothingMissing()
	{
		Result<SampleOutcome> result = SampleOutcome.NotFound;

		Assert.Null(result.Missing);
	}

	private enum SampleOutcome : byte
	{
		Ok = 1,
		NotFound = 2
	}
}