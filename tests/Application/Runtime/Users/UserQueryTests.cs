using Basil.Application.Persistence.Users;
using Basil.Domain.Users;

namespace Basil.Application.Runtime.Tests.Users;

/// <summary>
///     Behaviour of the user search syntax and of narrowing a query to what an asker may see, from the XML contract of
///     <see cref="UserQuery" />: free text matches names, <c>country=</c> takes two-letter codes, <c>permission=</c> a
///     decimal mask, <c>deleted=</c> <c>1</c>, <c>0</c> or <c>*</c>; a filter that is unknown or does not parse stays in
///     the text.
/// </summary>
public sealed class UserQueryTests
{
	#region Parse: text

	[Fact]
	public void Parse_Null_Throws()
	{
		Assert.Throws<ArgumentNullException>(() => UserQuery.Parse(null!));
	}

	[Theory]
	[InlineData("")]
	[InlineData("   ")]
	public void Parse_EmptyOrBlank_IsAnUnfilteredQuery(string text)
	{
		var query = UserQuery.Parse(text);

		Assert.Null(query.Text);
		Assert.Null(query.Countries);
		Assert.Null(query.Permissions);
		Assert.Null(query.Deleted);
	}

	[Fact]
	public void Parse_PlainText_IsKeptAsTheText()
	{
		var query = UserQuery.Parse("bob");

		Assert.Equal("bob", query.Text);
		Assert.Null(query.Countries);
	}

	[Fact]
	public void Parse_TextWithRepeatedSpaces_CollapsesAndTrimsThem()
	{
		Assert.Equal("bob smith", UserQuery.Parse("  bob    smith  ").Text);
	}

	[Fact]
	public void Parse_QuotedPhraseWithoutKey_StaysInTheText()
	{
		Assert.Equal("\"bob smith\"", UserQuery.Parse("\"bob smith\"").Text);
	}

	[Fact]
	public void Parse_FilterBetweenWords_IsRemovedAndTheWordsJoined()
	{
		var query = UserQuery.Parse("alice country=us bob");

		Assert.Equal("alice bob", query.Text);
		Assert.Equal([Country.Us], query.Countries);
	}

	[Fact]
	public void Parse_TheDocumentedExample_ReadsEveryPart()
	{
		var query = UserQuery.Parse("country=usvn permission=4 deleted=* bob");

		Assert.Equal("bob", query.Text);
		Assert.Equal([Country.Us, Country.Vn], query.Countries);
		Assert.Equal((UserPermissions)4, query.Permissions);
		Assert.Null(query.Deleted);
	}

	[Fact]
	public void Parse_OnlyFilters_LeavesNoText()
	{
		Assert.Null(UserQuery.Parse("country=us permission=1 deleted=0").Text);
	}

	[Fact]
	public void Parse_IgnoresTheFormatProvider()
	{
		var turkish = System.Globalization.CultureInfo.GetCultureInfo("tr-TR");

		Assert.Equal([Country.Id], UserQuery.Parse("country=ID", turkish).Countries);
	}

	#endregion

	#region Parse: country

	[Theory]
	[InlineData("country=us")]
	[InlineData("country:us")]
	[InlineData("COUNTRY=US")]
	[InlineData("Country=Us")]
	public void Parse_CountryKeyOperatorAndCaseVariants_AreAllRead(string text)
	{
		var query = UserQuery.Parse(text);

		Assert.Equal([Country.Us], query.Countries);
		Assert.Null(query.Text);
	}

	[Fact]
	public void Parse_SeveralCodesJoinedTogether_ReadsEachInOrder()
	{
		Assert.Equal([Country.Vn, Country.Us, Country.Gb], UserQuery.Parse("country=vnusgb").Countries);
	}

	[Theory]
	[InlineData("country=u")]
	[InlineData("country=usv")]
	[InlineData("country=zz")]
	[InlineData("country=usqq")]
	[InlineData("country=u-")]
	public void Parse_CountryValueThatIsNotWholeKnownCodes_StaysInTheText(string text)
	{
		var query = UserQuery.Parse(text);

		Assert.Null(query.Countries);
		Assert.Equal(text, query.Text);
	}

	[Theory]
	[InlineData("country=00")]
	[InlineData("country=12")]
	[InlineData("country=99")]
	public void Parse_CountryValueOfDigits_IsNotACodeAndStaysInTheText(string text)
	{
		var query = UserQuery.Parse(text);

		Assert.Null(query.Countries);
		Assert.Equal(text, query.Text);
	}

	[Fact]
	public void Parse_CountryWithoutValue_StaysInTheText()
	{
		var query = UserQuery.Parse("country=");

		Assert.Null(query.Countries);
		Assert.Equal("country=", query.Text);
	}

	[Fact]
	public void Parse_QuotedCountryValue_IsUnquotedBeforeReading()
	{
		Assert.Equal([Country.Us], UserQuery.Parse("country=\"us\"").Countries);
	}

	[Theory]
	[InlineData("country<us")]
	[InlineData("country>=us")]
	public void Parse_CountryWithAComparisonOperator_StaysInTheText(string text)
	{
		var query = UserQuery.Parse(text);

		Assert.Null(query.Countries);
		Assert.Equal(text, query.Text);
	}

	#endregion

	#region Parse: permission

	[Theory]
	[InlineData("0", 0UL)]
	[InlineData("4", 4UL)]
	[InlineData("18446744073709551615", ulong.MaxValue)]
	public void Parse_PermissionDecimalMask_IsReadWholeIncludingTheBounds(string mask, ulong expected)
	{
		var query = UserQuery.Parse($"permission={mask}");

		Assert.Equal((UserPermissions)expected, query.Permissions);
		Assert.Null(query.Text);
	}

	[Fact]
	public void Parse_PermissionWithColonOperator_IsRead()
	{
		Assert.Equal((UserPermissions)8, UserQuery.Parse("permission:8").Permissions);
	}

	[Theory]
	[InlineData("permission=abc")]
	[InlineData("permission=-1")]
	[InlineData("permission=+1")]
	[InlineData("permission=1.5")]
	[InlineData("permission=0x10")]
	[InlineData("permission=18446744073709551616")]
	public void Parse_PermissionThatIsNotAnUnsignedDecimal_StaysInTheText(string text)
	{
		var query = UserQuery.Parse(text);

		Assert.Null(query.Permissions);
		Assert.Equal(text, query.Text);
	}

	[Fact]
	public void Parse_PermissionWithAComparisonOperator_StaysInTheText()
	{
		var query = UserQuery.Parse("permission>=4");

		Assert.Null(query.Permissions);
		Assert.Equal("permission>=4", query.Text);
	}

	#endregion

	#region Parse: deleted

	[Theory]
	[InlineData("deleted=1", true)]
	[InlineData("deleted=0", false)]
	[InlineData("deleted:1", true)]
	public void Parse_DeletedYesOrNo_IsRead(string text, bool expected)
	{
		var query = UserQuery.Parse(text);

		Assert.Equal(expected, query.Deleted);
		Assert.Null(query.Text);
	}

	[Fact]
	public void Parse_DeletedStar_MeansBothAndIsConsumed()
	{
		var query = UserQuery.Parse("deleted=*");

		Assert.Null(query.Deleted);
		Assert.Null(query.Text);
	}

	[Theory]
	[InlineData("deleted=2")]
	[InlineData("deleted=yes")]
	[InlineData("deleted=true")]
	[InlineData("deleted=")]
	public void Parse_DeletedOtherValue_StaysInTheText(string text)
	{
		var query = UserQuery.Parse(text);

		Assert.Null(query.Deleted);
		Assert.Equal(text, query.Text);
	}

	#endregion

	#region Parse: unknown filters

	[Fact]
	public void Parse_UnknownKey_StaysInTheTextBesideKnownFilters()
	{
		var query = UserQuery.Parse("foo=bar deleted=1 baz");

		Assert.Equal("foo=bar baz", query.Text);
		Assert.True(query.Deleted);
	}

	[Fact]
	public void Parse_TextContainingAColonButNoKey_StaysInTheText()
	{
		Assert.Equal("12:30", UserQuery.Parse("12:30").Text);
	}

	#endregion

	#region TryParse

	[Fact]
	public void TryParse_Null_ReturnsFalseWithNoQuery()
	{
		var parsed = UserQuery.TryParse(null, null, out var query);

		Assert.False(parsed);
		Assert.Null(query);
	}

	[Theory]
	[InlineData("")]
	[InlineData("bob country=us")]
	[InlineData("country=zz !!")]
	public void TryParse_AnyText_ReturnsTheSameQueryAsParse(string text)
	{
		var parsed = UserQuery.TryParse(text, null, out var query);

		var expected = UserQuery.Parse(text);

		Assert.True(parsed);
		Assert.Equal(expected.Text, query!.Text);
		Assert.Equal(expected.Countries, query.Countries);
		Assert.Equal(expected.Permissions, query.Permissions);
		Assert.Equal(expected.Deleted, query.Deleted);
	}

	#endregion

	#region LimitTo

	[Theory]
	[InlineData(null)]
	[InlineData(true)]
	[InlineData(false)]
	public void LimitTo_AskerMayViewDeleted_KeepsTheQueryAsAsked(bool? deleted)
	{
		var query = new UserQuery("bob", Deleted: deleted);

		Assert.Equal(deleted, query.LimitTo(UserPermissions.UserViewDeleted).Deleted);
	}

	[Fact]
	public void LimitTo_AskerHoldingUserManage_MayViewDeleted()
	{
		var query = new UserQuery(Deleted: true);

		Assert.True(query.LimitTo(UserPermissions.UserManage).Deleted);
	}

	[Theory]
	[InlineData(null)]
	[InlineData(true)]
	[InlineData(false)]
	public void LimitTo_AskerMayNotViewDeleted_ShowsExistingUsersOnly(bool? deleted)
	{
		var query = new UserQuery(Deleted: deleted);

		Assert.False(query.LimitTo(UserPermissionsGroups.Default).Deleted);
	}

	[Fact]
	public void LimitTo_AskerWithNoPermissions_ShowsExistingUsersOnly()
	{
		Assert.False(new UserQuery().LimitTo(UserPermissions.None).Deleted);
	}

	[Fact]
	public void LimitTo_AskerMayNotViewDeleted_KeepsEveryOtherFilter()
	{
		var query = new UserQuery("bob", [Country.Us], (UserPermissions)4, true);

		var limited = query.LimitTo(UserPermissions.None);

		Assert.Equal("bob", limited.Text);
		Assert.Equal(query.Countries, limited.Countries);
		Assert.Equal(query.Permissions, limited.Permissions);
	}

	[Fact]
	public void LimitTo_AskerMayNotViewDeleted_LeavesTheOriginalQueryUnchanged()
	{
		var query = new UserQuery(Deleted: true);

		query.LimitTo(UserPermissions.None);

		Assert.True(query.Deleted);
	}

	[Fact]
	public void LimitTo_AfterParsingDeletedStar_StillHidesDeletedFromAnAskerWhoMayNot()
	{
		var limited = UserQuery.Parse("deleted=*").LimitTo(UserPermissionsGroups.Default);

		Assert.False(limited.Deleted);
	}

	#endregion
}
