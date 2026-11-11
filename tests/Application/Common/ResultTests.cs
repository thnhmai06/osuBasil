using Basil.Domain.Users;

namespace Basil.Application.Common.Tests;

public class ResultTests
{
	[Fact]
	public void OkNothingMissing_True()
	{
		Result<SampleOutcome> result = SampleOutcome.Ok;

		Assert.True(result.IsOk);
	}

	[Fact]
	public void DefaultResult_IsFalse()
	{
		Assert.False(default(Result<SampleOutcome>).IsOk);
	}

	[Fact]
	public void OtherOutcome_IsFalse()
	{
		Result<SampleOutcome> result = SampleOutcome.NotFound;

		Assert.False(result.IsOk);
	}

	[Fact]
	public void Denied_MissingNotOk()
	{
		var result = Result<SampleOutcome>.Denied(UserPrivileges.Player | UserPrivileges.Player);

		Assert.False(result.IsOk);
		Assert.Equal(UserPrivileges.Player | UserPrivileges.Player, result.Missing);
	}

	[Fact]
	public void DeniedEvenOk_NotOk()
	{
		var result = new Result<SampleOutcome>(SampleOutcome.Ok, UserPrivileges.Player);

		Assert.False(result.IsOk);
	}

	[Fact]
	public void NoPrivileges_Throws()
	{
		Assert.Throws<ArgumentOutOfRangeException>(() => Result<SampleOutcome>.Denied(UserPrivileges.None));
	}

	[Fact]
	public void FromOutcome_HasNothingMissing()
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