using System.Text.Json;
using ElwMeteo.Core.Meteorology;
using ElwMeteo.Core.Models;
using ElwMeteo.Core.Services;
using Xunit;

namespace ElwMeteo.Core.Tests;

public class WindFieldGridTests
{
    private static readonly LatLon Centre = new(50.0, 8.0);

    [Theory]
    [InlineData(3, 9)]
    [InlineData(5, 25)]
    [InlineData(9, 81)]
    public void BuildGrid_ProducesASquareGrid(int size, int expectedCount)
    {
        Assert.Equal(expectedCount, WindFieldProvider.BuildGrid(Centre, size, 1000).Count);
    }

    [Fact]
    public void BuildGrid_ClampsAnUnreasonableSize()
    {
        // Open-Meteo caps multi-coordinate requests; the grid must stay inside that.
        Assert.Equal(81, WindFieldProvider.BuildGrid(Centre, 50, 1000).Count);
        Assert.Equal(4, WindFieldProvider.BuildGrid(Centre, 1, 1000).Count);
    }

    [Fact]
    public void BuildGrid_PutsTheCentreInTheMiddleForOddSizes()
    {
        var grid = WindFieldProvider.BuildGrid(Centre, 5, 1000);

        LatLon middle = grid[12]; // index 12 of 25 is the centre node
        Assert.Equal(Centre.Latitude, middle.Latitude, 1e-9);
        Assert.Equal(Centre.Longitude, middle.Longitude, 1e-9);
    }

    [Fact]
    public void BuildGrid_OrdersRowsFromNorthToSouth()
    {
        var grid = WindFieldProvider.BuildGrid(Centre, 3, 1000);

        // First row is the northern one, last row the southern.
        Assert.True(grid[0].Latitude > Centre.Latitude);
        Assert.True(grid[8].Latitude < Centre.Latitude);

        // Within a row, longitude increases eastwards.
        Assert.True(grid[0].Longitude < grid[1].Longitude);
        Assert.True(grid[1].Longitude < grid[2].Longitude);
    }

    [Fact]
    public void BuildGrid_HonoursTheRequestedSpacing()
    {
        var grid = WindFieldProvider.BuildGrid(Centre, 3, 2000);

        // Centre node is index 4; its northern neighbour is index 1.
        double distance = Geodesy.DistanceMetres(grid[4], grid[1]);
        Assert.Equal(2000.0, distance, 5.0);
    }

    [Fact]
    public void BuildGrid_EnforcesAMinimumSpacing()
    {
        var grid = WindFieldProvider.BuildGrid(Centre, 3, spacingMetres: 1);

        double distance = Geodesy.DistanceMetres(grid[4], grid[1]);
        Assert.True(distance >= 99.0);
    }

    [Fact]
    public void BuildUrl_SendsAllCoordinatesInOneRequest()
    {
        var grid = WindFieldProvider.BuildGrid(Centre, 3, 1000);
        string url = WindFieldProvider.BuildUrl(grid);

        // Nine coordinates means eight separators in each list.
        Assert.Equal(8, url.Split("latitude=")[1].Split('&')[0].Count(c => c == ','));
        Assert.Contains("wind_gusts_10m", url);
        Assert.Contains("wind_speed_unit=ms", url);
    }

    [Fact]
    public void BuildUrl_UsesInvariantDecimalSeparators()
    {
        string url = WindFieldProvider.BuildUrl([new LatLon(50.5, 8.25)]);

        Assert.Contains("latitude=50.5000", url);
        Assert.Contains("longitude=8.2500", url);
    }
}

public class WindFieldParsingTests
{
    private static readonly IReadOnlyList<LatLon> Grid =
    [
        new(50.01, 8.0), new(50.0, 8.0), new(49.99, 8.0)
    ];

    [Fact]
    public void Parse_ReadsAnArrayOfLocations()
    {
        using var document = JsonDocument.Parse("""
        [
          {"latitude":50.01,"longitude":8.0,"current":{"wind_speed_10m":4.2,"wind_direction_10m":225,"wind_gusts_10m":8.1}},
          {"latitude":50.0,"longitude":8.0,"current":{"wind_speed_10m":5.0,"wind_direction_10m":230,"wind_gusts_10m":9.0}},
          {"latitude":49.99,"longitude":8.0,"current":{"wind_speed_10m":6.1,"wind_direction_10m":240,"wind_gusts_10m":11.2}}
        ]
        """);

        var field = WindFieldProvider.Parse(document.RootElement, Grid, 1000);

        Assert.Equal(3, field.Points.Count);
        Assert.All(field.Points, p => Assert.True(p.HasData));
        Assert.Equal(4.2, field.Points[0].SpeedMs);
        Assert.Equal(240, field.Points[2].DirectionDeg);
        Assert.Equal(1000, field.SpacingMetres);
    }

    [Fact]
    public void Parse_AcceptsASingleObjectResponse()
    {
        using var document = JsonDocument.Parse("""
        {"latitude":50.0,"longitude":8.0,"current":{"wind_speed_10m":3.0,"wind_direction_10m":90,"wind_gusts_10m":5.0}}
        """);

        var field = WindFieldProvider.Parse(document.RootElement, [new LatLon(50.0, 8.0)], 500);

        Assert.Single(field.Points);
        Assert.Equal(3.0, field.Points[0].SpeedMs);
    }

    [Fact]
    public void Parse_FillsGapsWhenTheResponseIsShort()
    {
        using var document = JsonDocument.Parse("""
        [{"latitude":50.01,"longitude":8.0,"current":{"wind_speed_10m":4.0,"wind_direction_10m":180,"wind_gusts_10m":6.0}}]
        """);

        var field = WindFieldProvider.Parse(document.RootElement, Grid, 1000);

        // Every requested node is represented, even the ones with no answer.
        Assert.Equal(3, field.Points.Count);
        Assert.True(field.Points[0].HasData);
        Assert.False(field.Points[1].HasData);
        Assert.Equal(50.0, field.Points[1].Latitude);
    }

    [Fact]
    public void Parse_ToleratesAMissingCurrentBlock()
    {
        using var document = JsonDocument.Parse("""[{"latitude":50.01,"longitude":8.0}]""");

        var field = WindFieldProvider.Parse(document.RootElement, [Grid[0]], 1000);

        Assert.False(field.Points[0].HasData);
        Assert.Null(field.Points[0].SpeedMs);
    }

    [Fact]
    public void DownwindDeg_IsTheReciprocalOfTheReportedDirection()
    {
        var point = new WindFieldPoint(50, 8, 5, 8, 270);
        Assert.Equal(90, point.DownwindDeg);
    }

    [Fact]
    public void DirectionSpread_ReportsTheWidestDisagreement()
    {
        var field = new WindField(DateTimeOffset.UnixEpoch, 1000,
        [
            new WindFieldPoint(50, 8, 5, 8, 350),
            new WindFieldPoint(50, 8, 5, 8, 10),
            new WindFieldPoint(50, 8, 5, 8, 60)
        ]);

        // 350° to 60° is 70° the short way round.
        Assert.Equal(70.0, field.DirectionSpreadDeg!.Value, 1e-9);
    }

    [Fact]
    public void DirectionSpread_IsZeroForAUniformFlow()
    {
        var field = new WindField(DateTimeOffset.UnixEpoch, 1000,
        [
            new WindFieldPoint(50, 8, 5, 8, 180),
            new WindFieldPoint(50, 8, 6, 9, 180)
        ]);

        Assert.Equal(0.0, field.DirectionSpreadDeg!.Value, 1e-9);
        Assert.Equal(1.0, field.SpeedSpreadMs!.Value, 1e-9);
    }

    [Fact]
    public void Spreads_AreUndefinedWithoutEnoughPoints()
    {
        var field = new WindField(DateTimeOffset.UnixEpoch, 1000,
            [new WindFieldPoint(50, 8, 5, 8, 180)]);

        Assert.Null(field.DirectionSpreadDeg);
        Assert.Null(field.SpeedSpreadMs);
        Assert.False(field.IsEmpty);
        Assert.True(WindField.Empty.IsEmpty);
    }
}
