using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using ElwMeteo.Core.Models;

namespace ElwMeteo.Core.Services;

public interface IWeatherProvider
{
    Task<WeatherSnapshot> GetAsync(GeoPosition position, CancellationToken cancellationToken = default);
}

/// <summary>
/// Reads current conditions, a 15-minute nowcast and an hourly forecast from
/// Open-Meteo. Over Central Europe this serves the DWD ICON-D2/ICON-EU chain, so
/// the numbers line up with what the DWD products on the map tab show.
///
/// No API key and no registration required; see https://open-meteo.com/en/license
/// (CC BY 4.0) for the attribution terms the About box reproduces.
/// </summary>
public sealed class OpenMeteoWeatherProvider(HttpClient httpClient) : IWeatherProvider
{
    private const string BaseUrl = "https://api.open-meteo.com/v1/forecast";

    private static readonly string[] CurrentFields =
    [
        "temperature_2m", "relative_humidity_2m", "apparent_temperature", "is_day",
        "precipitation", "rain", "snowfall", "weather_code", "cloud_cover",
        "pressure_msl", "surface_pressure", "wind_speed_10m", "wind_direction_10m",
        "wind_gusts_10m", "dew_point_2m"
    ];

    private static readonly string[] MinutelyFields =
    [
        "precipitation", "rain", "snowfall", "weather_code", "wind_speed_10m",
        "wind_gusts_10m", "wind_direction_10m", "temperature_2m", "cape",
        "lightning_potential"
    ];

    private static readonly string[] HourlyFields =
    [
        "temperature_2m", "relative_humidity_2m", "precipitation",
        "precipitation_probability", "wind_speed_10m", "wind_gusts_10m",
        "wind_direction_10m", "cloud_cover", "visibility", "cape", "weather_code"
    ];

    private static readonly string[] DailyFields =
    [
        "temperature_2m_min", "temperature_2m_max", "precipitation_sum",
        "uv_index_max", "wind_gusts_10m_max"
    ];

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public async Task<WeatherSnapshot> GetAsync(GeoPosition position, CancellationToken cancellationToken = default)
    {
        string url = BuildUrl(position);

        OpenMeteoResponse? response;
        try
        {
            response = await httpClient
                .GetFromJsonAsync<OpenMeteoResponse>(url, SerializerOptions, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (JsonException ex)
        {
            throw new WeatherProviderException("Antwort von Open-Meteo konnte nicht gelesen werden.", ex);
        }
        catch (HttpRequestException ex)
        {
            throw new WeatherProviderException($"Open-Meteo nicht erreichbar: {ex.Message}", ex);
        }

        if (response is null)
        {
            throw new WeatherProviderException("Open-Meteo hat eine leere Antwort geliefert.");
        }

        return Map(response, position);
    }

    internal static string BuildUrl(GeoPosition position)
    {
        var query = new List<string>
        {
            $"latitude={position.Latitude.ToString("F5", CultureInfo.InvariantCulture)}",
            $"longitude={position.Longitude.ToString("F5", CultureInfo.InvariantCulture)}",
            $"current={string.Join(',', CurrentFields)}",
            $"minutely_15={string.Join(',', MinutelyFields)}",
            $"hourly={string.Join(',', HourlyFields)}",
            $"daily={string.Join(',', DailyFields)}",
            // Epoch seconds sidesteps every time-zone ambiguity in the response.
            "timeformat=unixtime",
            "timezone=UTC",
            "wind_speed_unit=ms",
            "forecast_days=3",
            // Three hours ahead plus the last hour, which is what the nowcast strip shows.
            "forecast_minutely_15=12",
            "past_minutely_15=4"
        };

        return $"{BaseUrl}?{string.Join('&', query)}";
    }

    private static WeatherSnapshot Map(OpenMeteoResponse response, GeoPosition position)
    {
        OpenMeteoCurrent? current = response.Current;
        DateTimeOffset now = DateTimeOffset.UtcNow;

        var hourly = MapHourly(response.Hourly);

        // Visibility only exists on the hourly series, so borrow the current hour.
        var currentHour = hourly
            .Where(h => h.Time <= now)
            .OrderByDescending(h => h.Time)
            .FirstOrDefault();

        return new WeatherSnapshot
        {
            Position = position,
            RetrievedAtUtc = now,
            ObservationTime = FromUnix(current?.Time),
            TemperatureC = current?.Temperature2m,
            ApparentTemperatureC = current?.ApparentTemperature,
            DewPointC = current?.DewPoint2m,
            RelativeHumidityPercent = current?.RelativeHumidity2m,
            PressureMslHpa = current?.PressureMsl,
            SurfacePressureHpa = current?.SurfacePressure,
            WindSpeedMs = current?.WindSpeed10m,
            WindGustMs = current?.WindGusts10m,
            WindDirectionDeg = current?.WindDirection10m,
            CloudCoverPercent = current?.CloudCover,
            PrecipitationMm = current?.Precipitation,
            SnowfallCm = current?.Snowfall,
            WeatherCode = current?.WeatherCode,
            IsDay = current?.IsDay is null or 1,
            VisibilityM = currentHour?.VisibilityM,
            CapeJkg = currentHour?.CapeJkg,
            ElevationM = response.Elevation,
            ModelName = "Open-Meteo (DWD ICON)",
            Nowcast = MapNowcast(response.Minutely15),
            Hourly = hourly,
            Daily = MapDaily(response.Daily)
        };
    }

    private static List<NowcastStep> MapNowcast(OpenMeteoMinutely? block)
    {
        if (block?.Time is null)
        {
            return [];
        }

        var steps = new List<NowcastStep>(block.Time.Count);
        for (int i = 0; i < block.Time.Count; i++)
        {
            steps.Add(new NowcastStep(
                FromUnix(block.Time[i])!.Value,
                At(block.Precipitation, i),
                At(block.Rain, i),
                At(block.Snowfall, i),
                At(block.WindSpeed10m, i),
                At(block.WindGusts10m, i),
                At(block.WindDirection10m, i),
                At(block.Temperature2m, i),
                At(block.Cape, i),
                At(block.LightningPotential, i),
                AtInt(block.WeatherCode, i)));
        }

        return steps;
    }

    private static List<HourlyStep> MapHourly(OpenMeteoHourly? block)
    {
        if (block?.Time is null)
        {
            return [];
        }

        var steps = new List<HourlyStep>(block.Time.Count);
        for (int i = 0; i < block.Time.Count; i++)
        {
            steps.Add(new HourlyStep(
                FromUnix(block.Time[i])!.Value,
                At(block.Temperature2m, i),
                At(block.Precipitation, i),
                AtInt(block.PrecipitationProbability, i),
                At(block.WindSpeed10m, i),
                At(block.WindGusts10m, i),
                At(block.WindDirection10m, i),
                At(block.CloudCover, i),
                At(block.RelativeHumidity2m, i),
                At(block.Visibility, i),
                At(block.Cape, i),
                AtInt(block.WeatherCode, i)));
        }

        return steps;
    }

    private static List<DailySummary> MapDaily(OpenMeteoDaily? block)
    {
        if (block?.Time is null)
        {
            return [];
        }

        var days = new List<DailySummary>(block.Time.Count);
        for (int i = 0; i < block.Time.Count; i++)
        {
            days.Add(new DailySummary(
                FromUnix(block.Time[i])!.Value,
                At(block.Temperature2mMin, i),
                At(block.Temperature2mMax, i),
                At(block.PrecipitationSum, i),
                At(block.UvIndexMax, i),
                At(block.WindGusts10mMax, i)));
        }

        return days;
    }

    private static double? At(IReadOnlyList<double?>? series, int index) =>
        series is not null && index < series.Count ? series[index] : null;

    private static int? AtInt(IReadOnlyList<int?>? series, int index) =>
        series is not null && index < series.Count ? series[index] : null;

    private static DateTimeOffset? FromUnix(long? seconds) =>
        seconds is null ? null : DateTimeOffset.FromUnixTimeSeconds(seconds.Value);

    // --- DTOs -------------------------------------------------------------
    // Property names follow the JSON snake_case via explicit attributes so that
    // renaming a C# member can never silently break deserialisation.

    private sealed class OpenMeteoResponse
    {
        [JsonPropertyName("elevation")] public double? Elevation { get; set; }
        [JsonPropertyName("current")] public OpenMeteoCurrent? Current { get; set; }
        [JsonPropertyName("minutely_15")] public OpenMeteoMinutely? Minutely15 { get; set; }
        [JsonPropertyName("hourly")] public OpenMeteoHourly? Hourly { get; set; }
        [JsonPropertyName("daily")] public OpenMeteoDaily? Daily { get; set; }
    }

    private sealed class OpenMeteoCurrent
    {
        [JsonPropertyName("time")] public long? Time { get; set; }
        [JsonPropertyName("temperature_2m")] public double? Temperature2m { get; set; }
        [JsonPropertyName("relative_humidity_2m")] public double? RelativeHumidity2m { get; set; }
        [JsonPropertyName("apparent_temperature")] public double? ApparentTemperature { get; set; }
        [JsonPropertyName("dew_point_2m")] public double? DewPoint2m { get; set; }
        [JsonPropertyName("is_day")] public int? IsDay { get; set; }
        [JsonPropertyName("precipitation")] public double? Precipitation { get; set; }
        [JsonPropertyName("rain")] public double? Rain { get; set; }
        [JsonPropertyName("snowfall")] public double? Snowfall { get; set; }
        [JsonPropertyName("weather_code")] public int? WeatherCode { get; set; }
        [JsonPropertyName("cloud_cover")] public double? CloudCover { get; set; }
        [JsonPropertyName("pressure_msl")] public double? PressureMsl { get; set; }
        [JsonPropertyName("surface_pressure")] public double? SurfacePressure { get; set; }
        [JsonPropertyName("wind_speed_10m")] public double? WindSpeed10m { get; set; }
        [JsonPropertyName("wind_direction_10m")] public double? WindDirection10m { get; set; }
        [JsonPropertyName("wind_gusts_10m")] public double? WindGusts10m { get; set; }
    }

    private sealed class OpenMeteoMinutely
    {
        [JsonPropertyName("time")] public List<long>? Time { get; set; }
        [JsonPropertyName("precipitation")] public List<double?>? Precipitation { get; set; }
        [JsonPropertyName("rain")] public List<double?>? Rain { get; set; }
        [JsonPropertyName("snowfall")] public List<double?>? Snowfall { get; set; }
        [JsonPropertyName("weather_code")] public List<int?>? WeatherCode { get; set; }
        [JsonPropertyName("wind_speed_10m")] public List<double?>? WindSpeed10m { get; set; }
        [JsonPropertyName("wind_gusts_10m")] public List<double?>? WindGusts10m { get; set; }
        [JsonPropertyName("wind_direction_10m")] public List<double?>? WindDirection10m { get; set; }
        [JsonPropertyName("temperature_2m")] public List<double?>? Temperature2m { get; set; }
        [JsonPropertyName("cape")] public List<double?>? Cape { get; set; }
        [JsonPropertyName("lightning_potential")] public List<double?>? LightningPotential { get; set; }
    }

    private sealed class OpenMeteoHourly
    {
        [JsonPropertyName("time")] public List<long>? Time { get; set; }
        [JsonPropertyName("temperature_2m")] public List<double?>? Temperature2m { get; set; }
        [JsonPropertyName("relative_humidity_2m")] public List<double?>? RelativeHumidity2m { get; set; }
        [JsonPropertyName("precipitation")] public List<double?>? Precipitation { get; set; }
        [JsonPropertyName("precipitation_probability")] public List<int?>? PrecipitationProbability { get; set; }
        [JsonPropertyName("wind_speed_10m")] public List<double?>? WindSpeed10m { get; set; }
        [JsonPropertyName("wind_gusts_10m")] public List<double?>? WindGusts10m { get; set; }
        [JsonPropertyName("wind_direction_10m")] public List<double?>? WindDirection10m { get; set; }
        [JsonPropertyName("cloud_cover")] public List<double?>? CloudCover { get; set; }
        [JsonPropertyName("visibility")] public List<double?>? Visibility { get; set; }
        [JsonPropertyName("cape")] public List<double?>? Cape { get; set; }
        [JsonPropertyName("weather_code")] public List<int?>? WeatherCode { get; set; }
    }

    private sealed class OpenMeteoDaily
    {
        [JsonPropertyName("time")] public List<long>? Time { get; set; }
        [JsonPropertyName("temperature_2m_min")] public List<double?>? Temperature2mMin { get; set; }
        [JsonPropertyName("temperature_2m_max")] public List<double?>? Temperature2mMax { get; set; }
        [JsonPropertyName("precipitation_sum")] public List<double?>? PrecipitationSum { get; set; }
        [JsonPropertyName("uv_index_max")] public List<double?>? UvIndexMax { get; set; }
        [JsonPropertyName("wind_gusts_10m_max")] public List<double?>? WindGusts10mMax { get; set; }
    }
}

public sealed class WeatherProviderException : Exception
{
    public WeatherProviderException(string message) : base(message)
    {
    }

    public WeatherProviderException(string message, Exception inner) : base(message, inner)
    {
    }
}
