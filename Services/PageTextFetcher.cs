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
}
