using ElwMeteo.Core.Meteorology;
using Xunit;

namespace ElwMeteo.Core.Tests;

public class WindScaleTests
{
    [Theory]
    [InlineData(0.0, 0)]
    [InlineData(0.2, 0)]
    [InlineData(0.3, 1)]
    [InlineData(1.5, 1)]
    [InlineData(1.6, 2)]
    [InlineData(5.4, 3)]
    [InlineData(5.5, 4)]
    [InlineData(10.8, 6)]
    [InlineData(17.2, 8)]
    [InlineData(32.7, 12)]
    [InlineData(50.0, 12)]
    public void Beaufort_MatchesScaleBoundaries(double windMs, int expectedForce)
    {
        Assert.Equal(expectedForce, WindScale.Beaufort(windMs).Force);
    }

    [Fact]
    public void Beaufort_NamesGaleForceInGerman()
    {
        Assert.Equal("Sturm", WindScale.Beaufort(22.0).Description);
    }

    [Theory]
    [InlineData(0.0, "N")]
    [InlineData(11.0, "N")]
    [InlineData(45.0, "NO")]
    [InlineData(90.0, "O")]
    [InlineData(180.0, "S")]
    [InlineData(270.0, "W")]
    [InlineData(337.5, "NNW")]
    [InlineData(359.0, "N")]
    public void CompassPoint_UsesGermanSixteenPointNaming(double degrees, string expected)
    {
        Assert.Equal(expected, WindScale.CompassPoint(degrees));
    }

    [Theory]
    [InlineData(0.0, 180.0)]
    [InlineData(270.0, 90.0)]
    [InlineData(350.0, 170.0)]
    [InlineData(90.0, 270.0)]
    public void DownwindDirection_IsTheReciprocal(double windFrom, double expected)
    {
        Assert.Equal(expected, WindScale.DownwindDirection(windFrom), 1e-9);
    }

    [Fact]
    public void Normalize_WrapsNegativeAngles()
    {
        Assert.Equal(350.0, WindScale.Normalize(-10.0), 1e-9);
        Assert.Equal(10.0, WindScale.Normalize(370.0), 1e-9);
    }
}

public class ThermodynamicsTests
{
    [Fact]
    public void DewPoint_EqualsTemperatureAtSaturation()
    {
        Assert.Equal(20.0, Thermodynamics.DewPointC(20.0, 100.0), 0.05);
    }

    [Fact]
    public void DewPoint_MatchesReferenceValue()
    {
        // 20 °C at 50 % RH gives a dew point of about 9.3 °C.
        Assert.Equal(9.3, Thermodynamics.DewPointC(20.0, 50.0), 0.2);
    }

    [Fact]
    public void DewPoint_IsAlwaysAtOrBelowTemperature()
    {
        for (double t = -10; t <= 40; t += 5)
        {
            for (double rh = 10; rh <= 100; rh += 10)
            {
                Assert.True(Thermodynamics.DewPointC(t, rh) <= t + 1e-6);
            }
        }
    }

    [Fact]
    public void SaturationVapourPressure_MatchesReferenceAtZeroAndTwenty()
    {
        Assert.Equal(6.112, Thermodynamics.SaturationVapourPressureHpa(0.0), 0.01);
        Assert.Equal(23.4, Thermodynamics.SaturationVapourPressureHpa(20.0), 0.2);
    }

    [Fact]
    public void WindChill_IsUndefinedInMildWeather()
    {
        Assert.Null(Thermodynamics.WindChillC(15.0, 10.0));
    }

    [Fact]
    public void WindChill_IsUndefinedInCalmAir()
    {
        Assert.Null(Thermodynamics.WindChillC(-5.0, 0.5));
    }

    [Fact]
    public void WindChill_MatchesEnvironmentCanadaExample()
    {
        // -20 °C with 30 km/h wind feels like about -33 °C.
        double? chill = Thermodynamics.WindChillC(-20.0, WindScale.KmhToMs(30.0));
        Assert.NotNull(chill);
        Assert.Equal(-33.0, chill!.Value, 1.0);
    }

    [Fact]
    public void HeatIndex_IsUndefinedBelowTheValidRange()
    {
        Assert.Null(Thermodynamics.HeatIndexC(20.0, 80.0));
    }

    [Fact]
    public void HeatIndex_ExceedsAirTemperatureInHumidHeat()
    {
        double? heatIndex = Thermodynamics.HeatIndexC(32.0, 70.0);
        Assert.NotNull(heatIndex);
        Assert.True(heatIndex!.Value > 32.0);
        // NWS table gives roughly 41 °C for these inputs.
        Assert.Equal(41.0, heatIndex.Value, 2.0);
    }

    [Fact]
    public void WetBulb_LiesBetweenDewPointAndTemperature()
    {
        const double t = 25.0;
        const double rh = 55.0;

        double wetBulb = Thermodynamics.WetBulbC(t, rh);
        double dewPoint = Thermodynamics.DewPointC(t, rh);

        Assert.True(wetBulb < t);
        Assert.True(wetBulb > dewPoint);
    }

    [Fact]
    public void WbgtShade_RisesWithHumidityAtConstantTemperature()
    {
        double dry = Thermodynamics.WbgtShadeC(30.0, 30.0);
        double humid = Thermodynamics.WbgtShadeC(30.0, 80.0);

        Assert.True(humid > dry);
    }

    [Fact]
    public void AbsoluteHumidity_MatchesReferenceValue()
    {
        // 20 °C at 50 % RH holds about 8.6 g of water per m³.
        Assert.Equal(8.6, Thermodynamics.AbsoluteHumidityGm3(20.0, 50.0), 0.3);
    }

    [Fact]
    public void ConvectiveCloudBase_UsesTheSpreadRuleOfThumb()
    {
        // A 10 K spread puts the lifting condensation level near 1250 m.
        Assert.Equal(1250.0, Thermodynamics.ConvectiveCloudBaseM(20.0, 10.0), 1.0);
    }

    [Fact]
    public void ConvectiveCloudBase_IsNeverNegative()
    {
        Assert.Equal(0.0, Thermodynamics.ConvectiveCloudBaseM(5.0, 8.0));
    }
}

public class StabilityClassifierTests
{
    [Fact]
    public void Classify_ReturnsVeryUnstableOnCalmSunnyMidday()
    {
        var result = StabilityClassifier.Classify(windSpeedMs: 1.0, cloudCoverPercent: 0, solarElevationDeg: 65);

        Assert.Equal(PasquillClass.A, result.Pasquill);
        Assert.Equal("V", result.KlugManier);
    }

    [Fact]
    public void Classify_ReturnsStableOnClearCalmNight()
    {
        var result = StabilityClassifier.Classify(windSpeedMs: 1.0, cloudCoverPercent: 0, solarElevationDeg: -20);

        Assert.Equal(PasquillClass.F, result.Pasquill);
        Assert.Equal("I", result.KlugManier);
        Assert.Contains("bodennah", result.TacticalNote);
    }

    [Fact]
    public void Classify_ReturnsNeutralWhenOvercast()
    {
        // Overcast forces neutral stability regardless of sun or wind.
        var day = StabilityClassifier.Classify(2.0, 100, 60);
        var night = StabilityClassifier.Classify(2.0, 100, -30);

        Assert.Equal(PasquillClass.D, day.Pasquill);
        Assert.Equal(PasquillClass.D, night.Pasquill);
        Assert.Equal("III/1", day.KlugManier);
    }

    [Fact]
    public void Classify_ReturnsNeutralInStrongWindDayOrNight()
    {
        Assert.Equal(PasquillClass.D, StabilityClassifier.Classify(9.0, 20, 40).Pasquill);
        Assert.Equal(PasquillClass.D, StabilityClassifier.Classify(9.0, 20, -10).Pasquill);
    }

    [Fact]
    public void Classify_BecomesLessUnstableAsWindIncreasesUnderStrongSun()
    {
        var calm = StabilityClassifier.Classify(1.0, 0, 70);
        var breezy = StabilityClassifier.Classify(4.0, 0, 70);
        var windy = StabilityClassifier.Classify(8.0, 0, 70);

        Assert.True(calm.Pasquill < breezy.Pasquill);
        Assert.True(breezy.Pasquill < windy.Pasquill);
    }

    [Fact]
    public void Classify_TreatsCloudyNightAsLessStableThanClearNight()
    {
        var clear = StabilityClassifier.Classify(2.5, 10, -20);
        var cloudy = StabilityClassifier.Classify(2.5, 70, -20);

        Assert.Equal(PasquillClass.F, clear.Pasquill);
        Assert.Equal(PasquillClass.E, cloudy.Pasquill);
    }

    [Fact]
    public void Classify_ToleratesMissingValues()
    {
        var result = StabilityClassifier.Classify(double.NaN, double.NaN, 30);
        Assert.NotNull(result.Label);
    }
}

public class FireRiskTests
{
    [Fact]
    public void Assess_ReportsVeryHighRiskWhenHotAndDry()
    {
        var result = FireRisk.Assess(temperatureC: 35.0, relativeHumidityPercent: 20.0);

        // 20/20 + (27-35)/10 = 1.0 - 0.8 = 0.2
        Assert.Equal(0.2, result.AngstromIndex, 1e-9);
        Assert.Equal(FireRiskLevel.VeryHigh, result.Level);
    }

    [Fact]
    public void Assess_ReportsVeryLowRiskWhenCoolAndDamp()
    {
        var result = FireRisk.Assess(temperatureC: 8.0, relativeHumidityPercent: 90.0);

        Assert.True(result.AngstromIndex >= 4.0);
        Assert.Equal(FireRiskLevel.VeryLow, result.Level);
    }

    [Fact]
    public void Assess_IndexFallsAsConditionsWorsen()
    {
        double mild = FireRisk.Assess(18.0, 70.0).AngstromIndex;
        double hot = FireRisk.Assess(33.0, 30.0).AngstromIndex;

        Assert.True(hot < mild);
    }
}

public class GeodesyTests
{
    private static readonly LatLon Frankfurt = new(50.1109, 8.6821);

    [Fact]
    public void Destination_MovingNorthIncreasesLatitudeOnly()
    {
        var target = Geodesy.Destination(Frankfurt, bearingDeg: 0, distanceM: 1000);

        Assert.True(target.Latitude > Frankfurt.Latitude);
        Assert.Equal(Frankfurt.Longitude, target.Longitude, 1e-6);
        // One kilometre north is about 0.009 degrees of latitude.
        Assert.Equal(0.00899, target.Latitude - Frankfurt.Latitude, 1e-4);
    }

    [Fact]
    public void Destination_AndDistanceAreInverses()
    {
        var target = Geodesy.Destination(Frankfurt, bearingDeg: 123.4, distanceM: 2500);

        Assert.Equal(2500.0, Geodesy.DistanceMetres(Frankfurt, target), 0.5);
        Assert.Equal(123.4, Geodesy.BearingDeg(Frankfurt, target), 0.05);
    }

    [Fact]
    public void DistanceMetres_MatchesKnownCityPairSeparation()
    {
        var berlin = new LatLon(52.5200, 13.4050);
        double km = Geodesy.DistanceMetres(Frankfurt, berlin) / 1000.0;

        // Great-circle Frankfurt-Berlin is about 424 km.
        Assert.Equal(424.0, km, 3.0);
    }

    [Fact]
    public void DistanceMetres_IsZeroForIdenticalPoints()
    {
        Assert.Equal(0.0, Geodesy.DistanceMetres(Frankfurt, Frankfurt), 1e-6);
    }

    [Fact]
    public void FormatDegreesDecimalMinutes_UsesChartNotation()
    {
        string text = Geodesy.FormatDegreesDecimalMinutes(new LatLon(50.1109, 8.6821));

        Assert.Equal("N 50° 06.654' E 008° 40.926'", text);
    }

    [Fact]
    public void FormatDegreesDecimalMinutes_MarksSouthernAndWesternHemispheres()
    {
        string text = Geodesy.FormatDegreesDecimalMinutes(new LatLon(-33.8688, -151.2093));

        Assert.StartsWith("S 33°", text);
        Assert.Contains("W 151°", text);
    }
}

public class HazardPlumeTests
{
    private static readonly LatLon Origin = new(50.0, 8.0);

    [Fact]
    public void Build_OpensTheConeDownwind()
    {
        // Wind from the north means the plume travels south.
        var area = HazardPlume.Build(Origin, windFromDeg: 0, stability: PasquillClass.D, rangeMetres: 1000);

        Assert.Equal(180.0, area.DownwindBearingDeg, 1e-9);
        Assert.All(area.ConeOutline.Skip(1), point => Assert.True(point.Latitude < Origin.Latitude));
    }

    [Fact]
    public void Build_StartsAndStaysWithinTheRequestedRange()
    {
        var area = HazardPlume.Build(Origin, 270, PasquillClass.F, rangeMetres: 1500);

        Assert.Equal(Origin, area.ConeOutline[0]);
        foreach (var point in area.ConeOutline.Skip(1))
        {
            Assert.Equal(1500.0, Geodesy.DistanceMetres(Origin, point), 1.0);
        }
    }

    [Fact]
    public void HalfAngle_NarrowsAsAirBecomesMoreStable()
    {
        Assert.True(HazardPlume.HalfAngleFor(PasquillClass.A) > HazardPlume.HalfAngleFor(PasquillClass.D));
        Assert.True(HazardPlume.HalfAngleFor(PasquillClass.D) > HazardPlume.HalfAngleFor(PasquillClass.F));
    }

    [Fact]
    public void SuggestedRange_GrowsAsAirBecomesMoreStable()
    {
        Assert.True(HazardPlume.SuggestedRangeFor(PasquillClass.F) > HazardPlume.SuggestedRangeFor(PasquillClass.A));
    }

    [Fact]
    public void Build_UsesTheStabilityDefaultRangeWhenNoneGiven()
    {
        var area = HazardPlume.Build(Origin, 90, PasquillClass.E);
        Assert.Equal(HazardPlume.SuggestedRangeFor(PasquillClass.E), area.RangeMetres);
    }

    [Fact]
    public void Build_ProducesAClosedOutlineWithTheRequestedResolution()
    {
        var area = HazardPlume.Build(Origin, 45, PasquillClass.C, rangeMetres: 500, arcSegments: 12);

        // Apex plus 13 arc points.
        Assert.Equal(14, area.ConeOutline.Count);
        Assert.Equal(2, area.CentreLine.Count);
    }

    [Fact]
    public void TravelTime_IsUndefinedInCalmAir()
    {
        Assert.Null(HazardPlume.TravelTime(1000, 0.1));
    }

    [Fact]
    public void TravelTime_ScalesWithDistanceOverWindSpeed()
    {
        var travel = HazardPlume.TravelTime(1000, 5.0);

        Assert.NotNull(travel);
        Assert.Equal(200.0, travel!.Value.TotalSeconds, 1e-6);
    }
}

public class WeatherCodesTests
{
    [Fact]
    public void Describe_TranslatesKnownCodes()
    {
        Assert.Equal("Gewitter", WeatherCodes.Describe(95));
        Assert.Equal("Nebel", WeatherCodes.Describe(45));
    }

    [Fact]
    public void Describe_FallsBackForUnknownAndMissingCodes()
    {
        Assert.Equal("keine Angabe", WeatherCodes.Describe(null));
        Assert.Equal("keine Angabe", WeatherCodes.Describe(1234));
    }

    [Fact]
    public void IsSignificant_FlagsHazardousWeatherOnly()
    {
        Assert.True(WeatherCodes.IsSignificant(99));
        Assert.True(WeatherCodes.IsSignificant(45));
        Assert.False(WeatherCodes.IsSignificant(1));
        Assert.False(WeatherCodes.IsSignificant(null));
    }
}
