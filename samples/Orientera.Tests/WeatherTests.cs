using Orientera.Services.Weather;

namespace Orientera.Tests;

/// <summary>
/// Vädret är en utsmyckning, men en som påstår något: att det är så många grader där läsaren är,
/// just nu. De tre sätten det påståendet kan bli fel på — fel timme, obegripligt svar, gammalt
/// svar — är vad som testas.
/// </summary>
public class WeatherTests
{
    private static string Forecast(params (string Time, double Temperature, string Symbol)[] hours) =>
        $$"""
        {
          "properties": {
            "timeseries": [
              {{string.Join(",", hours.Select(h => $$"""
              {
                "time": "{{h.Time}}",
                "data": {
                  "instant": { "details": { "air_temperature": {{h.Temperature.ToString(System.Globalization.CultureInfo.InvariantCulture)}} } },
                  "next_1_hours": { "summary": { "symbol_code": "{{h.Symbol}}" } }
                }
              }
              """))}}
            ]
          }
        }
        """;

    [Fact]
    public void The_hour_nearest_now_is_the_one_that_is_read()
    {
        var now = DateTimeOffset.Parse("2026-08-25T09:40:00Z");

        var json = Forecast(
            ("2026-08-25T08:00:00Z", 12.1, "cloudy"),
            ("2026-08-25T09:00:00Z", 15.4, "fair_day"),
            ("2026-08-25T10:00:00Z", 18.2, "clearsky_day"),
            ("2026-08-25T11:00:00Z", 21.0, "clearsky_day"));

        var weather = MetForecast.Parse(json, now, "Gävle");

        Assert.NotNull(weather);
        Assert.Equal(18.2, weather.TemperatureC);
        Assert.Equal("clearsky_day", weather.Symbol);
    }

    [Fact]
    public void An_hour_that_has_already_passed_still_counts_when_it_is_the_closest()
    {
        var now = DateTimeOffset.Parse("2026-08-25T09:10:00Z");

        var json = Forecast(
            ("2026-08-25T09:00:00Z", 15.4, "fair_day"),
            ("2026-08-25T10:00:00Z", 18.2, "clearsky_day"));

        Assert.Equal(15.4, MetForecast.Parse(json, now, "Gävle")!.TemperatureC);
    }

    [Fact]
    public void The_place_is_the_callers_and_never_the_forecasts()
    {
        var json = Forecast(("2026-08-25T10:00:00Z", 18.2, "clearsky_day"));

        Assert.Equal("Sandviken",
            MetForecast.Parse(json, DateTimeOffset.Parse("2026-08-25T10:00:00Z"), "Sandviken")!.Place);
    }

    [Fact]
    public void A_symbol_that_only_covers_six_hours_is_still_a_symbol()
    {
        var json = """
        {
          "properties": {
            "timeseries": [
              {
                "time": "2026-08-25T10:00:00Z",
                "data": {
                  "instant": { "details": { "air_temperature": 18.2 } },
                  "next_6_hours": { "summary": { "symbol_code": "lightrain" } }
                }
              }
            ]
          }
        }
        """;

        Assert.Equal("lightrain",
            MetForecast.Parse(json, DateTimeOffset.Parse("2026-08-25T10:00:00Z"), "Gävle")!.Symbol);
    }

    [Theory]
    [InlineData("")]
    [InlineData("inte json alls")]
    [InlineData("""{ "properties": { "timeseries": [] } }""")]
    [InlineData("""{ "type": "Feature" }""")]
    public void Ett_svar_appen_inte_forstar_ger_ingen_rad(string json)
    {
        Assert.Null(MetForecast.Parse(json, DateTimeOffset.Parse("2026-08-25T10:00:00Z"), "Gävle"));
    }

    [Fact]
    public void A_temperature_without_a_symbol_is_not_half_a_weather()
    {
        var json = """
        {
          "properties": {
            "timeseries": [
              {
                "time": "2026-08-25T10:00:00Z",
                "data": { "instant": { "details": { "air_temperature": 18.2 } } }
              }
            ]
          }
        }
        """;

        Assert.Null(MetForecast.Parse(json, DateTimeOffset.Parse("2026-08-25T10:00:00Z"), "Gävle"));
    }

    [Theory]
    [InlineData("clearsky_day", "☀️", "klart")]
    [InlineData("clearsky_night", "🌙", "klart")]
    [InlineData("fair_night", "🌙", "lätt molnighet")]
    [InlineData("partlycloudy_day", "⛅", "växlande molnighet")]
    [InlineData("cloudy", "☁️", "mulet")]
    [InlineData("fog", "🌫️", "dimma")]
    [InlineData("lightrainshowers_day", "🌧️", "regn")]
    [InlineData("heavysleetshowers_day", "🌨️", "snöblandat regn")]
    [InlineData("lightsnow", "❄️", "snö")]
    public void Symbols_are_read_by_what_they_contain(string code, string sign, string spoken)
    {
        Assert.Equal(sign, WeatherWords.Symbol(code));
        Assert.Equal(spoken, WeatherWords.Spoken(code));
    }

    [Fact]
    public void Thunder_wins_over_what_it_rains_with()
    {
        Assert.Equal("⛈️", WeatherWords.Symbol("heavysleetshowersandthunder_day"));
        Assert.Equal("åska", WeatherWords.Spoken("heavysleetshowersandthunder_day"));
    }

    [Fact]
    public void A_code_nobody_has_seen_before_has_no_word_to_say()
    {
        Assert.Equal("🌡️", WeatherWords.Symbol("meteorstorm"));
        Assert.Equal(string.Empty, WeatherWords.Spoken("meteorstorm"));
    }

    [Fact]
    public void A_saved_weather_ages_in_both_directions()
    {
        var read = DateTimeOffset.Parse("2026-08-25T10:00:00Z");
        var weather = new CurrentWeather(18.2, "clearsky_day", "Gävle", read);

        Assert.True(WeatherStore.IsFresh(weather, read.AddMinutes(20)));
        Assert.False(WeatherStore.IsFresh(weather, read.AddMinutes(40)));

        Assert.True(WeatherStore.IsUsable(weather, read.AddHours(6)));
        Assert.False(WeatherStore.IsUsable(weather, read.AddHours(20)));

        // Tidsmaskinen flyttar appens dygn bakåt, och då ligger stämpeln i framtiden.
        Assert.True(WeatherStore.IsFresh(weather, read.AddMinutes(-20)));
        Assert.False(WeatherStore.IsFresh(weather, read.AddDays(-3)));
        Assert.False(WeatherStore.IsUsable(weather, read.AddDays(-3)));
    }
}
