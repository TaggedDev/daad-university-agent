using System.Diagnostics.CodeAnalysis;
using GermanUniversityAgent.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.SemanticKernel;
using GermanUniversityAgent.Services;
using GermanUniversityAgent.Utilities;

namespace GermanUniversityAgent;

internal sealed class Program
{
    private const string DefaultModel = "deepseek-chat";
    private const string DefaultSheetName = "Sheet1";
    [Experimental("SKEXP0010")]
    private static async Task Main(string[] args)
    {
        var config = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
            .AddEnvironmentVariables()
            .Build();

        var deepSeekApiKey = ConfigUtils.GetConfigOrThrow(config, "DeepSeek:ApiKey");
        var deepSeekBase = config["DeepSeek:ApiBase"] ?? "https://api.deepseek.com/v1";
        var deepSeekModel = config["DeepSeek:Model"] ?? DefaultModel;

        var sheetId = ConfigUtils.GetConfigOrThrow(config, "GoogleSheets:SpreadsheetId");
        var sheetName = config["GoogleSheets:SheetName"] ?? DefaultSheetName;

        var kernel = Kernel.CreateBuilder()
            .AddOpenAIChatCompletion(modelId: deepSeekModel, apiKey: deepSeekApiKey, endpoint: new Uri(deepSeekBase))
            .Build();

        var extractor = kernel.CreateFunctionFromPrompt(
            """
            You are extracting structured data from a university program page.
            Use ONLY the provided page text. If a value is missing, return an empty string.
            Return ONLY a JSON object, no markdown, no backticks, no extra text.

            Output strict JSON with exactly these fields:
            - semester_count: non-negative integer
            - tuition_fee_eur: price as a number string (no currency symbol, no semester fee). If none, "0".
            - admission_semester: one of ["winter only","summer only","winter and summer"]
            - city: string
            - university: string

            Hints:
            - semester_count: look for "Standard period of study (amount)", "Standard period of study", "Duration",
              or "{N} semester(s)".
            - tuition_fee_eur: look for "Tuition fees" or "Tuition fee". If "no tuition fees", use "0".
            - admission_semester: look for "Admission semester" or "Admission only in the (season) trimester".
              Map winter trimester/semester/term -> "winter only", summer trimester/semester/term -> "summer only".
              If both winter and summer are listed, use "winter and summer".
            - city: look for location.

            Page text:
            {{ $pageText }}
            """
        );

        var credentialsPath = config["GoogleSheets:ApplicationCredentials"] ?? string.Empty;
        var sheetsService = GoogleSheetsServiceFactory.BuildService(credentialsPath);
        var sheetsRepository = new SheetsRepository(sheetsService);
        var pageTextFetcher = new PageTextFetcher();
        var programExtractor = new ProgramExtractor();

        var links = await sheetsRepository.ReadLinksAsync(sheetId, sheetName);
        if (links.Count == 0)
        {
            Console.WriteLine("No links found in column A.");
            return;
        }

        Console.WriteLine($"Found {links.Count} links. Processing...");

        foreach (LinkRow link in links)
        {
            if (string.IsNullOrWhiteSpace(link.Url))
                continue;

            Console.WriteLine($"Row {link.Row}: {link.Url}");

            var pageText = await pageTextFetcher.FetchPageTextAsync(link.Url);
            if (string.IsNullOrWhiteSpace(pageText))
            {
                Console.WriteLine($"Row {link.Row}: empty page text.");
                continue;
            }

            ProgramInfo? result = await programExtractor.ExtractAsync(kernel, extractor, pageText);
            if (result is null)
            {
                Console.WriteLine($"Row {link.Row}: failed to parse model.");
                return;
            }

            await sheetsRepository.WriteResultAsync(sheetId, sheetName, link.Row, result);
            Console.WriteLine("Processed first row; stopping for setup verification.");
            return;
        }

        Console.WriteLine("Done.");
    }
}
