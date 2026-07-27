using ElwMeteo.Core.Meteorology;
using ElwMeteo.Core.Models;
using Xunit;

namespace ElwMeteo.Core.Tests;

public class WindVectorTests
{
    [Fact]
    public void FromMeteorological_NortherlyBlowsSouthward()
    {
        // Wind "from north" travels towards the south: V negative, U zero.
        var vector = WindVector.FromMeteorological(10, 0);

        Assert.Equal(0.0, vector.U, 1e-9);
        Assert.Equal(-10.0, vector.V, 1e-9);
    }

    [Fact]
    public void FromMeteorological_WesterlyBlowsEastward()
    {
        var vector = WindVector.FromMeteorological(10, 270);

        Assert.Equal(10.0, vector.U, 1e-9);
        Assert.Equal(0.0, vector.V, 1e-9);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(45)]
    [InlineData(123)]
    [InlineData(270)]
    [InlineData(359)]
    public void FromMeteorological_RoundTripsThroughDirectionAndSpeed(double direction)
    {
        var vector = WindVector.FromMeteorological(7.5, direction);

        Assert.Equal(7.5, vector.SpeedMs, 1e-9);
        Assert.Equal(direction, vector.DirectionFromDeg, 1e-6);
    }

    [Fact]
    public void DownwindDeg_IsTheReciprocalOfTheSourceDirection()
    {
        var vector = WindVector.FromMeteorological(5, 225);
        Assert.Equal(45.0, vector.DownwindDeg, 1e-6);
    }

    [Fact]
    public void DirectionFromDeg_IsWellDefinedInDeadCalm()
    {
        var vector = new WindVector(0, 0);

        Assert.Equal(0.0, vector.SpeedMs);
        Assert.Equal(0.0, vector.DirectionFromDeg);
    }

    [Fact]
    public void Lerp_BlendsComponentsLinearly()
    {
        var blended = WindVector.Lerp(new WindVector(0, 0), new WindVector(10, -4), 0.25);

        Assert.Equal(2.5, blended.U, 1e-9);
        Assert.Equal(-1.0, blended.V, 1e-9);
    }
}

public class WindGridTests
{
    /// <summary>A 3×3 field spanning 50.0–50.2 N and 8.0–8.2 E.</summary>
    private static WindField BuildField(params (double Speed, double Direction)[] nodes)
    {
        var points = new List<WindFieldPoint>();
        int index = 0;

        for (int row = 0; row < 3; row++)
        {
            for (int column = 0; column < 3; column++)
            {
                var (speed, direction) = nodes[index++];
                points.Add(new WindFieldPoint(
                    50.2 - row * 0.1,
                    8.0 + column * 0.1,
                    speed,
                    speed * 1.5,
                    direction));
            }
        }

        return new WindField(DateTimeOffset.UnixEpoch, 1000, points);
    }

    private static WindField UniformField(double speed, double direction) =>
        BuildField(Enumerable.Repeat((speed, direction), 9).ToArray());

    [Fact]
    public void ToGrid_DescribesTheBoundingBoxAndSize()
    {
        var grid = UniformField(10, 270).ToGrid();

        Assert.NotNull(grid);
        Assert.Equal(3, grid!.Size);
        Assert.Equal(50.2, grid.North, 1e-9);
        Assert.Equal(50.0, grid.South, 1e-9);
        Assert.Equal(8.0, grid.West, 1e-9);
        Assert.Equal(8.2, grid.East, 1e-9);
        Assert.Equal(10.0, grid.MaxSpeedMs, 1e-9);
    }

    [Fact]
    public void Sample_ReturnsTheUniformVectorEverywhere()
    {
        var grid = UniformField(10, 270).ToGrid()!;

        foreach (var (lat, lon) in new[] { (50.0, 8.0), (50.1, 8.1), (50.2, 8.2), (50.05, 8.17) })
        {
            var wind = grid.Sample(lat, lon);
            Assert.Equal(10.0, wind.SpeedMs, 1e-6);
            Assert.Equal(270.0, wind.DirectionFromDeg, 1e-6);
        }
    }

    [Fact]
    public void Sample_InterpolatesBetweenNodes()
    {
        // North row calm, south row 10 m/s, all westerly.
        var field = BuildField(
            (0, 270), (0, 270), (0, 270),
            (5, 270), (5, 270), (5, 270),
            (10, 270), (10, 270), (10, 270));

        var grid = field.ToGrid()!;

        Assert.Equal(0.0, grid.Sample(50.2, 8.1).SpeedMs, 1e-6);
        Assert.Equal(5.0, grid.Sample(50.1, 8.1).SpeedMs, 1e-6);
        Assert.Equal(10.0, grid.Sample(50.0, 8.1).SpeedMs, 1e-6);

        // Halfway between the north and middle rows.
        Assert.Equal(2.5, grid.Sample(50.15, 8.1).SpeedMs, 1e-6);
    }

    [Fact]
    public void Sample_ClampsToTheEdgeOutsideTheGrid()
    {
        var grid = UniformField(8, 180).ToGrid()!;

        // Well outside the box in every direction; the edge value still applies.
        Assert.Equal(8.0, grid.Sample(60.0, 20.0).SpeedMs, 1e-6);
        Assert.Equal(8.0, grid.Sample(40.0, -5.0).SpeedMs, 1e-6);
    }

    [Fact]
    public void ToGrid_SubstitutesTheMeanForNodesWithoutData()
    {
        var points = new List<WindFieldPoint>();
        for (int i = 0; i < 9; i++)
        {
            // The centre node has no reading.
            points.Add(i == 4
                ? new WindFieldPoint(50.1, 8.1, null, null, null)
                : new WindFieldPoint(50.2 - i / 3 * 0.1, 8.0 + i % 3 * 0.1, 6, 9, 270));
        }

        var grid = new WindField(DateTimeOffset.UnixEpoch, 1000, points).ToGrid();

        Assert.NotNull(grid);

        // The gap borrows the field mean instead of punching a calm into the middle.
        var centre = grid!.Sample(50.1, 8.1);
        Assert.Equal(6.0, centre.SpeedMs, 1e-6);
        Assert.Equal(270.0, centre.DirectionFromDeg, 1e-6);
    }

    [Fact]
    public void ToGrid_ReturnsNullWhenNoNodeHasData()
    {
        var points = Enumerable.Range(0, 9)
            .Select(i => new WindFieldPoint(50 + i * 0.1, 8, null, null, null))
            .ToList();

        Assert.Null(new WindField(DateTimeOffset.UnixEpoch, 1000, points).ToGrid());
    }

    [Fact]
    public void ToGrid_ReturnsNullForANonSquareField()
    {
        var points = new List<WindFieldPoint>
        {
            new(50.0, 8.0, 5, 8, 270),
            new(50.1, 8.0, 5, 8, 270)
        };

        Assert.Null(new WindField(DateTimeOffset.UnixEpoch, 1000, points).ToGrid());
        Assert.Null(WindField.Empty.ToGrid());
    }

    [Fact]
    public void Grid_IsOrderedNorthRowFirst()
    {
        // Distinct speeds per row let us confirm the row ordering directly.
        var field = BuildField(
            (2, 270), (2, 270), (2, 270),
            (5, 270), (5, 270), (5, 270),
            (9, 270), (9, 270), (9, 270));

        var grid = field.ToGrid()!;

        // Index 0 is the north-west node.
        Assert.Equal(2.0, new WindVector(grid.U[0], grid.V[0]).SpeedMs, 1e-6);
        // Index 8 is the south-east node.
        Assert.Equal(9.0, new WindVector(grid.U[8], grid.V[8]).SpeedMs, 1e-6);
    }
}
