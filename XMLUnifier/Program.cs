using System.Net;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace XMLUnifier;

internal static class Program
{
    private static readonly HashSet<string> TargetFolders = new(StringComparer.Ordinal)
    {
        "HSK (temp)",
        "HSK (temp 2)"
    };

    private static readonly Regex FolderRegex =
        new(@"<DT><H3[^>]*>(?<name>.*?)</H3>", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex LinkRegex =
        new(@"<A[^>]*\bHREF=""(?<href>[^""]+)""", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private const string DefaultInputFile = "bookmarks_07.03.2026.html";
    private const string DefaultOutputFile = "hsk-links.json";

    private static void Main(string[] args)
    {
        var inputPath = args.Length > 0 ? args[0] : DefaultInputFile;
        var outputPath = args.Length > 1 ? args[1] : DefaultOutputFile;

        if (!File.Exists(inputPath))
        {
            Console.Error.WriteLine($"Input file not found: {inputPath}");
            return;
        }

        var links = ExtractLinksFromTargetFolders(inputPath);
        var json = JsonSerializer.Serialize(links, new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });
        File.WriteAllText(outputPath, json);

        Console.WriteLine($"Saved {links.Count} unique links to: {outputPath}");
    }

    private static List<string> ExtractLinksFromTargetFolders(string htmlFilePath)
    {
        var folderStack = new Stack<string>();
        var uniqueLinks = new HashSet<string>(StringComparer.Ordinal);
        var orderedLinks = new List<string>();
        string? pendingFolderName = null;

        foreach (var line in File.ReadLines(htmlFilePath))
        {
            var folderMatch = FolderRegex.Match(line);
            if (folderMatch.Success)
            {
                pendingFolderName = DecodeHtml(folderMatch.Groups["name"].Value);
            }

            if (line.Contains("<DL><p>", StringComparison.OrdinalIgnoreCase) && pendingFolderName is not null)
            {
                folderStack.Push(pendingFolderName);
                pendingFolderName = null;
            }

            if (line.Contains("</DL><p>", StringComparison.OrdinalIgnoreCase) && folderStack.Count > 0)
            {
                folderStack.Pop();
            }

            var linkMatch = LinkRegex.Match(line);
            if (!linkMatch.Success)
            {
                continue;
            }

            if (!IsInsideTargetFolder(folderStack))
            {
                continue;
            }

            var href = DecodeHtml(linkMatch.Groups["href"].Value);
            if (uniqueLinks.Add(href))
            {
                orderedLinks.Add(href);
            }
        }

        return orderedLinks;
    }

    private static bool IsInsideTargetFolder(IEnumerable<string> folderStack)
    {
        foreach (var folderName in folderStack)
        {
            if (TargetFolders.Contains(folderName))
            {
                return true;
            }
        }

        return false;
    }

    private static string DecodeHtml(string value) => WebUtility.HtmlDecode(value).Trim();
}
