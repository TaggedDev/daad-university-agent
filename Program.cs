using System.Diagnostics.CodeAnalysis;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using AngleSharp.Html.Parser;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.Sheets.v4;
using Google.Apis.Sheets.v4.Data;
using Microsoft.Extensions.Configuration;
using Microsoft.SemanticKernel;

namespace GermanUniversityAgent;

internal sealed class Program
{
    private const string DefaultModel = "deepseek-chat";
    private const string DefaultSheetName = "Sheet1";
    private const int HeaderRow = 1;
    private const int MaxContentChars = 12000;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    [Experimental("SKEXP0010")]
    private static async Task Main(string[] args)
    {
        var config = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
            .AddEnvironmentVariables()
            .Build();

        var deepSeekApiKey = GetConfigOrThrow(config, "DeepSeek:ApiKey");
        var deepSeekBase = config["DeepSeek:ApiBase"] ?? "https://api.deepseek.com/v1";
        var deepSeekModel = config["DeepSeek:Model"] ?? DefaultModel;

        var sheetId = GetConfigOrThrow(config, "GoogleSheets:SpreadsheetId");
        var sheetName = config["GoogleSheets:SheetName"] ?? DefaultSheetName;

        var kernel = Kernel.CreateBuilder()
            .AddOpenAIChatCompletion(modelId: deepSeekModel, apiKey: deepSeekApiKey, endpoint: new Uri(deepSeekBase))
            .Build();

        var extractor = kernel.CreateFunctionFromPrompt(
            """
            You are extracting structured data from a DAAD program page.
            Use ONLY the provided page text. If a value is missing, return an empty string.

            Output strict JSON with exactly these fields:
            - semester_count: non-negative integer
            - tuition_fee_eur: price in EUR as a number string (no currency symbol, no semester fee). If none, "0".
            - admission_semester: one of ["winter only","summer only","winter and summer"]
            - city: string
            - university: string

            Page text:
            {{ $pageText }}
            """
        );

        var credentialsPath = config["GoogleSheets:ApplicationCredentials"] ?? string.Empty;
        var sheetsService = BuildSheetsService(credentialsPath);

        var links = await ReadLinksAsync(sheetsService, sheetId, sheetName);
        if (links.Count == 0)
        {
            Console.WriteLine("No links found in column A.");
            return;
        }

        Console.WriteLine($"Found {links.Count} links. Processing...");

        foreach (var link in links)
        {
            if (string.IsNullOrWhiteSpace(link.Url))
            {
                continue;
            }

            Console.WriteLine($"Row {link.Row}: {link.Url}");

            var pageText = await FetchPageTextAsync(link.Url);
            if (string.IsNullOrWhiteSpace(pageText))
            {
                Console.WriteLine($"Row {link.Row}: empty page text.");
                continue;
            }

            var result = await ExtractAsync(kernel, extractor, pageText);
            if (result is null)
            {
                Console.WriteLine($"Row {link.Row}: failed to parse model.");
                continue;
            }

            await WriteResultAsync(sheetsService, sheetId, sheetName, link.Row, result);
            Console.WriteLine("Processed first row; stopping for setup verification.");
            break;
        }

        Console.WriteLine("Done.");
    }

    private static SheetsService BuildSheetsService(string credentialsPath)
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

    private static async Task<List<LinkRow>> ReadLinksAsync(SheetsService service, string sheetId, string sheetName)
    {
        var range = $"{sheetName}!A{HeaderRow + 1}:A";
        var request = service.Spreadsheets.Values.Get(sheetId, range);
        var response = await request.ExecuteAsync();

        var links = new List<LinkRow>();
        if (response.Values is null)
        {
            return links;
        }

        for (var i = 0; i < response.Values.Count; i++)
        {
            var rowIndex = HeaderRow + 1 + i;
            var url = response.Values[i].Count > 0 ? response.Values[i][0]?.ToString() ?? string.Empty : string.Empty;
            links.Add(new LinkRow(rowIndex, url.Trim()));
        }

        return links;
    }

    private static async Task<string> FetchPageTextAsync(string url)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("GermanUniversityAgent", "1.0"));
            using var response = await Http.SendAsync(request);
            response.EnsureSuccessStatusCode();

            var html = await response.Content.ReadAsStringAsync();
            var parser = new HtmlParser();
            var document = await parser.ParseDocumentAsync(html);

            var text = document.Body?.TextContent ?? string.Empty;
            text = NormalizeWhitespace(text);

            if (text.Length > MaxContentChars)
            {
                text = text[..MaxContentChars];
            }

            return text;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Fetch failed: {ex.Message}");
            return string.Empty;
        }
    }

    private static async Task<ProgramInfo?> ExtractAsync(Kernel kernel, KernelFunction function, string pageText)
    {
        try
        {
            var result = await kernel.InvokeAsync(function, new KernelArguments
            {
                ["pageText"] = pageText
            });

            var json = result.GetValue<string>()?.Trim();
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            return JsonSerializer.Deserialize<ProgramInfo>(json, JsonOptions);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Extract failed: {ex.Message}");
            return null;
        }
    }

    private static async Task WriteResultAsync(SheetsService service, string sheetId, string sheetName, int row, ProgramInfo info)
    {
        var range = $"{sheetName}!C{row}:G{row}";
        var values = new List<IList<object>>
        {
            new List<object>
            {
                Math.Max(0, info.SemesterCount),
                info.TuitionFeeEur ?? string.Empty,
                info.AdmissionSemester ?? string.Empty,
                info.City ?? string.Empty,
                info.University ?? string.Empty
            }
        };

        var body = new ValueRange { Values = values };
        var request = service.Spreadsheets.Values.Update(body, sheetId, range);
        request.ValueInputOption = SpreadsheetsResource.ValuesResource.UpdateRequest.ValueInputOptionEnum.RAW;
        await request.ExecuteAsync();
    }

    private static string GetConfigOrThrow(IConfiguration config, string key)
    {
        var value = config[key];
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Missing configuration value: {key}");
        }

        return value;
    }

    private static string NormalizeWhitespace(string input)
    {
        var sb = new StringBuilder(input.Length);
        var wasWhite = false;

        foreach (var ch in input)
        {
            if (char.IsWhiteSpace(ch))
            {
                if (!wasWhite)
                {
                    sb.Append(' ');
                    wasWhite = true;
                }
            }
            else
            {
                sb.Append(ch);
                wasWhite = false;
            }
        }

        return sb.ToString().Trim();
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

    private sealed record LinkRow(int Row, string Url);

    private sealed class ProgramInfo
    {
        public int SemesterCount { get; set; }
        public string? TuitionFeeEur { get; set; }
        public string? AdmissionSemester { get; set; }
        public string? City { get; set; }
        public string? University { get; set; }
    }
}
