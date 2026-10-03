using System.Globalization;

using Library.Example.Abstractions;
using Microsoft.Extensions.Logging;

namespace Library.Example;

/// <summary>
/// A service that suggests if the weather is good to go for a walk.
/// </summary>
public class GoodToWalkService
{
    // The temperature range, in degrees Fahrenheit, that is comfortable for a walk. Both endpoints
    // are treated as comfortable.
    private const double MinimumComfortableTemperatureInFahrenheit = 65;
    private const double MaximumComfortableTemperatureInFahrenheit = 90;

    // The probability of rain, as a fraction between 0 and 1, above which an umbrella is worth
    // carrying.
    private const double UmbrellaProbabilityOfRainThreshold = 0.25;

    private readonly IWeatherService _weatherService;

    private readonly ILogger<GoodToWalkService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="GoodToWalkService"/> class.
    /// </summary>
    /// <param name="weatherService">The <see cref="IWeatherService"/> used to read the forecast.</param>
    /// <param name="logger">The logger that records each recommendation.</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="weatherService"/> or <paramref name="logger"/> is <see langword="null"/>.
    /// </exception>
    public GoodToWalkService(IWeatherService weatherService, ILogger<GoodToWalkService> logger)
    {
        _weatherService = weatherService ?? throw new ArgumentNullException(nameof(weatherService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Gets a recommendation on whether the current weather at the specified zipcode is good for a walk.
    /// </summary>
    /// <param name="zipCode">The 5-digit zipcode of the place the recommendation is being requested for.</param>
    /// <param name="cancellationToken">A token that cancels the request.</param>
    /// <returns>
    /// A task that resolves to <c>"Yes."</c> when the temperature is between 65 and 90 degrees
    /// Fahrenheit, with <c>" But you should bring an umbrella"</c> appended when the probability of
    /// rain is more than 25%; otherwise <c>"No."</c>.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="zipCode"/> is not a 5-digit numeric string.
    /// </exception>
    public async Task<string> IsItGoodToWalkAsync(string zipCode, CancellationToken cancellationToken)
    {
        // NumberStyles.None keeps the parse to bare digits: the default styles would also accept a
        // leading sign or surrounding whitespace, so "+1234" would be read as the zipcode 1234.
        if (!string.IsNullOrWhiteSpace(zipCode) &&
            zipCode.Length == 5 &&
            int.TryParse(zipCode, NumberStyles.None, CultureInfo.InvariantCulture, out var numericZipCode))
        {
            return await IsItGoodToWalkAsync(numericZipCode, _weatherService, _logger, cancellationToken).ConfigureAwait(false);
        }

        throw new ArgumentException("The zipcode has to be a 5-digit numeric string", nameof(zipCode));
    }

    /// <summary>
    /// Reads the forecast for a zipcode and turns it into a walk recommendation.
    /// </summary>
    /// <param name="zipCode">The numeric zipcode of the place the recommendation is being requested for.</param>
    /// <param name="weatherService">The <see cref="IWeatherService"/> used to read the forecast.</param>
    /// <param name="logger">The logger that records the recommendation.</param>
    /// <param name="cancellationToken">A token that cancels the request.</param>
    /// <inheritdoc cref="IsItGoodToWalkAsync(string, CancellationToken)" path="/returns"/>
    internal static async Task<string> IsItGoodToWalkAsync(int zipCode, IWeatherService weatherService, ILogger<GoodToWalkService> logger, CancellationToken cancellationToken)
    {
        var weather = await weatherService.GetWeatherInformationAsync(zipCode, cancellationToken).ConfigureAwait(false);

        // The forecast is recorded as soon as it arrives, and property by property because
        // WeatherInformation does not override ToString. Logging it here rather than only alongside
        // the recommendation keeps what the weather service reported on record even when a later
        // failure means no recommendation is ever produced.
        // The IsEnabled guard keeps the temperature and probability from being boxed into the
        // params array when the level is off.
#pragma warning disable CA1848 // The ILogger extension methods are preferred here over the LoggerMessage delegates.
        if (logger.IsEnabled(LogLevel.Debug))
        {
            logger.LogDebug(
                "Weather service reported {TemperatureInFahrenheit}F with a {ProbabilityOfRain} probability of rain for zipcode {ZipCode}",
                weather.TemperatureInFahrenheit,
                weather.ProbabilityOfRain,
                zipCode);
        }
#pragma warning restore CA1848

        string recommendation;

        if (weather.TemperatureInFahrenheit is < MinimumComfortableTemperatureInFahrenheit or > MaximumComfortableTemperatureInFahrenheit)
        {
            recommendation = "No.";
        }
        else if (weather.ProbabilityOfRain > UmbrellaProbabilityOfRainThreshold)
        {
            recommendation = "Yes. But you should bring an umbrella";
        }
        else
        {
            recommendation = "Yes.";
        }

        // Logs the recommendation together with the forecast it was derived from.
#pragma warning disable CA1848 // The ILogger extension methods are preferred here over the LoggerMessage delegates.
        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation(
                "Walk recommendation for zipcode {ZipCode} at {TemperatureInFahrenheit}F with a {ProbabilityOfRain} probability of rain: {Recommendation}",
                zipCode,
                weather.TemperatureInFahrenheit,
                weather.ProbabilityOfRain,
                recommendation);
        }
#pragma warning restore CA1848

        return recommendation;
    }
}
