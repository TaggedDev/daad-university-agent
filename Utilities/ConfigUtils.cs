using Microsoft.Extensions.Configuration;

namespace GermanUniversityAgent.Utilities;

internal static class ConfigUtils
{
    public static string GetConfigOrThrow(IConfiguration config, string key)
    {
        var value = config[key];
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Missing configuration value: {key}");
        }

        return value;
    }
}
