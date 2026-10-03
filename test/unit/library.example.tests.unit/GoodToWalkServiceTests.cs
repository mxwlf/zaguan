using AutoFixture;

using AwesomeAssertions;

using Library.Example.Abstractions;

using Microsoft.Extensions.Logging;

using NSubstitute;

namespace Library.Example.Tests.Unit;

/// <summary>
/// Tests for <see cref="GoodToWalkService"/>.
/// </summary>
/// <param name="outputHelper">The xunit helper that collects output for the running test.</param>
public class GoodToWalkServiceTests(ITestOutputHelper outputHelper)
{
    // Supplies values that are irrelevant to the behavior under test.
    private readonly IFixture _fixture = new Fixture();

    // A real logger rather than a substitute, so what the service logs is attributed to the test
    // that produced it and shows up in that test's output. None of these tests assert on logging,
    // but XUnitLogger enables every level and formats each message eagerly, so a broken log message
    // template surfaces as a failure rather than as silently dead code.
    private readonly ILogger<GoodToWalkService> _logger = outputHelper.ToLogger<GoodToWalkService>();

    [Fact]
    public void Constructor_WhenTheWeatherServiceIsNull_ThenThrowsArgumentNullException()
    {
        // Arrange
        // Act
        var act = () => new GoodToWalkService(null!, _logger);

        // Assert
        act.Should()
            .Throw<ArgumentNullException>("the service cannot produce a recommendation without a weather source, so an unusable instance must be rejected at construction time instead of failing later on every call")
            .WithParameterName("weatherService");
    }

    [Fact]
    public void Constructor_WhenTheLoggerIsNull_ThenThrowsArgumentNullException()
    {
        // Arrange
        var weatherService = Substitute.For<IWeatherService>();

        // Act
        var act = () => new GoodToWalkService(weatherService, null!);

        // Assert
        act.Should()
            .Throw<ArgumentNullException>("a missing logger is a composition mistake that must surface at construction time rather than as a NullReferenceException on the first recommendation")
            .WithParameterName("logger");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("1234")]
    [InlineData("123456")]
    [InlineData("9021a")]
    [InlineData("90 21")]
    [InlineData("-1234")]
    [InlineData("+1234")]
    [InlineData("902.1")]
    public async Task IsItGoodToWalkAsync_WhenTheZipCodeIsNotAFiveDigitNumericString_ThenThrowsArgumentException(string? invalidZipCode)
    {
        // Arrange
        var weatherService = Substitute.For<IWeatherService>();
        var sut = new GoodToWalkService(weatherService, _logger);

        // Act
        var act = async () => await sut.IsItGoodToWalkAsync(invalidZipCode!, TestContext.Current.CancellationToken);

        // Assert
        await act.Should()
            .ThrowAsync<ArgumentException>("the weather service is addressed by a numeric zipcode, so anything that is not exactly five digits cannot be resolved to a location and must be rejected rather than silently queried")
            .WithParameterName("zipCode");

        await weatherService.DidNotReceiveWithAnyArgs()
            .GetWeatherInformationAsync(default, TestContext.Current.CancellationToken);
    }

    [Theory]
    [InlineData("90210", 90210)]
    [InlineData("00501", 501)]
    [InlineData("99999", 99999)]
    public async Task IsItGoodToWalkAsync_WhenTheZipCodeIsAFiveDigitNumericString_ThenQueriesTheWeatherServiceWithItsNumericValue(string zipCode, int expectedZipCode)
    {
        // Arrange
        var weatherService = WeatherServiceReporting(temperatureInFahrenheit: 70, probabilityOfRain: 0);
        var sut = new GoodToWalkService(weatherService, _logger);

        // Act
        await sut.IsItGoodToWalkAsync(zipCode, TestContext.Current.CancellationToken);

        // Assert
        await weatherService.Received(1)
            .GetWeatherInformationAsync(expectedZipCode, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(65)]
    [InlineData(66)]
    [InlineData(77.5)]
    [InlineData(89.9)]
    [InlineData(90)]
    public async Task IsItGoodToWalkAsync_WhenTheTemperatureIsBetween65And90AndRainIsUnlikely_ThenAnswersYes(double temperatureInFahrenheit)
    {
        // Arrange
        var weatherService = WeatherServiceReporting(temperatureInFahrenheit, probabilityOfRain: 0);

        // Act
        var answer = await GoodToWalkService
            .IsItGoodToWalkAsync(_fixture.Create<int>(), weatherService, _logger, TestContext.Current.CancellationToken);

        // Assert
        answer.Should().Be(
            "Yes.",
            "{0}F falls inside the comfortable 65F to 90F walking range (endpoints included) and rain is unlikely, so the answer must be a plain yes",
            temperatureInFahrenheit);
    }

    [Theory]
    [InlineData(-20)]
    [InlineData(0)]
    [InlineData(32)]
    [InlineData(64)]
    [InlineData(64.9)]
    [InlineData(90.1)]
    [InlineData(91)]
    [InlineData(120)]
    public async Task IsItGoodToWalkAsync_WhenTheTemperatureIsOutside65To90_ThenAnswersNo(double temperatureInFahrenheit)
    {
        // Arrange
        var weatherService = WeatherServiceReporting(temperatureInFahrenheit, probabilityOfRain: 0);

        // Act
        var answer = await GoodToWalkService
            .IsItGoodToWalkAsync(_fixture.Create<int>(), weatherService, _logger, TestContext.Current.CancellationToken);

        // Assert
        answer.Should().Be(
            "No.",
            "{0}F falls outside the comfortable 65F to 90F walking range, so the walk is discouraged regardless of anything else the forecast reports",
            temperatureInFahrenheit);
    }

    [Theory]
    [InlineData(0.2501)]
    [InlineData(0.26)]
    [InlineData(0.5)]
    [InlineData(1)]
    public async Task IsItGoodToWalkAsync_WhenTheTemperatureIsComfortableAndRainIsMoreLikelyThan25Percent_ThenAnswersYesAndRecommendsAnUmbrella(double probabilityOfRain)
    {
        // Arrange
        var weatherService = WeatherServiceReporting(temperatureInFahrenheit: 70, probabilityOfRain);

        // Act
        var answer = await GoodToWalkService
            .IsItGoodToWalkAsync(_fixture.Create<int>(), weatherService, _logger, TestContext.Current.CancellationToken);

        // Assert
        answer.Should().Be(
            "Yes. But you should bring an umbrella",
            "the temperature is comfortable so the walk is still recommended, and a {0:P2} chance of rain exceeds the 25% umbrella threshold, so the umbrella advice must be appended to the yes",
            probabilityOfRain);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(0.1)]
    [InlineData(0.2499)]
    [InlineData(0.25)]
    public async Task IsItGoodToWalkAsync_WhenTheTemperatureIsComfortableAndRainIsNoMoreLikelyThan25Percent_ThenAnswersYesWithoutRecommendingAnUmbrella(double probabilityOfRain)
    {
        // Arrange
        var weatherService = WeatherServiceReporting(temperatureInFahrenheit: 70, probabilityOfRain);

        // Act
        var answer = await GoodToWalkService
            .IsItGoodToWalkAsync(_fixture.Create<int>(), weatherService, _logger, TestContext.Current.CancellationToken);

        // Assert
        answer.Should().Be(
            "Yes.",
            "the umbrella advice is only added when rain is *more* likely than 25%, so a {0:P2} chance must leave the answer as a plain yes",
            probabilityOfRain);
    }

    // Creates a weather service substitute that reports the supplied forecast for any zipcode.
    private static IWeatherService WeatherServiceReporting(double temperatureInFahrenheit, double probabilityOfRain)
    {
        var weatherService = Substitute.For<IWeatherService>();

        weatherService
            .GetWeatherInformationAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new WeatherInformation
            {
                TemperatureInFahrenheit = temperatureInFahrenheit,
                ProbabilityOfRain = probabilityOfRain,
            });

        return weatherService;
    }
}
