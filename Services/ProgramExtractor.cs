using System.Text.Json;
using System.Text.RegularExpressions;
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

            var info = JsonSerializer.Deserialize<ProgramInfo>(json, JsonOptions);
            if (info is null)
            {
                return null;
            }

            EnrichFromText(info, pageText);
            return info;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Extract failed: {ex.Message}");
            return null;
        }
    }

    private static void EnrichFromText(ProgramInfo info, string pageText)
    {
        if (info.SemesterCount <= 0)
        {
            var semesterMatch = Regex.Match(
                pageText,
                @"(standard period of study|duration)\s*[:\-]?\s*(\d+)\s*(semester|semesters)\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

            if (semesterMatch.Success && int.TryParse(semesterMatch.Groups[2].Value, out var semesters))
            {
                info.SemesterCount = Math.Max(0, semesters);
            }
        }

        if (string.IsNullOrWhiteSpace(info.TuitionFeeEur))
        {
            if (Regex.IsMatch(pageText, @"no tuition fees?\b|tuition fees?\s*:\s*none\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            {
                info.TuitionFeeEur = "0";
            }
            else
            {
                var feeMatch = Regex.Match(
                    pageText,
                    @"tuition fees?\s*[:\-]?\s*€?\s*([0-9][0-9\.,\s]*)",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

                if (feeMatch.Success)
                {
                    var feeRaw = feeMatch.Groups[1].Value;
                    info.TuitionFeeEur = NormalizeAmount(feeRaw);
                }
            }
        }

        if (string.IsNullOrWhiteSpace(info.AdmissionSemester))
        {
            var hasWinter = Regex.IsMatch(pageText, @"\bwinter semester\b|\bwinter term\b|\bWS\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            var hasSummer = Regex.IsMatch(pageText, @"\bsummer semester\b|\bsummer term\b|\bSS\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

            if (hasWinter && hasSummer)
            {
                info.AdmissionSemester = "winter and summer";
            }
            else if (hasWinter)
            {
                info.AdmissionSemester = "winter only";
            }
            else if (hasSummer)
            {
                info.AdmissionSemester = "summer only";
            }
        }
    }

    private static string NormalizeAmount(string amount)
    {
        var cleaned = amount.Replace(" ", string.Empty);
        var hasComma = cleaned.Contains(',');
        var hasDot = cleaned.Contains('.');

        if (hasComma && hasDot)
        {
            cleaned = cleaned.Replace(".", string.Empty).Replace(',', '.');
        }
        else if (hasComma)
        {
            cleaned = cleaned.Replace(',', '.');
        }

        return cleaned.Trim();
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
