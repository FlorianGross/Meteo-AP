using ElwMeteo.Core.Services;
using Xunit;

namespace ElwMeteo.Core.Tests;

public class NmeaParserTests
{
    [Fact]
    public void ParseSentence_ReadsGgaFixWithAltitude()
    {
        var fix = NmeaParser.ParseSentence("$GPGGA,123519,4807.038,N,01131.000,E,1,08,0.9,545.4,M,46.9,M,,*47");

        Assert.NotNull(fix);
        Assert.Equal(48.1173, fix!.Latitude, 1e-4);
        Assert.Equal(11.5167, fix.Longitude, 1e-4);
        Assert.Equal(545.4, fix.AltitudeM);
        Assert.Equal(0.9, fix.HorizontalDilution);
        Assert.Equal(8, fix.SatelliteCount);
        Assert.Equal(new TimeOnly(12, 35, 19), fix.UtcTime);
    }

    [Fact]
    public void ParseSentence_RejectsGgaWithoutFix()
    {
        // Quality field 0 means no position fix available.
        Assert.Null(NmeaParser.ParseSentence("$GPGGA,123519,4807.038,N,01131.000,E,0,00,,,M,,M,,"));
    }

    [Fact]
    public void ParseSentence_ReadsRmcWithSpeedAndCourse()
    {
        var fix = NmeaParser.ParseSentence("$GPRMC,123519,A,4807.038,N,01131.000,E,022.4,084.4,230394,003.1,W*6A");

        Assert.NotNull(fix);
        Assert.Equal(48.1173, fix!.Latitude, 1e-4);
        Assert.Equal(84.4, fix.CourseOverGroundDeg);
        // 22.4 knots is about 11.5 m/s.
        Assert.Equal(11.52, fix.SpeedOverGroundMs!.Value, 0.05);
    }

    [Fact]
    public void ParseSentence_RejectsRmcMarkedVoid()
    {
        Assert.Null(NmeaParser.ParseSentence("$GPRMC,123519,V,4807.038,N,01131.000,E,022.4,084.4,230394,003.1,W"));
    }

    [Fact]
    public void ParseSentence_ReadsGll()
    {
        var fix = NmeaParser.ParseSentence("$GPGLL,4916.45,N,12311.12,W,225444,A");

        Assert.NotNull(fix);
        Assert.Equal(49.2742, fix!.Latitude, 1e-4);
        Assert.Equal(-123.1853, fix.Longitude, 1e-4);
    }

    [Fact]
    public void ParseSentence_AcceptsGnssTalkerIds()
    {
        // A multi-constellation receiver reports GN instead of GP.
        var fix = NmeaParser.ParseSentence("$GNGGA,123519,4807.038,N,01131.000,E,1,08,0.9,545.4,M,46.9,M,,");

        Assert.NotNull(fix);
        Assert.Equal(48.1173, fix!.Latitude, 1e-4);
    }

    [Fact]
    public void ParseSentence_SignsSouthernAndWesternHemispheres()
    {
        var fix = NmeaParser.ParseSentence("$GPGGA,123519,3352.128,S,15112.558,W,1,08,0.9,10.0,M,46.9,M,,");

        Assert.NotNull(fix);
        Assert.True(fix!.Latitude < 0);
        Assert.True(fix.Longitude < 0);
        Assert.Equal(-33.8688, fix.Latitude, 1e-4);
    }

    [Fact]
    public void ParseSentence_RejectsCorruptChecksum()
    {
        // Valid sentence body, deliberately wrong checksum.
        Assert.Null(NmeaParser.ParseSentence("$GPGGA,123519,4807.038,N,01131.000,E,1,08,0.9,545.4,M,46.9,M,,*00"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a sentence")]
    [InlineData("$GPGGA")]
    [InlineData("$GPXYZ,1,2,3")]
    [InlineData("$GPGGA,,,,,,,,,,,,,")]
    public void ParseSentence_ReturnsNullForUnusableInput(string? input)
    {
        Assert.Null(NmeaParser.ParseSentence(input));
    }

    [Fact]
    public void ParseCoordinate_SplitsDegreesFromMinutesCorrectly()
    {
        // Two degree digits for latitude, three for longitude.
        Assert.Equal(48.1173, NmeaParser.ParseCoordinate("4807.038", "N")!.Value, 1e-4);
        Assert.Equal(11.5167, NmeaParser.ParseCoordinate("01131.000", "E")!.Value, 1e-4);
    }

    [Fact]
    public void ParseCoordinate_RejectsUnknownHemisphere()
    {
        Assert.Null(NmeaParser.ParseCoordinate("4807.038", "X"));
    }

    [Fact]
    public void EstimatedAccuracy_DerivesFromHorizontalDilution()
    {
        var fix = NmeaParser.ParseSentence("$GPGGA,123519,4807.038,N,01131.000,E,1,08,1.4,545.4,M,46.9,M,,");

        Assert.NotNull(fix);
        Assert.Equal(7.0, fix!.EstimatedAccuracyM!.Value, 1e-9);
    }
}
