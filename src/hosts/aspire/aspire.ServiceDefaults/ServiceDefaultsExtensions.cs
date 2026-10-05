using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Aspire.ServiceDefaults;

/// <summary>
/// Adds the services every project in this solution shares: service discovery, resilience, health
/// checks and OpenTelemetry. Reference this project from each service project and call
/// <see cref="AddServiceDefaults{TBuilder}(TBuilder)"/> on its builder.
/// </summary>
/// <remarks>
/// See <see href="https://aka.ms/aspire/service-defaults"/> for what each default does and when to
/// override it. The type is named for what it configures rather than plain <c>Extensions</c>,
/// because that name collides with the <c>Microsoft.AspNetCore.Builder.Extensions</c> namespace
/// (CA1724).
/// </remarks>
public static class ServiceDefaultsExtensions
{
    private const string HealthEndpointPath = "/health";
    private const string AlivenessEndpointPath = "/alive";

    /// <summary>
    /// Registers the shared defaults on <paramref name="builder"/>: OpenTelemetry, the default
    /// health checks, service discovery, and resilience plus service discovery on every
    /// <see cref="HttpClient"/> the application resolves.
    /// </summary>
    /// <typeparam name="TBuilder">The host application builder being configured.</typeparam>
    /// <param name="builder">The builder to register the defaults on.</param>
    /// <returns><paramref name="builder"/>, so calls can be chained.</returns>
    public static TBuilder AddServiceDefaults<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.ConfigureOpenTelemetry();

        builder.AddDefaultHealthChecks();

        builder.Services.AddServiceDiscovery();

        builder.Services.ConfigureHttpClientDefaults(http =>
        {
            // Turn on resilience by default
            http.AddStandardResilienceHandler();

            // Turn on service discovery by default
            http.AddServiceDiscovery();
        });

        return builder;
    }

    /// <summary>
    /// Configures OpenTelemetry logging, metrics and tracing, and wires up the OTLP exporter when
    /// an endpoint is configured.
    /// </summary>
    /// <typeparam name="TBuilder">The host application builder being configured.</typeparam>
    /// <param name="builder">The builder to configure telemetry on.</param>
    /// <returns><paramref name="builder"/>, so calls can be chained.</returns>
    public static TBuilder ConfigureOpenTelemetry<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
        });

        builder.Services.AddOpenTelemetry()
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation())
            .WithTracing(tracing => tracing
                .AddSource(builder.Environment.ApplicationName)
                .AddAspNetCoreInstrumentation(options => options.Filter = static context => !IsHealthCheckRequest(context))
                .AddHttpClientInstrumentation());

        builder.AddOpenTelemetryExporters();

        return builder;
    }

    /// <summary>
    /// Adds the liveness check that reports the application is responding at all.
    /// </summary>
    /// <typeparam name="TBuilder">The host application builder being configured.</typeparam>
    /// <param name="builder">The builder to register the health checks on.</param>
    /// <returns><paramref name="builder"/>, so calls can be chained.</returns>
    public static TBuilder AddDefaultHealthChecks<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddHealthChecks()
            // Add a default liveness check to ensure app is responsive
            .AddCheck("self", static () => HealthCheckResult.Healthy(), ["live"]);

        return builder;
    }

    /// <summary>
    /// Maps the health check endpoints, in the Development environment only.
    /// </summary>
    /// <param name="app">The application to map the endpoints on.</param>
    /// <returns><paramref name="app"/>, so calls can be chained.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="app"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// Exposing these endpoints outside Development has security implications; see
    /// <see href="https://aka.ms/aspire/healthchecks"/> before widening the environment check.
    /// </remarks>
    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        if (app.Environment.IsDevelopment())
        {
            // All health checks must pass for app to be considered ready to accept traffic after starting
            app.MapHealthChecks(HealthEndpointPath);

            // Only health checks tagged with the "live" tag must pass for app to be considered alive
            app.MapHealthChecks(AlivenessEndpointPath, new HealthCheckOptions
            {
                Predicate = static r => r.Tags.Contains("live"),
            });
        }

        return app;
    }

    private static void AddOpenTelemetryExporters<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        var useOtlpExporter = !string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]);

        if (useOtlpExporter)
        {
            builder.Services.AddOpenTelemetry().UseOtlpExporter();
        }
    }

    // Health check traffic is a machine polling on a timer, so tracing it would bury the requests
    // that came from a caller. StringComparison is passed explicitly (CA1307/MA0074) and is the
    // comparison PathString.StartsWithSegments already uses by default, so the behaviour is
    // unchanged.
    private static bool IsHealthCheckRequest(HttpContext context)
    {
        return context.Request.Path.StartsWithSegments(HealthEndpointPath, StringComparison.OrdinalIgnoreCase)
            || context.Request.Path.StartsWithSegments(AlivenessEndpointPath, StringComparison.OrdinalIgnoreCase);
    }
}
