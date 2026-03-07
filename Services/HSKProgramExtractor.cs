using System.Text.Json;
using System.Text.RegularExpressions;
using GermanUniversityAgent.Models;
using Microsoft.SemanticKernel;

namespace GermanUniversityAgent.Services;

internal sealed class HSKProgramExtractor
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static KernelFunction CreateExtractionFunction(Kernel kernel) => kernel.CreateFunctionFromPrompt(
        """
        You are extracting structured data from a German university programme page (hochschulkompass.de).
        Use ONLY the provided page text. If a value is missing, return an empty string.
        Return ONLY a JSON object, no markdown, no backticks, no extra text.

        Output strict JSON with exactly these fields:
        - program_name: string (name of the degree programme)
        - semester_count: non-negative integer
        - tuition_fee_eur: price as a number string (no currency symbol). If none or "None", use "0".
        - admission_semester: one of ["winter only","summer only","winter and summer"]
        - city: string
        - university: string

        Hints:
        - program_name: the degree programme title, usually the most prominent heading.
        - semester_count: look for "Duration", "Standard period of study", or "{N} semester(s)".
        - tuition_fee_eur: look for "Tuition fees". "None" or "No tuition fees" → "0".
        - admission_semester: look for "Begin of studies". Winter semester/term → "winter only",
          summer semester/term → "summer only", both → "winter and summer".
        - city: look for "Location" or study location.
        - university: look for "University" or "Institution".

        Page text:
        {{ $pageText }}
        """
    );

    public async Task<HSKProgramInfo?> ExtractAsync(Kernel kernel, KernelFunction function, string pageText)
    {
        try
        {
            var result = await kernel.InvokeAsync(function, new KernelArguments
            {
                ["pageText"] = pageText
            });

            var json = NormalizeJson(result.GetValue<string>());
            if (string.IsNullOrWhiteSpace(json))
                return null;

            var info = JsonSerializer.Deserialize<HSKProgramInfo>(json, JsonOptions);
            if (info is null)
                return null;

            EnrichFromText(info, pageText);
            return info;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Extract failed: {ex.Message}");
            return null;
        }
    }

    private static void EnrichFromText(HSKProgramInfo info, string pageText)
    {
        if (info.SemesterCount <= 0)
        {
            var semesterMatch = Regex.Match(
                pageText,
                @"duration\s*[:\-]?\s*(\d+)\s*(semester|semesters)\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

            if (!semesterMatch.Success)
            {
                semesterMatch = Regex.Match(
                    pageText,
                    @"standard period of study\s*[:\-]?\s*.*?(\d+)\s*(semester|semesters)\b",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline);
            }

            if (!semesterMatch.Success)
            {
                semesterMatch = Regex.Match(
                    pageText,
                    @"(\d+)\s*(semester|semesters)\b",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            }

            if (semesterMatch.Success && int.TryParse(semesterMatch.Groups[1].Value, out var semesters))
                info.SemesterCount = Math.Max(0, semesters);
        }

        if (string.IsNullOrWhiteSpace(info.TuitionFeeEur))
        {
            if (Regex.IsMatch(pageText, @"tuition fees?\s*[:\-]?\s*none\b|no tuition fees?\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
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
                    info.TuitionFeeEur = NormalizeAmount(feeMatch.Groups[1].Value);
            }
        }

        if (string.IsNullOrWhiteSpace(info.AdmissionSemester))
        {
            var hasWinter = Regex.IsMatch(pageText, @"\bwinter semester\b|\bwinter term\b|\bwinter trimester\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            var hasSummer = Regex.IsMatch(pageText, @"\bsummer semester\b|\bsummer term\b|\bsummer trimester\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

            info.AdmissionSemester = (hasWinter, hasSummer) switch
            {
                (true, true) => "winter and summer",
                (true, false) => "winter only",
                (false, true) => "summer only",
                _ => string.Empty
            };
        }
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
