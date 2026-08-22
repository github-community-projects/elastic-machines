using System.Collections;
using System.Reflection;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using ElasticMachines.Utilities;
using NCrontab;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using YamlDotNet.Serialization.Utilities;

namespace ElasticMachines.Engine.Models;

public record Configuration
{
    public required string Name
    {
        get;
        init
        {
            if (!Regex.IsMatch(value, "^[a-zA-Z0-9-_]+$"))
                throw new Exception($"Configuration's 'name' property can contain only letters, numbers, -, and _");

            field = value;
        }
    }

    public required Uri Url { get; init; }
    public string Group { get; init; } = "Default";
    public object? With { get; init; }
    public List<Scale>? Scale { get; init; }

    public T WithAs<T>() => (T)WithAs(typeof(T));

    public object WithAs(Type type) => Convert(With, type) ?? throw new Exception($"Could not convert to type '{type}'");

    public Scale GetCurrentScale()
    {
        static Scale aggregate(Scale scale, Scale next)
        {
            var timezone = scale.Timezone != null ? TimeZoneInfo.FindSystemTimeZoneById(scale.Timezone) : TimeZoneInfo.Utc;
            var now = TimeZoneInfo.ConvertTime(DateTime.Now, timezone);

            var cronExpression = scale.CronExpression ?? "* * * * *"; // Default to any time
            var parts = cronExpression.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            var requiredPartsLength = 5;
            if (parts.Length != requiredPartsLength)
                throw new Exception($"Cron expression '{cronExpression}' does not have the correct number of parts ({requiredPartsLength})");

            var match = CrontabField.Minutes(parts[0]).Contains(now.Minute)
                && CrontabField.Hours(parts[1]).Contains(now.Hour)
                && CrontabField.Days(parts[2]).Contains(now.Day)
                && CrontabField.Months(parts[3]).Contains(now.Month)
                && CrontabField.DaysOfWeek(parts[4]).Contains((int)now.DayOfWeek);

            return !match ? scale : new()
            {
                CronExpression = scale.CronExpression ?? next.CronExpression,
                Timezone = scale.Timezone ?? next.Timezone,
                MinimumIdleMachines = scale.MinimumIdleMachines ?? next.MinimumIdleMachines,
                MaximumIdleMachines = scale.MaximumIdleMachines ?? next.MaximumIdleMachines,
                MinimumMachines = scale.MinimumMachines ?? next.MinimumMachines,
                MaximumMachines = scale.MaximumMachines ?? next.MaximumMachines,
            };
        }

        return Scale?.Aggregate(new Scale(), aggregate) ?? new();
    }

    public static async Task<Configuration> FromEnvironment(CancellationToken cancellationToken)
    {
        var configuration = Environment.GetEnvironmentVariable("CONFIGURATION");
        var configurationFile = Environment.GetEnvironmentVariable("CONFIGURATION_FILE");

        configuration ??= await File.ReadAllTextAsync(configurationFile ?? throw new("No configuration provided"), cancellationToken).ConfigureAwait(false);

        var deserializer = new DeserializerBuilder().WithNamingConvention(CamelCaseNamingConvention.Instance).Build();
        var deserialized = deserializer.Deserialize<Configuration>(configuration) ?? throw new("Unable to deserialize configuration");

        return deserialized;
    }

    private object? Convert(object? value, Type type)
    {
        if (type.IsAssignableFrom(value?.GetType()))
            return value;

        return value switch
        {
            Dictionary<object, object> d => Convert(d, type),
            List<object> l => Convert(l, type),
            _ => TypeConverter.ChangeType(value, type, CamelCaseNamingConvention.Instance, new DeserializerBuilder().BuildTypeInspector()),
        };
    }

    private object? Convert(Dictionary<object, object> value, Type type)
    {
        if (type.IsGenericType)
        {
            var genericType = type.GenericTypeArguments.Last();

            var dictionary = type.GetGenericTypeDefinition() switch
            {
                var t when t == typeof(Dictionary<,>) => Activator.CreateInstance(type) as IDictionary,
                var t when t == typeof(IDictionary<,>) => Activator.CreateInstance(typeof(Dictionary<,>)
                    .MakeGenericType(type.GenericTypeArguments.First(), genericType)) as IDictionary,
                _ => null
            };

            if (dictionary != null)
            {
                foreach (var (key, val) in value)
                {
                    dictionary.Add(key, Convert(val, genericType));
                }
            }

            return dictionary;
        }

        // Get the first constructor that the dictionary can satisfy
        var uncasedDictionary = value.ToDictionary(kv => (string)kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);
        var constructor = type.GetConstructors().FirstOrDefault(c => c.GetParameters().All(p => p.IsOptional || uncasedDictionary.ContainsKey(p.Name.GetValue())));

        if (constructor == null)
        {
            var configParameters = type.GetConstructors().Select(c => c.GetParameters()).ToList();
            var missingOptions = configParameters.SelectMany(p => p)
                .Where(p => !p.IsOptional && !uncasedDictionary.ContainsKey(p.Name.GetValue()))
                .Select(p => p.Name.GetValue())
                .Select(n => char.ToLower(n[0]) + n[1..])
                .ToList();
            throw new Exception($"The following required options are missing: {string.Join(", ", missingOptions)}");
        }

        var parameters = constructor.GetParameters();
        var parameterValues = new object[parameters.Length];

        for (var i = 0; i < parameters.Length; i++)
        {
            uncasedDictionary.Remove(parameters[i].Name.GetValue(), out var paramValue);
            parameterValues[i] = Convert(paramValue, parameters[i].ParameterType) ?? Type.Missing;
        }

        var instance = constructor.Invoke(parameterValues);

        // Set any remaining properties
        foreach (var (key, val) in uncasedDictionary)
        {
            var property = type.GetProperty(key, BindingFlags.IgnoreCase | BindingFlags.Instance | BindingFlags.Public);

            // Look for properties decorated with JsonPropertyNameAttribute, which may specify a different property name to use
            if (property == null)
            {
                property = type.GetProperties()
                    .FirstOrDefault(p => ((JsonPropertyNameAttribute?)Attribute.GetCustomAttribute(p, typeof(JsonPropertyNameAttribute)))?.Name == key);
            }

            property?.SetValue(instance, Convert(val, property.PropertyType));
        }

        return instance;
    }

    private object? Convert(List<object> value, Type type)
    {
        var genericType = type.GenericTypeArguments.Single();

        var list = type.GetGenericTypeDefinition() switch
        {
            var t when t == typeof(List<>) => Activator.CreateInstance(type) as IList,
            var t when t == typeof(IList<>) => Activator.CreateInstance(typeof(List<>)
                .MakeGenericType(genericType)) as IList,
            _ => null
        };

        if (list != null)
        {
            foreach (var item in value)
            {
                list.Add(Convert(item, genericType));
            }
        }

        return list;
    }
}
