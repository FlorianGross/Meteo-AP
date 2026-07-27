using ElwMeteo.Core.Time;
using Xunit;

namespace ElwMeteo.Core.Tests;

public class SolarCalculatorTests
{
    // Frankfurt am Main.
    private const double FrankfurtLat = 50.1109;
    private const double FrankfurtLon = 8.6821;

    [Fact]
    public void CalculateDay_MatchesPublishedSunriseForFrankfurtMidsummer()
    {
        // 21 June 2026: sunrise 05:16, sunset 21:38 CEST (published DWD/almanac values).
        var date = new DateTimeOffset(2026, 6, 21, 12, 0, 0, TimeSpan.FromHours(2));
        var day = SolarCalculator.CalculateDay(FrankfurtLat, FrankfurtLon, date);

        Assert.NotNull(day.Sunrise);
        Assert.NotNull(day.Sunset);
        AssertClose(new TimeSpan(5, 16, 0), day.Sunrise!.Value.TimeOfDay, toleranceMinutes: 2);
        AssertClose(new TimeSpan(21, 38, 0), day.Sunset!.Value.TimeOfDay, toleranceMinutes: 2);
    }

    [Fact]
    public void CalculateDay_MatchesPublishedSunriseForFrankfurtMidwinter()
    {
        // 21 December 2026: sunrise 08:22, sunset 16:24 CET.
        var date = new DateTimeOffset(2026, 12, 21, 12, 0, 0, TimeSpan.FromHours(1));
        var day = SolarCalculator.CalculateDay(FrankfurtLat, FrankfurtLon, date);

        AssertClose(new TimeSpan(8, 22, 0), day.Sunrise!.Value.TimeOfDay, toleranceMinutes: 3);
        AssertClose(new TimeSpan(16, 24, 0), day.Sunset!.Value.TimeOfDay, toleranceMinutes: 3);
    }

    [Fact]
    public void CalculateDay_ReturnsResultsInTheRequestedOffset()
    {
        var date = new DateTimeOffset(2026, 6, 21, 12, 0, 0, TimeSpan.FromHours(2));
        var day = SolarCalculator.CalculateDay(FrankfurtLat, FrankfurtLon, date);

        Assert.Equal(TimeSpan.FromHours(2), day.Sunrise!.Value.Offset);
        Assert.Equal(TimeSpan.FromHours(2), day.SolarNoon.Offset);
    }

    [Fact]
    public void CalculateDay_OrdersTwilightPhasesOutwardFromSunrise()
    {
        var date = new DateTimeOffset(2026, 4, 15, 12, 0, 0, TimeSpan.FromHours(2));
        var day = SolarCalculator.CalculateDay(FrankfurtLat, FrankfurtLon, date);

        Assert.True(day.AstronomicalDawn < day.NauticalDawn);
        Assert.True(day.NauticalDawn < day.CivilDawn);
        Assert.True(day.CivilDawn < day.Sunrise);
        Assert.True(day.Sunrise < day.SolarNoon);
        Assert.True(day.SolarNoon < day.Sunset);
        Assert.True(day.Sunset < day.CivilDusk);
        Assert.True(day.CivilDusk < day.NauticalDusk);
        Assert.True(day.NauticalDusk < day.AstronomicalDusk);
    }

    [Fact]
    public void CalculateDay_DetectsMidnightSunAboveTheArcticCircle()
    {
        // Tromsø in late June: the sun does not set.
        var date = new DateTimeOffset(2026, 6, 21, 12, 0, 0, TimeSpan.FromHours(2));
        var day = SolarCalculator.CalculateDay(69.65, 18.96, date);

        Assert.Null(day.Sunrise);
        Assert.Null(day.Sunset);
        Assert.True(day.MidnightSun);
        Assert.False(day.SunNeverRises);
    }

    [Fact]
    public void CalculateDay_DetectsPolarNightAboveTheArcticCircle()
    {
        var date = new DateTimeOffset(2026, 12, 21, 12, 0, 0, TimeSpan.FromHours(1));
        var day = SolarCalculator.CalculateDay(69.65, 18.96, date);

        Assert.Null(day.Sunrise);
        Assert.True(day.SunNeverRises);
        Assert.False(day.MidnightSun);
    }

    [Fact]
    public void CalculatePosition_PutsSunNearSouthAtLocalSolarNoon()
    {
        var date = new DateTimeOffset(2026, 6, 21, 12, 0, 0, TimeSpan.FromHours(2));
        var day = SolarCalculator.CalculateDay(FrankfurtLat, FrankfurtLon, date);
        var position = SolarCalculator.CalculatePosition(FrankfurtLat, FrankfurtLon, day.SolarNoon);

        // Northern hemisphere: the sun culminates in the south (azimuth ~180°).
        Assert.Equal(180.0, position.AzimuthDeg, 1.0);

        // Maximum elevation = 90 - latitude + declination ≈ 90 - 50.1 + 23.4.
        Assert.Equal(63.3, position.ElevationDeg, 0.5);
        Assert.True(position.IsDaylight);
    }

    [Fact]
    public void CalculatePosition_ReportsNightAtLocalMidnight()
    {
        var midnight = new DateTimeOffset(2026, 6, 21, 1, 0, 0, TimeSpan.FromHours(2));
        var position = SolarCalculator.CalculatePosition(FrankfurtLat, FrankfurtLon, midnight);

        Assert.True(position.ElevationDeg < 0);
        Assert.False(position.IsDaylight);
    }

    [Fact]
    public void CalculatePosition_HasEastwardAzimuthInTheMorning()
    {
        var morning = new DateTimeOffset(2026, 6, 21, 7, 0, 0, TimeSpan.FromHours(2));
        var position = SolarCalculator.CalculatePosition(FrankfurtLat, FrankfurtLon, morning);

        Assert.InRange(position.AzimuthDeg, 60.0, 110.0);
        Assert.True(position.ElevationDeg > 0);
    }

    [Fact]
    public void JulianDay_MatchesTheJ2000Epoch()
    {
        // 1 January 2000, 12:00 UTC is JD 2451545.0 by definition.
        Assert.Equal(2451545.0, SolarCalculator.JulianDay(new DateTime(2000, 1, 1, 12, 0, 0, DateTimeKind.Utc)), 1e-6);
    }

    [Fact]
    public void DayLength_IsLongerInSummerThanInWinter()
    {
        var summer = SolarCalculator.CalculateDay(FrankfurtLat, FrankfurtLon,
            new DateTimeOffset(2026, 6, 21, 12, 0, 0, TimeSpan.FromHours(2)));
        var winter = SolarCalculator.CalculateDay(FrankfurtLat, FrankfurtLon,
            new DateTimeOffset(2026, 12, 21, 12, 0, 0, TimeSpan.FromHours(1)));

        Assert.True(summer.DayLength > TimeSpan.FromHours(16));
        Assert.True(winter.DayLength < TimeSpan.FromHours(9));
    }

    /// <summary>Compares two clock times, allowing for the algorithm's sub-minute error.</summary>
    private static void AssertClose(TimeSpan expected, TimeSpan actual, double toleranceMinutes)
    {
        double deltaMinutes = Math.Abs((actual - expected).TotalMinutes);
        Assert.True(deltaMinutes <= toleranceMinutes,
            $"Erwartet {expected:hh\\:mm}, erhalten {actual:hh\\:mm} (Abweichung {deltaMinutes:F1} min).");
    }
}
