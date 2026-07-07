namespace Bancho.Domain.Login;

/// <summary>Ported from app/state/services.py's Geolocation (TypedDict).</summary>
public sealed record Geolocation(double Latitude, double Longitude, string CountryAcronym, int CountryNumeric);
