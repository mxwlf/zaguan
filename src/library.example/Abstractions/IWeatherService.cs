namespace Library.Example.Abstractions;

/// <summary>
/// Represents a weather information service.
/// </summary>
public interface IWeatherService
{
    /// <summary>
    /// Gets the weather information for the specified zipcode.
    /// </summary>
    /// <param name="zipCode">The 5-digit zipcode of the place the weather is being requested for.</param>
    /// <param name="cancellationToken">A token that cancels the request.</param>
    /// <returns>
    /// A task that resolves to the <see cref="WeatherInformation"/> currently reported for
    /// <paramref name="zipCode"/>.
    /// </returns>
    Task<WeatherInformation> GetWeatherInformationAsync(int zipCode, CancellationToken cancellationToken);
}
