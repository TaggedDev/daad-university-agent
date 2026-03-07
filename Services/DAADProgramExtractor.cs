using System.Text.Json;
using System.Text.RegularExpressions;
using GermanUniversityAgent.Models;
using Microsoft.SemanticKernel;

namespace GermanUniversityAgent.Services;

internal sealed class DAADProgramExtractor
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<DAADProgramInfo?> ExtractAsync(Kernel kernel, KernelFunction function, string pageText)
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

            var info = JsonSerializer.Deserialize<DAADProgramInfo>(json, JsonOptions);
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

    private static void EnrichFromText(DAADProgramInfo info, string pageText)
    {
        if (info.SemesterCount <= 0)
        {
            var semesterMatch = Regex.Match(
                pageText,
                @"standard period of study\s*\(amount\)\s*[:\-]?\s*.*?(\d+)\s*(semester|semesters)\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline);

            if (!semesterMatch.Success)
            {
                semesterMatch = Regex.Match(
                    pageText,
                    @"(standard period of study)\s*[:\-]?\s*.*?(\d+)\s*(semester|semesters)\b",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline);
            }

            if (!semesterMatch.Success)
            {
                semesterMatch = Regex.Match(
                    pageText,
                    @"(duration|length of study)\s*[:\-]?\s*.*?(\d+)\s*(semester|semesters)\b",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline);
            }

            if (!semesterMatch.Success)
            {
                semesterMatch = Regex.Match(
                    pageText,
                    @"(\d+)\s*(semester|semesters)\b\s*(standard period of study|duration|length of study)",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            }

            if (semesterMatch.Success)
            {
                var numberGroup = semesterMatch.Groups[1].Success ? semesterMatch.Groups[1] : semesterMatch.Groups[2];
                if (int.TryParse(numberGroup.Value, out var semesters))
                {
                    info.SemesterCount = Math.Max(0, semesters);
                }
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
            var admissionMatch = Regex.Match(
                pageText,
                @"admission semester\s*[:\-]?\s*([A-Za-z\s]+)",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

            if (admissionMatch.Success)
            {
                var admissionText = admissionMatch.Groups[1].Value;
                info.AdmissionSemester = NormalizeAdmissionSemester(admissionText);
            }
            else
            {
                var hasWinter = Regex.IsMatch(pageText, @"\bwinter semester\b|\bwinter term\b|\bwinter trimester\b|\bWS\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                var hasSummer = Regex.IsMatch(pageText, @"\bsummer semester\b|\bsummer term\b|\bsummer trimester\b|\bSS\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

                switch (hasWinter)
                {
                    case true when hasSummer:
                        info.AdmissionSemester = "winter and summer";
                        break;
                    case true:
                        info.AdmissionSemester = "winter only";
                        break;
                    default:
                    {
                        if (hasSummer) info.AdmissionSemester = "summer only";
                        break;
                    }
                }
            }
        }
    }

    private static string NormalizeAdmissionSemester(string admissionText)
    {
        var text = admissionText.ToLowerInvariant();

        var hasWinter = text.Contains("winter");
        var hasSummer = text.Contains("summer");

        return hasWinter switch
        {
            true when hasSummer => "winter and summer",
            true => "winter only",
            _ => hasSummer ? "summer only" : string.Empty
        };
    }

    private static string NormalizeAmount(string amount)
    {
        string cleaned = amount.Replace(" ", string.Empty);
        bool hasComma = cleaned.Contains(',');
        bool hasDot = cleaned.Contains('.');

        cleaned = hasComma switch
        {
            true when hasDot => cleaned.Replace(".", string.Empty).Replace(',', '.'),
            true => cleaned.Replace(',', '.'),
            _ => cleaned
        };

        return cleaned.Trim();
    }

    private static string NormalizeJson(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;

        var text = raw.Trim();
        if (text.StartsWith("```", StringComparison.Ordinal))
        {
            var firstNewline = text.IndexOf('\n');
            if (firstNewline >= 0) 
                text = text[(firstNewline + 1)..];

            var fenceIndex = text.LastIndexOf("```", StringComparison.Ordinal);
            if (fenceIndex >= 0)
                text = text[..fenceIndex];

            text = text.Trim();
        }

        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start >= 0 && end > start) 
            text = text.Substring(start, end - start + 1);

        return text.Trim();
    }
}
