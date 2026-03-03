using System.Text.Json;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.Sheets.v4;

namespace GermanUniversityAgent.Services;

internal static class GoogleSheetsServiceFactory
{
    public static SheetsService BuildService(string credentialsPath)
    {
        var resolvedPath = ResolveCredentialsPath(credentialsPath);
        if (!File.Exists(resolvedPath))
        {
            throw new InvalidOperationException($"Google credentials file not found: {resolvedPath}");
        }

        if (!IsServiceAccountJson(resolvedPath))
        {
            throw new InvalidOperationException(
                "GoogleSheets credentials must be a Service Account JSON file. " +
                $"Provided file is not a service account: {resolvedPath}");
        }

        var credential = GoogleCredential.FromFile(resolvedPath)
            .CreateScoped(SheetsService.Scope.Spreadsheets);

        return new SheetsService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "GermanUniversityAgent"
        });
    }

    private static string ResolveCredentialsPath(string configuredPath)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            var baseDir = Directory.GetCurrentDirectory();
            return Path.IsPathRooted(configuredPath)
                ? configuredPath
                : Path.Combine(baseDir, configuredPath);
        }

        var baseDirectory = Directory.GetCurrentDirectory();
        var secretsDir = Path.Combine(baseDirectory, "secrets");
        if (!Directory.Exists(secretsDir))
        {
            throw new InvalidOperationException(
                "GoogleSheets:ApplicationCredentials is empty and secrets directory was not found. " +
                "Create a secrets folder at the project root and place a single *.json credential file there.");
        }

        var matches = Directory.GetFiles(secretsDir, "*.json");
        if (matches.Length == 0)
        {
            throw new InvalidOperationException("No *.json credentials found in secrets folder.");
        }

        if (matches.Length > 1)
        {
            throw new InvalidOperationException(
                "Multiple *.json files found in secrets folder. Specify GoogleSheets:ApplicationCredentials explicitly.");
        }

        return matches[0];
    }

    private static bool IsServiceAccountJson(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var doc = JsonDocument.Parse(stream);
            if (doc.RootElement.TryGetProperty("type", out var typeElement))
            {
                return string.Equals(typeElement.GetString(), "service_account", StringComparison.OrdinalIgnoreCase);
            }
        }
        catch
        {
            // Ignore and treat as non-service account.
        }

        return false;
    }
}
