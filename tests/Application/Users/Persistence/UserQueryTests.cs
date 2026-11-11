using System.Globalization;
using Basil.Application.Users.Persistence;
using Basil.Domain.Users;

namespace Basil.Application.Users.Tests.Persistence;

/// <summary>
///     Behaviour of the user search syntax and of narrowing a query to what an asker may see, from the XML contract of
///     <see cref="UserQuery" />: free text matches names, <c>country=</c> takes two-letter codes, <c>privilege=</c> a
///     decimal mask, <c>deleted=</c> <c>1</c>, <c>0</c> or <c>*</c>; a filter that is unknown or does not parse stays in
///     the text.
/// </summary>
public sealed class UserQueryTests
{
	#region Parse: text

	[Fact]
	public void ParseNull_Throws()
	{
		Assert.Throws<ArgumentNullException>(() => UserQuery.Parse(null!));
	}

	[Theory]
	[InlineData("")]
	[InlineData("   ")]
	public void ParseBlank_Unfiltered(string text)
	{
		var query = UserQuery.Parse(text);

		Assert.Null(query.Text);
		Assert.Null(query.Countries);
		Assert.Null(query.Privileges);
		Assert.Null(query.Deleted);
	}

	[Fact]
	public void ParsePlain_KeptAsText()
	{
		var query = UserQuery.Parse("bob");

		Assert.Equal("bob", query.Text);
		Assert.Null(query.Countries);
	}

	[Fact]
	public void RepeatedSpaces_Collapsed()
	{
		Assert.Equal("bob smith", UserQuery.Parse("  bob    smith  ").Text);
	}

	[Fact]
	public void QuotedNoKey_StaysText()
	{
		Assert.Equal("\"bob smith\"", UserQuery.Parse("\"bob smith\"").Text);
	}

	[Fact]
	public void FilterBetweenWords_Removed()
	{
		var query = UserQuery.Parse("alice country=us bob");

		Assert.Equal("alice bob", query.Text);
		Assert.Equal([Country.Us], query.Countries);
	}

	[Fact]
	public void DocumentedExample_ReadsAll()
	{
		var query = UserQuery.Parse("country=usvn privilege=4 deleted=* bob");

		Assert.Equal("bob", query.Text);
		Assert.Equal([Country.Us, Country.Vn], query.Countries);
		Assert.Equal((UserPrivileges)4, query.Privileges);
		Assert.Null(query.Deleted);
	}

	[Fact]
	public void OnlyFilters_NoText()
	{
		Assert.Null(UserQuery.Parse("country=us privilege=1 deleted=0").Text);
	}

	[Fact]
	public void ParseProvider_Ignored()
	{
		var turkish = CultureInfo.GetCultureInfo("tr-TR");

		Assert.Equal([Country.Id], UserQuery.Parse("country=ID", turkish).Countries);
	}

	#endregion

	#region Parse: country

	[Theory]
	[InlineData("country=us")]
	[InlineData("country:us")]
	[InlineData("COUNTRY=US")]
	[InlineData("Country=Us")]
	public void CountryVariants_AllRead(string text)
	{
		var query = UserQuery.Parse(text);

		Assert.Equal([Country.Us], query.Countries);
		Assert.Null(query.Text);
	}

	[Fact]
	public void SeveralCodes_ReadInOrder()
	{
		Assert.Equal([Country.Vn, Country.Us, Country.Gb], UserQuery.Parse("country=vnusgb").Countries);
	}

	[Theory]
	[InlineData("country=u")]
	[InlineData("country=usv")]
	[InlineData("country=zz")]
	[InlineData("country=usqq")]
	[InlineData("country=u-")]
	public void CountryNotCodes_StaysText(string text)
	{
		var query = UserQuery.Parse(text);

		Assert.Null(query.Countries);
		Assert.Equal(text, query.Text);
	}

	[Theory]
	[InlineData("country=00")]
	[InlineData("country=12")]
	[InlineData("country=99")]
	public void CountryDigits_StaysText(string text)
	{
		var query = UserQuery.Parse(text);

		Assert.Null(query.Countries);
		Assert.Equal(text, query.Text);
	}

	[Fact]
	public void CountryNoValue_StaysText()
	{
		var query = UserQuery.Parse("country=");

		Assert.Null(query.Countries);
		Assert.Equal("country=", query.Text);
	}

	[Fact]
	public void CountryQuoted_Unquoted()
	{
		Assert.Equal([Country.Us], UserQuery.Parse("country=\"us\"").Countries);
	}

	[Theory]
	[InlineData("country<us")]
	[InlineData("country>=us")]
	public void CountryComparison_StaysText(string text)
	{
		var query = UserQuery.Parse(text);

		Assert.Null(query.Countries);
		Assert.Equal(text, query.Text);
	}

	#endregion

	#region Parse: privilege

	[Theory]
	[InlineData("0", 0UL)]
	[InlineData("4", 4UL)]
	[InlineData("18446744073709551615", ulong.MaxValue)]
	public void PermissionDecimal_ReadWhole(string mask, ulong expected)
	{
		var query = UserQuery.Parse($"privilege={mask}");

		Assert.Equal((UserPrivileges)expected, query.Privileges);
		Assert.Null(query.Text);
	}

	[Fact]
	public void PermissionColon_Read()
	{
		Assert.Equal((UserPrivileges)8, UserQuery.Parse("privilege:8").Privileges);
	}

	[Theory]
	[InlineData("privilege=abc")]
	[InlineData("privilege=-1")]
	[InlineData("privilege=+1")]
	[InlineData("privilege=1.5")]
	[InlineData("privilege=0x10")]
	[InlineData("privilege=18446744073709551616")]
	public void PermissionNotDecimal_StaysText(string text)
	{
		var query = UserQuery.Parse(text);

		Assert.Null(query.Privileges);
		Assert.Equal(text, query.Text);
	}

	[Fact]
	public void PermissionComparison_StaysText()
	{
		var query = UserQuery.Parse("privilege>=4");

		Assert.Null(query.Privileges);
		Assert.Equal("privilege>=4", query.Text);
	}

	#endregion

	#region Parse: deleted

	[Theory]
	[InlineData("deleted=1", true)]
	[InlineData("deleted=0", false)]
	[InlineData("deleted:1", true)]
	public void DeletedYesNo_Read(string text, bool expected)
	{
		var query = UserQuery.Parse(text);

		Assert.Equal(expected, query.Deleted);
		Assert.Null(query.Text);
	}

	[Fact]
	public void DeletedStar_BothConsumed()
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
	public void DeletedOther_StaysText(string text)
	{
		var query = UserQuery.Parse(text);

		Assert.Null(query.Deleted);
		Assert.Equal(text, query.Text);
	}

	#endregion

	#region Parse: unknown filters

	[Fact]
	public void UnknownKey_StaysText()
	{
		var query = UserQuery.Parse("foo=bar deleted=1 baz");

		Assert.Equal("foo=bar baz", query.Text);
		Assert.True(query.Deleted);
	}

	[Fact]
	public void ColonNoKey_StaysText()
	{
		Assert.Equal("12:30", UserQuery.Parse("12:30").Text);
	}

	#endregion

	#region TryParse

	[Fact]
	public void TryParseNull_FalseNoQuery()
	{
		var parsed = UserQuery.TryParse(null, null, out var query);

		Assert.False(parsed);
		Assert.Null(query);
	}

	[Theory]
	[InlineData("")]
	[InlineData("bob country=us")]
	[InlineData("country=zz !!")]
	public void TryParseText_SameAsParse(string text)
	{
		var parsed = UserQuery.TryParse(text, null, out var query);

		var expected = UserQuery.Parse(text);

		Assert.True(parsed);
		Assert.Equal(expected.Text, query!.Text);
		Assert.Equal(expected.Countries, query.Countries);
		Assert.Equal(expected.Privileges, query.Privileges);
		Assert.Equal(expected.Deleted, query.Deleted);
	}

	#endregion

	#region LimitTo

	[Theory]
	[InlineData(null)]
	[InlineData(true)]
	[InlineData(false)]
	public void AskerMayView_KeepsQuery(bool? deleted)
	{
		var query = new UserQuery("bob", Deleted: deleted);

		Assert.Equal(deleted, query.LimitTo(UserPrivileges.Owner).Deleted);
	}

	[Fact]
	public void UserManage_ViewsDeleted()
	{
		var query = new UserQuery(Deleted: true);

		Assert.True(query.LimitTo(UserPrivileges.Owner).Deleted);
	}

	[Theory]
	[InlineData(null)]
	[InlineData(true)]
	[InlineData(false)]
	public void NoView_ExistingOnly(bool? deleted)
	{
		var query = new UserQuery(Deleted: deleted);

		Assert.False(query.LimitTo(UserPrivilegesExtensions.Default).Deleted);
	}

	[Fact]
	public void NoPrivileges_ExistingOnly()
	{
		Assert.False(new UserQuery().LimitTo(UserPrivileges.None).Deleted);
	}

	[Fact]
	public void NoView_KeepsFilters()
	{
		var query = new UserQuery("bob", [Country.Us], (UserPrivileges)4, true);

		var limited = query.LimitTo(UserPrivileges.None);

		Assert.Equal("bob", limited.Text);
		Assert.Equal(query.Countries, limited.Countries);
		Assert.Equal(query.Privileges, limited.Privileges);
	}

	[Fact]
	public void NoView_OriginalUnchanged()
	{
		var query = new UserQuery(Deleted: true);

		query.LimitTo(UserPrivileges.None);

		Assert.True(query.Deleted);
	}

	[Fact]
	public void DeletedStar_StillHidden()
	{
		var limited = UserQuery.Parse("deleted=*").LimitTo(UserPrivilegesExtensions.Default);

		Assert.False(limited.Deleted);
	}

	#endregion
}