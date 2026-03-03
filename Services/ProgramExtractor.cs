using System.Text.Json;
using GermanUniversityAgent.Models;
using Microsoft.SemanticKernel;

namespace GermanUniversityAgent.Services;

internal sealed class ProgramExtractor
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<ProgramInfo?> ExtractAsync(Kernel kernel, KernelFunction function, string pageText)
    {
        try
        {
            var result = await kernel.InvokeAsync(function, new KernelArguments
            {
                ["pageText"] = pageText
            });

            var json = NormalizeJson(result.GetValue<string>());
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

    private static string NormalizeJson(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        var text = raw.Trim();
        if (text.StartsWith("```", StringComparison.Ordinal))
        {
            var firstNewline = text.IndexOf('\n');
            if (firstNewline >= 0)
            {
                text = text[(firstNewline + 1)..];
            }

            var fenceIndex = text.LastIndexOf("```", StringComparison.Ordinal);
            if (fenceIndex >= 0)
            {
                text = text[..fenceIndex];
            }

            text = text.Trim();
        }

        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start >= 0 && end > start)
        {
            text = text.Substring(start, end - start + 1);
        }

        return text.Trim();
    }
}
