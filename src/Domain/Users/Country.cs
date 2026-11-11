using System.Globalization;

namespace Basil.Domain.Users;

/// <summary>
///     Represents a country by its two-letter ISO code.
/// </summary>
/// <remarks>
///     Member names are the two-letter lowercase country codes. The numeric values follow the order
///     the codes appear in this file and carry no meaning on their own; use
///     <see cref="CountryExtensions.ToAcronym" /> to get the two-letter code for the wire.
/// </remarks>
public enum Country : byte
{
	/// <summary>Oceania (generic region code).</summary>
	Oc = 1,

	/// <summary>Europe (generic region code).</summary>
	Eu = 2,

	/// <summary>Andorra.</summary>
	Ad = 3,

	/// <summary>United Arab Emirates.</summary>
	Ae = 4,

	/// <summary>Afghanistan.</summary>
	Af = 5,

	/// <summary>Antigua &amp; Barbuda.</summary>
	Ag = 6,

	/// <summary>Anguilla.</summary>
	Ai = 7,

	/// <summary>Albania.</summary>
	Al = 8,

	/// <summary>Armenia.</summary>
	Am = 9,

	/// <summary>Netherlands Antilles.</summary>
	An = 10,

	/// <summary>Angola.</summary>
	Ao = 11,

	/// <summary>Antarctica.</summary>
	Aq = 12,

	/// <summary>Argentina.</summary>
	Ar = 13,

	/// <summary>American Samoa.</summary>
	As = 14,

	/// <summary>Austria.</summary>
	At = 15,

	/// <summary>Australia.</summary>
	Au = 16,

	/// <summary>Aruba.</summary>
	Aw = 17,

	/// <summary>Azerbaijan.</summary>
	Az = 18,

	/// <summary>Bosnia &amp; Herzegovina.</summary>
	Ba = 19,

	/// <summary>Barbados.</summary>
	Bb = 20,

	/// <summary>Bangladesh.</summary>
	Bd = 21,

	/// <summary>Belgium.</summary>
	Be = 22,

	/// <summary>Burkina Faso.</summary>
	Bf = 23,

	/// <summary>Bulgaria.</summary>
	Bg = 24,

	/// <summary>Bahrain.</summary>
	Bh = 25,

	/// <summary>Burundi.</summary>
	Bi = 26,

	/// <summary>Benin.</summary>
	Bj = 27,

	/// <summary>Bermuda.</summary>
	Bm = 28,

	/// <summary>Brunei.</summary>
	Bn = 29,

	/// <summary>Bolivia.</summary>
	Bo = 30,

	/// <summary>Brazil.</summary>
	Br = 31,

	/// <summary>Bahamas.</summary>
	Bs = 32,

	/// <summary>Bhutan.</summary>
	Bt = 33,

	/// <summary>Bouvet Island.</summary>
	Bv = 34,

	/// <summary>Botswana.</summary>
	Bw = 35,

	/// <summary>Belarus.</summary>
	By = 36,

	/// <summary>Belize.</summary>
	Bz = 37,

	/// <summary>Canada.</summary>
	Ca = 38,

	/// <summary>Cocos (Keeling) Islands.</summary>
	Cc = 39,

	/// <summary>Congo - Kinshasa.</summary>
	Cd = 40,

	/// <summary>Central African Republic.</summary>
	Cf = 41,

	/// <summary>Congo - Brazzaville.</summary>
	Cg = 42,

	/// <summary>Switzerland.</summary>
	Ch = 43,

	/// <summary>Côte d’Ivoire.</summary>
	Ci = 44,

	/// <summary>Cook Islands.</summary>
	Ck = 45,

	/// <summary>Chile.</summary>
	Cl = 46,

	/// <summary>Cameroon.</summary>
	Cm = 47,

	/// <summary>China.</summary>
	Cn = 48,

	/// <summary>Colombia.</summary>
	Co = 49,

	/// <summary>Costa Rica.</summary>
	Cr = 50,

	/// <summary>Cuba.</summary>
	Cu = 51,

	/// <summary>Cape Verde.</summary>
	Cv = 52,

	/// <summary>Christmas Island.</summary>
	Cx = 53,

	/// <summary>Cyprus.</summary>
	Cy = 54,

	/// <summary>Czechia.</summary>
	Cz = 55,

	/// <summary>Germany.</summary>
	De = 56,

	/// <summary>Djibouti.</summary>
	Dj = 57,

	/// <summary>Denmark.</summary>
	Dk = 58,

	/// <summary>Dominica.</summary>
	Dm = 59,

	/// <summary>Dominican Republic.</summary>
	Do = 60,

	/// <summary>Algeria.</summary>
	Dz = 61,

	/// <summary>Ecuador.</summary>
	Ec = 62,

	/// <summary>Estonia.</summary>
	Ee = 63,

	/// <summary>Egypt.</summary>
	Eg = 64,

	/// <summary>Western Sahara.</summary>
	Eh = 65,

	/// <summary>Eritrea.</summary>
	Er = 66,

	/// <summary>Spain.</summary>
	Es = 67,

	/// <summary>Ethiopia.</summary>
	Et = 68,

	/// <summary>Finland.</summary>
	Fi = 69,

	/// <summary>Fiji.</summary>
	Fj = 70,

	/// <summary>Falkland Islands.</summary>
	Fk = 71,

	/// <summary>Micronesia.</summary>
	Fm = 72,

	/// <summary>Faroe Islands.</summary>
	Fo = 73,

	/// <summary>France.</summary>
	Fr = 74,

	/// <summary>France, Metropolitan.</summary>
	Fx = 75,

	/// <summary>Gabon.</summary>
	Ga = 76,

	/// <summary>United Kingdom.</summary>
	Gb = 77,

	/// <summary>Grenada.</summary>
	Gd = 78,

	/// <summary>Georgia.</summary>
	Ge = 79,

	/// <summary>French Guiana.</summary>
	Gf = 80,

	/// <summary>Ghana.</summary>
	Gh = 81,

	/// <summary>Gibraltar.</summary>
	Gi = 82,

	/// <summary>Greenland.</summary>
	Gl = 83,

	/// <summary>Gambia.</summary>
	Gm = 84,

	/// <summary>Guinea.</summary>
	Gn = 85,

	/// <summary>Guadeloupe.</summary>
	Gp = 86,

	/// <summary>Equatorial Guinea.</summary>
	Gq = 87,

	/// <summary>Greece.</summary>
	Gr = 88,

	/// <summary>South Georgia &amp; South Sandwich Islands.</summary>
	Gs = 89,

	/// <summary>Guatemala.</summary>
	Gt = 90,

	/// <summary>Guam.</summary>
	Gu = 91,

	/// <summary>Guinea-Bissau.</summary>
	Gw = 92,

	/// <summary>Guyana.</summary>
	Gy = 93,

	/// <summary>Hong Kong SAR China.</summary>
	Hk = 94,

	/// <summary>Heard &amp; McDonald Islands.</summary>
	Hm = 95,

	/// <summary>Honduras.</summary>
	Hn = 96,

	/// <summary>Croatia.</summary>
	Hr = 97,

	/// <summary>Haiti.</summary>
	Ht = 98,

	/// <summary>Hungary.</summary>
	Hu = 99,

	/// <summary>Indonesia.</summary>
	Id = 100,

	/// <summary>Ireland.</summary>
	Ie = 101,

	/// <summary>Israel.</summary>
	Il = 102,

	/// <summary>India.</summary>
	In = 103,

	/// <summary>British Indian Ocean Territory.</summary>
	Io = 104,

	/// <summary>Iraq.</summary>
	Iq = 105,

	/// <summary>Iran.</summary>
	Ir = 106,

	/// <summary>Iceland.</summary>
	Is = 107,

	/// <summary>Italy.</summary>
	It = 108,

	/// <summary>Jamaica.</summary>
	Jm = 109,

	/// <summary>Jordan.</summary>
	Jo = 110,

	/// <summary>Japan.</summary>
	Jp = 111,

	/// <summary>Kenya.</summary>
	Ke = 112,

	/// <summary>Kyrgyzstan.</summary>
	Kg = 113,

	/// <summary>Cambodia.</summary>
	Kh = 114,

	/// <summary>Kiribati.</summary>
	Ki = 115,

	/// <summary>Comoros.</summary>
	Km = 116,

	/// <summary>St. Kitts &amp; Nevis.</summary>
	Kn = 117,

	/// <summary>North Korea.</summary>
	Kp = 118,

	/// <summary>South Korea.</summary>
	Kr = 119,

	/// <summary>Kuwait.</summary>
	Kw = 120,

	/// <summary>Cayman Islands.</summary>
	Ky = 121,

	/// <summary>Kazakhstan.</summary>
	Kz = 122,

	/// <summary>Laos.</summary>
	La = 123,

	/// <summary>Lebanon.</summary>
	Lb = 124,

	/// <summary>St. Lucia.</summary>
	Lc = 125,

	/// <summary>Liechtenstein.</summary>
	Li = 126,

	/// <summary>Sri Lanka.</summary>
	Lk = 127,

	/// <summary>Liberia.</summary>
	Lr = 128,

	/// <summary>Lesotho.</summary>
	Ls = 129,

	/// <summary>Lithuania.</summary>
	Lt = 130,

	/// <summary>Luxembourg.</summary>
	Lu = 131,

	/// <summary>Latvia.</summary>
	Lv = 132,

	/// <summary>Libya.</summary>
	Ly = 133,

	/// <summary>Morocco.</summary>
	Ma = 134,

	/// <summary>Monaco.</summary>
	Mc = 135,

	/// <summary>Moldova.</summary>
	Md = 136,

	/// <summary>Madagascar.</summary>
	Mg = 137,

	/// <summary>Marshall Islands.</summary>
	Mh = 138,

	/// <summary>North Macedonia.</summary>
	Mk = 139,

	/// <summary>Mali.</summary>
	Ml = 140,

	/// <summary>Myanmar (Burma).</summary>
	Mm = 141,

	/// <summary>Mongolia.</summary>
	Mn = 142,

	/// <summary>Macao SAR China.</summary>
	Mo = 143,

	/// <summary>Northern Mariana Islands.</summary>
	Mp = 144,

	/// <summary>Martinique.</summary>
	Mq = 145,

	/// <summary>Mauritania.</summary>
	Mr = 146,

	/// <summary>Montserrat.</summary>
	Ms = 147,

	/// <summary>Malta.</summary>
	Mt = 148,

	/// <summary>Mauritius.</summary>
	Mu = 149,

	/// <summary>Maldives.</summary>
	Mv = 150,

	/// <summary>Malawi.</summary>
	Mw = 151,

	/// <summary>Mexico.</summary>
	Mx = 152,

	/// <summary>Malaysia.</summary>
	My = 153,

	/// <summary>Mozambique.</summary>
	Mz = 154,

	/// <summary>Namibia.</summary>
	Na = 155,

	/// <summary>New Caledonia.</summary>
	Nc = 156,

	/// <summary>Niger.</summary>
	Ne = 157,

	/// <summary>Norfolk Island.</summary>
	Nf = 158,

	/// <summary>Nigeria.</summary>
	Ng = 159,

	/// <summary>Nicaragua.</summary>
	Ni = 160,

	/// <summary>Netherlands.</summary>
	Nl = 161,

	/// <summary>Norway.</summary>
	No = 162,

	/// <summary>Nepal.</summary>
	Np = 163,

	/// <summary>Nauru.</summary>
	Nr = 164,

	/// <summary>Niue.</summary>
	Nu = 165,

	/// <summary>New Zealand.</summary>
	Nz = 166,

	/// <summary>Oman.</summary>
	Om = 167,

	/// <summary>Panama.</summary>
	Pa = 168,

	/// <summary>Peru.</summary>
	Pe = 169,

	/// <summary>French Polynesia.</summary>
	Pf = 170,

	/// <summary>Papua New Guinea.</summary>
	Pg = 171,

	/// <summary>Philippines.</summary>
	Ph = 172,

	/// <summary>Pakistan.</summary>
	Pk = 173,

	/// <summary>Poland.</summary>
	Pl = 174,

	/// <summary>St. Pierre &amp; Miquelon.</summary>
	Pm = 175,

	/// <summary>Pitcairn Islands.</summary>
	Pn = 176,

	/// <summary>Puerto Rico.</summary>
	Pr = 177,

	/// <summary>Palestinian Territories.</summary>
	Ps = 178,

	/// <summary>Portugal.</summary>
	Pt = 179,

	/// <summary>Palau.</summary>
	Pw = 180,

	/// <summary>Paraguay.</summary>
	Py = 181,

	/// <summary>Qatar.</summary>
	Qa = 182,

	/// <summary>Réunion.</summary>
	Re = 183,

	/// <summary>Romania.</summary>
	Ro = 184,

	/// <summary>Russia.</summary>
	Ru = 185,

	/// <summary>Rwanda.</summary>
	Rw = 186,

	/// <summary>Saudi Arabia.</summary>
	Sa = 187,

	/// <summary>Solomon Islands.</summary>
	Sb = 188,

	/// <summary>Seychelles.</summary>
	Sc = 189,

	/// <summary>Sudan.</summary>
	Sd = 190,

	/// <summary>Sweden.</summary>
	Se = 191,

	/// <summary>Singapore.</summary>
	Sg = 192,

	/// <summary>St. Helena.</summary>
	Sh = 193,

	/// <summary>Slovenia.</summary>
	Si = 194,

	/// <summary>Svalbard &amp; Jan Mayen.</summary>
	Sj = 195,

	/// <summary>Slovakia.</summary>
	Sk = 196,

	/// <summary>Sierra Leone.</summary>
	Sl = 197,

	/// <summary>San Marino.</summary>
	Sm = 198,

	/// <summary>Senegal.</summary>
	Sn = 199,

	/// <summary>Somalia.</summary>
	So = 200,

	/// <summary>Suriname.</summary>
	Sr = 201,

	/// <summary>São Tomé &amp; Príncipe.</summary>
	St = 202,

	/// <summary>El Salvador.</summary>
	Sv = 203,

	/// <summary>Syria.</summary>
	Sy = 204,

	/// <summary>Eswatini.</summary>
	Sz = 205,

	/// <summary>Turks &amp; Caicos Islands.</summary>
	Tc = 206,

	/// <summary>Chad.</summary>
	Td = 207,

	/// <summary>French Southern Territories.</summary>
	Tf = 208,

	/// <summary>Togo.</summary>
	Tg = 209,

	/// <summary>Thailand.</summary>
	Th = 210,

	/// <summary>Tajikistan.</summary>
	Tj = 211,

	/// <summary>Tokelau.</summary>
	Tk = 212,

	/// <summary>Turkmenistan.</summary>
	Tm = 213,

	/// <summary>Tunisia.</summary>
	Tn = 214,

	/// <summary>Tonga.</summary>
	To = 215,

	/// <summary>Timor-Leste.</summary>
	Tl = 216,

	/// <summary>Türkiye.</summary>
	Tr = 217,

	/// <summary>Trinidad &amp; Tobago.</summary>
	Tt = 218,

	/// <summary>Tuvalu.</summary>
	Tv = 219,

	/// <summary>Taiwan.</summary>
	Tw = 220,

	/// <summary>Tanzania.</summary>
	Tz = 221,

	/// <summary>Ukraine.</summary>
	Ua = 222,

	/// <summary>Uganda.</summary>
	Ug = 223,

	/// <summary>U.S. Outlying Islands.</summary>
	Um = 224,

	/// <summary>United States.</summary>
	Us = 225,

	/// <summary>Uruguay.</summary>
	Uy = 226,

	/// <summary>Uzbekistan.</summary>
	Uz = 227,

	/// <summary>Vatican City.</summary>
	Va = 228,

	/// <summary>St. Vincent &amp; Grenadines.</summary>
	Vc = 229,

	/// <summary>Venezuela.</summary>
	Ve = 230,

	/// <summary>British Virgin Islands.</summary>
	Vg = 231,

	/// <summary>U.S. Virgin Islands.</summary>
	Vi = 232,

	/// <summary>Vietnam.</summary>
	Vn = 233,

	/// <summary>Vanuatu.</summary>
	Vu = 234,

	/// <summary>Wallis &amp; Futuna.</summary>
	Wf = 235,

	/// <summary>Samoa.</summary>
	Ws = 236,

	/// <summary>Yemen.</summary>
	Ye = 237,

	/// <summary>Mayotte.</summary>
	Yt = 238,

	/// <summary>Serbia.</summary>
	Rs = 239,

	/// <summary>South Africa.</summary>
	Za = 240,

	/// <summary>Zambia.</summary>
	Zm = 241,

	/// <summary>Montenegro.</summary>
	Me = 242,

	/// <summary>Zimbabwe.</summary>
	Zw = 243,

	/// <summary>Unknown country.</summary>
	Xx = 244,

	/// <summary>Satellite provider.</summary>
	A2 = 245,

	/// <summary>Other country.</summary>
	O1 = 246,

	/// <summary>Åland Islands.</summary>
	Ax = 247,

	/// <summary>Guernsey.</summary>
	Gg = 248,

	/// <summary>Isle of Man.</summary>
	Im = 249,

	/// <summary>Jersey.</summary>
	Je = 250,

	/// <summary>St. Barthélemy.</summary>
	Bl = 251,

	/// <summary>St. Martin.</summary>
	Mf = 252
}

/// <summary>
///     Provides conversions for <see cref="Country" /> values.
/// </summary>
public static class CountryExtensions
{
	/// <param name="code">The country code to convert.</param>
	extension(Country code)
	{
		/// <summary>
		///     Converts a country code to its lowercase two-letter acronym.
		/// </summary>
		/// <returns>The two-letter acronym of the code, in lowercase.</returns>
		public string ToAcronym()
		{
			return code.ToString().ToLowerInvariant();
		}

		/// <summary>Resolves country to its English region name.</summary>
		/// <returns>
		///     The English region name, or the bare code when cannot resolve.
		/// </returns>
		public string Describe()
		{
			return code switch
			{
				Country.Xx => "Unknown",
				Country.A2 => "Satellite Provider",
				Country.O1 => "Other Country",
				Country.Eu => "Europe",
				Country.Oc => "Oceania",
				Country.An => "Netherlands Antilles",
				Country.Fx => "Metropolitan France",
				_ => GetRegionName(code)
			};

			static string GetRegionName(Country code)
			{
				try
				{
					return new RegionInfo(code.ToString().ToUpperInvariant()).EnglishName;
				}
				catch (ArgumentException)
				{
					return code.ToString().ToUpperInvariant();
				}
			}
		}
	}
}