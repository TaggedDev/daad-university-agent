using System.Net.Http.Headers;
using System.Text;
using AngleSharp.Html.Parser;

namespace GermanUniversityAgent.Services;

internal sealed class PageTextFetcher
{
    private const int MaxContentChars = 12000;

    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    public async Task<string> FetchPageTextAsync(string url)
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

            var keyFacts = ExtractKeyFacts(document);

            var text = document.Body?.TextContent ?? string.Empty;
            text = NormalizeWhitespace(text);

            if (keyFacts.Count > 0)
            {
                var keyFactsBlock = string.Join('\n', keyFacts);
                text = $"{keyFactsBlock}\n{text}";
            }

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

    private static List<string> ExtractKeyFacts(AngleSharp.Dom.IDocument document)
    {
        var lines = new List<string>();
        var items = document.QuerySelectorAll(".keyfact__item");
        foreach (var item in items)
        {
            var label = item.QuerySelector("dt")?.TextContent ?? string.Empty;
            var value = item.QuerySelector("dd")?.TextContent ?? string.Empty;

            label = NormalizeWhitespace(label);
            value = NormalizeWhitespace(value);

            if (!string.IsNullOrWhiteSpace(label) && !string.IsNullOrWhiteSpace(value))
            {
                lines.Add($"{label}: {value}");
            }
        }

        return lines;
    }
}
