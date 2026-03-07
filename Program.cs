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
    private const int ProgressBarWidth = 30;
    
    [Experimental("SKEXP0010")]
    private static async Task Main()
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

        KernelFunction extractor = HSKProgramExtractor.CreateExtractionFunction(kernel);

        var credentialsPath = config["GoogleSheets:ApplicationCredentials"] ?? string.Empty;
        var sheetsService = GoogleSheetsServiceFactory.BuildService(credentialsPath);
        var sheetsRepository = new SheetsRepository(sheetsService);
        var pageTextFetcher = new HSKPageTextFetcher();
        var programExtractor = new HSKProgramExtractor();

        List<LinkRow> links = await sheetsRepository.ReadLinksAsync(sheetId, sheetName);
        if (links.Count == 0)
        {
            Console.WriteLine("No links found in column A.");
            return;
        }

        var validLinks = links.Where(link => !string.IsNullOrWhiteSpace(link.Url)).ToList();
        if (validLinks.Count == 0)
        {
            Console.WriteLine("No valid links found in column A.");
            return;
        }

        foreach (var link in validLinks)
        {
            RenderProgress(validLinks, link.Row);

            var pageText = await pageTextFetcher.FetchPageTextAsync(link.Url);
            if (string.IsNullOrWhiteSpace(pageText))
                continue;

            HSKProgramInfo? result = await programExtractor.ExtractAsync(kernel, extractor, pageText);
            if (result is null)
                continue;

            await sheetsRepository.WriteHSKResultAsync(sheetId, sheetName, link.Row, result);
        }

        RenderProgressDone(validLinks.Count);
    }

    private static void RenderProgress(IReadOnlyList<LinkRow> validLinks, int currentRow)
    {
        var currentIndex = GetIndexByRow(validLinks, currentRow);
        var progress = (double)currentIndex / validLinks.Count;
        var filled = (int)(progress * ProgressBarWidth);
        var bar = new string('#', filled).PadRight(ProgressBarWidth, '-');
        Console.Write($"\r[{bar}] {currentIndex}/{validLinks.Count} (row {currentRow})");
    }

    private static void RenderProgressDone(int total)
    {
        var bar = new string('#', ProgressBarWidth);
        Console.WriteLine($"\r[{bar}] {total}/{total} (done)");
    }

    private static int GetIndexByRow(IReadOnlyList<LinkRow> links, int row)
    {
        for (var i = 0; i < links.Count; i++)
        {
            if (links[i].Row == row)
            {
                return i + 1;
            }
        }

        return 1;
    }
}
