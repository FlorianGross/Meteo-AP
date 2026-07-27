using ElwMeteo.Core.Meteorology;
using Xunit;

namespace ElwMeteo.Core.Tests;

public class SignedDifferenceTests
{
    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(0, 90, 90)]
    [InlineData(90, 0, -90)]
    [InlineData(350, 10, 20)]     // wraps forward past north
    [InlineData(10, 350, -20)]    // wraps backward past north
    [InlineData(0, 180, 180)]
    [InlineData(180, 0, 180)]     // exactly opposite resolves to +180
    [InlineData(270, 45, 135)]
    public void SignedDifference_TakesTheShortWayRound(double from, double to, double expected)
    {
        Assert.Equal(expected, WindShiftDetector.SignedDifference(from, to), 1e-9);
    }

    [Fact]
    public void SignedDifference_StaysWithinHalfATurn()
    {
        for (double from = 0; from < 360; from += 7)
        {
            for (double to = 0; to < 360; to += 11)
            {
                double delta = WindShiftDetector.SignedDifference(from, to);
                Assert.InRange(delta, -180.0, 180.0);
            }
        }
    }
}

public class WindShiftDetectorTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 27, 12, 0, 0, TimeSpan.Zero);

    private static (DateTimeOffset, double?, double?) Step(int hoursFromNow, double? direction, double speed = 5.0) =>
        (Now.AddHours(hoursFromNow), direction, speed);

    [Fact]
    public void Detect_FindsTheFirstSignificantTurn()
    {
        var forecast = new[]
        {
            Step(1, 230),   // 5° — noise
            Step(2, 250),   // 25° — still below threshold
            Step(3, 290),   // 65° — this is the one
            Step(4, 310)
        };

        var shift = WindShiftDetector.Detect(225, forecast, Now, TimeSpan.FromHours(6));

        Assert.NotNull(shift);
        Assert.Equal(Now.AddHours(3), shift!.Time);
        Assert.Equal(290, shift.ToDeg);
        Assert.Equal(65, shift.SignedDeltaDeg, 1e-9);
        Assert.Equal(TurnDirection.Veering, shift.Direction);
        Assert.Equal("rechtsdrehend", shift.DirectionLabel);
    }

    [Fact]
    public void Detect_RecognisesAnAnticlockwiseTurn()
    {
        var forecast = new[] { Step(2, 180) };

        var shift = WindShiftDetector.Detect(270, forecast, Now, TimeSpan.FromHours(6));

        Assert.NotNull(shift);
        Assert.Equal(TurnDirection.Backing, shift!.Direction);
        Assert.Equal("linksdrehend", shift.DirectionLabel);
        Assert.Equal(90, shift.AbsoluteDeltaDeg, 1e-9);
    }

    [Fact]
    public void Detect_ReturnsNullWhenTheWindHolds()
    {
        var forecast = new[] { Step(1, 230), Step(2, 220), Step(3, 240) };

        Assert.Null(WindShiftDetector.Detect(225, forecast, Now, TimeSpan.FromHours(6)));
    }

    [Fact]
    public void Detect_IgnoresStepsBeyondTheHorizon()
    {
        // The big turn only happens after the look-ahead window closes.
        var forecast = new[] { Step(1, 230), Step(8, 20) };

        Assert.Null(WindShiftDetector.Detect(225, forecast, Now, TimeSpan.FromHours(6)));
    }

    [Fact]
    public void Detect_IgnoresStepsInThePast()
    {
        var forecast = new[] { Step(-2, 20), Step(1, 230) };

        Assert.Null(WindShiftDetector.Detect(225, forecast, Now, TimeSpan.FromHours(6)));
    }

    [Fact]
    public void Detect_IgnoresDirectionReportedInNearlyCalmAir()
    {
        // Below 1 m/s the reported direction is noise, not a real shift.
        var forecast = new[] { Step(1, 20, speed: 0.4), Step(2, 230, speed: 5.0) };

        Assert.Null(WindShiftDetector.Detect(225, forecast, Now, TimeSpan.FromHours(6)));
    }

    [Fact]
    public void Detect_SkipsStepsWithoutADirection()
    {
        var forecast = new[] { Step(1, null), Step(2, 300) };

        var shift = WindShiftDetector.Detect(225, forecast, Now, TimeSpan.FromHours(6));

        Assert.NotNull(shift);
        Assert.Equal(Now.AddHours(2), shift!.Time);
    }

    [Fact]
    public void Detect_HonoursACustomThreshold()
    {
        var forecast = new[] { Step(1, 250) };  // a 25° turn

        Assert.Null(WindShiftDetector.Detect(225, forecast, Now, TimeSpan.FromHours(6)));
        Assert.NotNull(WindShiftDetector.Detect(225, forecast, Now, TimeSpan.FromHours(6), thresholdDeg: 20));
    }

    [Fact]
    public void Detect_HandlesAnEmptyForecast()
    {
        Assert.Null(WindShiftDetector.Detect(225, [], Now, TimeSpan.FromHours(6)));
    }

    [Fact]
    public void WindShift_ReportsTheNewPlumeDirection()
    {
        var shift = new WindShift(Now.AddHours(2), 225, 315, 90, TurnDirection.Veering);

        // Wind from 315° (NW) sends the plume to 135° (SE).
        Assert.Equal(135, shift.NewDownwindDeg, 1e-9);

        string text = shift.Describe(Now);
        Assert.Contains("rechtsdrehend", text);
        Assert.Contains("nach NW", text);
        Assert.Contains("Ausbreitung dann nach SO", text);
    }

    [Fact]
    public void WindShift_UsesMinutesForNearTermTurns()
    {
        var shift = new WindShift(Now.AddMinutes(45), 225, 315, 90, TurnDirection.Veering);

        Assert.Contains("in ca. 45 min", shift.Describe(Now));
    }
}

public class GustPeakTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 27, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void PeakGust_FindsTheStrongestGustInWindow()
    {
        var forecast = new (DateTimeOffset, double?)[]
        {
            (Now.AddHours(1), 12.0),
            (Now.AddHours(2), 21.0),
            (Now.AddHours(3), 15.0)
        };

        var peak = WindShiftDetector.PeakGust(forecast, Now, TimeSpan.FromHours(6));

        Assert.NotNull(peak);
        Assert.Equal(21.0, peak!.GustMs);
        Assert.Equal(Now.AddHours(2), peak.Time);
    }

    [Fact]
    public void PeakGust_IgnoresValuesOutsideTheWindow()
    {
        var forecast = new (DateTimeOffset, double?)[]
        {
            (Now.AddHours(1), 12.0),
            (Now.AddHours(9), 30.0)
        };

        var peak = WindShiftDetector.PeakGust(forecast, Now, TimeSpan.FromHours(6));

        Assert.Equal(12.0, peak!.GustMs);
    }

    [Fact]
    public void PeakGust_ReturnsNullWithoutData()
    {
        Assert.Null(WindShiftDetector.PeakGust([], Now, TimeSpan.FromHours(6)));
        Assert.Null(WindShiftDetector.PeakGust([(Now.AddHours(1), null)], Now, TimeSpan.FromHours(6)));
    }

    [Fact]
    public void GustPeak_DescribesTheTimingInGermanUnits()
    {
        var peak = new GustPeak(Now.AddMinutes(40), 20.0);

        string text = peak.Describe(Now);
        Assert.Contains("72 km/h", text);
        Assert.Contains("in ca. 40 min", text);
    }
}
