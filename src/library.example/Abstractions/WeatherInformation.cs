namespace Library.Example.Abstractions;

/// <summary>
/// The weather information reported by the weather service.
/// </summary>
public class WeatherInformation
{
    /// <summary>
    /// Gets or sets the current temperature.
    /// </summary>
    /// <value>The temperature reported by the weather service, in degrees Fahrenheit.</value>
    public double TemperatureInFahrenheit { get; set; }

    /// <summary>
    /// Gets or sets the probability of rain.
    /// </summary>
    /// <value>
    /// The probability of rain expressed as a fraction between 0 and 1, where <c>0.25</c> means a
    /// 25% chance of rain.
    /// </value>
    public double ProbabilityOfRain { get; set; }
}
