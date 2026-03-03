using System.Net.Http.Headers;
using System.Text;
using AngleSharp.Html.Dom;
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

            string html = await response.Content.ReadAsStringAsync();
            HtmlParser parser = new HtmlParser();
            IHtmlDocument document = await parser.ParseDocumentAsync(html);

            List<string> keyFacts = ExtractKeyFacts(document);

            string text = document.Body?.TextContent ?? string.Empty;
            text = NormalizeWhitespace(text);

            if (keyFacts.Count > 0)
            {
                var keyFactsBlock = string.Join('\n', keyFacts);
                text = $"{keyFactsBlock}\n{text}";
            }

            if (text.Length > MaxContentChars) 
                text = text[..MaxContentChars];

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
        var stringBuilder = new StringBuilder(input.Length);
        var wasWhite = false;

        foreach (char inputChar in input)
        {
            if (char.IsWhiteSpace(inputChar))
            {
                if (wasWhite) 
                    continue;
                
                stringBuilder.Append(' ');
                wasWhite = true;
            }
            else
            {
                stringBuilder.Append(inputChar);
                wasWhite = false;
            }
        }

        return stringBuilder.ToString().Trim();
    }

    private static List<string> ExtractKeyFacts(AngleSharp.Dom.IDocument document)
    {
        var lines = new List<string>();
        var items = document.QuerySelectorAll(".keyfact__item");
        foreach (var item in items)
        {
            string label = item.QuerySelector("dt")?.TextContent ?? string.Empty;
            string value = item.QuerySelector("dd")?.TextContent ?? string.Empty;

            label = NormalizeWhitespace(label);
            value = NormalizeWhitespace(value);

            if (!string.IsNullOrWhiteSpace(label) && !string.IsNullOrWhiteSpace(value)) 
                lines.Add($"{label}: {value}");
        }

        return lines;
    }
}
