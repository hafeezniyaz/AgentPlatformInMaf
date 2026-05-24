using System.ComponentModel;
using System.Data;
using System.Globalization;
using AgentPlatform.Core.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace AgentPlatform.Infrastructure.Sqlite.Tools;

public interface IClockService
{
    DateTimeOffset UtcNow { get; }
}

public sealed class SystemClockService : IClockService
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

public interface ICalculatorService
{
    string Evaluate(string expression);
}

public sealed class DataTableCalculatorService : ICalculatorService
{
    public string Evaluate(string expression)
    {
        if (expression.Any(character => !"0123456789.+-*/() ".Contains(character)))
        {
            return "Only simple arithmetic expressions are allowed.";
        }

        try
        {
            var result = new DataTable().Compute(expression, null);
            return Convert.ToString(result, CultureInfo.InvariantCulture) ?? "";
        }
        catch (Exception ex)
        {
            return $"Could not evaluate expression: {ex.Message}";
        }
    }
}

public interface IWeatherService
{
    string GetWeather(string location);
}

public sealed class DemoWeatherService : IWeatherService
{
    public string GetWeather(string location)
        => $"The weather in {location} is pleasant with light clouds. This is demo data.";
}

public sealed class ClockToolDefinition(IClockService clockService) : IAgentToolDefinition
{
    public string Id => "clock";

    public string Name => "Clock";

    public string Description => "Returns the current UTC time.";

    public string Category => "utility";

    public IReadOnlyDictionary<string, string>? Metadata => null;

    public AITool CreateTool(IServiceProvider services)
        => AIFunctionFactory.Create(
            GetCurrentUtcTime,
            new AIFunctionFactoryOptions
            {
                Name = Id,
                Description = "Get the current UTC timestamp in ISO-8601 format."
            });

    [Description("Get the current UTC timestamp in ISO-8601 format.")]
    private string GetCurrentUtcTime()
        => clockService.UtcNow.ToString("O", CultureInfo.InvariantCulture);
}

public sealed class CalculatorToolDefinition(ICalculatorService calculatorService) : IAgentToolDefinition
{
    public string Id => "calculator";

    public string Name => "Calculator";

    public string Description => "Evaluates simple arithmetic expressions.";

    public string Category => "utility";

    public IReadOnlyDictionary<string, string>? Metadata => null;

    public AITool CreateTool(IServiceProvider services)
        => AIFunctionFactory.Create(
            Calculate,
            new AIFunctionFactoryOptions
            {
                Name = Id,
                Description = "Evaluate a simple arithmetic expression containing numbers, parentheses, and +, -, *, / operators."
            });

    [Description("Evaluate a simple arithmetic expression containing numbers, parentheses, and +, -, *, / operators.")]
    private string Calculate([Description("Arithmetic expression to evaluate.")] string expression)
        => calculatorService.Evaluate(expression);
}

public sealed class WeatherToolDefinition : IAgentToolDefinition
{
    public string Id => "weather";

    public string Name => "Weather";

    public string Description => "Returns a stub weather report for testing tool selection.";

    public string Category => "demo";

    public IReadOnlyDictionary<string, string>? Metadata => null;

    public AITool CreateTool(IServiceProvider services)
        => AIFunctionFactory.Create(
            GetWeather,
            new AIFunctionFactoryOptions
            {
                Name = Id,
                Description = "Get a demo weather report for a city. This is a local stub for development."
            });

    [Description("Get a demo weather report for a city. This is a local stub for development.")]
    private static string GetWeather(
        [Description("City or location name.")] string location,
        IServiceProvider services)
        => services.GetRequiredService<IWeatherService>().GetWeather(location);
}
