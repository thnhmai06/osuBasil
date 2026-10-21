using Basil.Domain.Permissions;

namespace Basil.Application.Runtime.Tests.Common;

public class ResultTests
{
	private enum SampleOutcome : byte
	{
		Ok = 1,
		NotFound = 2
	}

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
	public void Denied_NamesTheMissingPermissionsAndIsNotOk()
	{
		var result = Result<SampleOutcome>.Denied(Permissions.RoomJoin | Permissions.ChatPost);

		Assert.False(result.IsOk);
		Assert.Equal(Permissions.RoomJoin | Permissions.ChatPost, result.Missing);
	}

	[Fact]
	public void Denied_EvenWithOkOutcome_IsNotOk()
	{
		var result = new Result<SampleOutcome>(SampleOutcome.Ok, Permissions.RoomJoin);

		Assert.False(result.IsOk);
	}

	[Fact]
	public void Denied_NoPermissions_Throws()
	{
		Assert.Throws<ArgumentOutOfRangeException>(() => Result<SampleOutcome>.Denied(Permissions.None));
	}

	[Fact]
	public void ImplicitConversion_FromOutcome_HasNothingMissing()
	{
		Result<SampleOutcome> result = SampleOutcome.NotFound;

		Assert.Null(result.Missing);
	}
}
