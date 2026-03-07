using Google.Apis.Sheets.v4;
using Google.Apis.Sheets.v4.Data;
using GermanUniversityAgent.Models;

namespace GermanUniversityAgent.Services;

internal sealed class SheetsRepository
{
    private const int HeaderRow = 1;

    private readonly SheetsService _service;

    public SheetsRepository(SheetsService service)
    {
        _service = service;
    }

    public async Task<List<LinkRow>> ReadLinksAsync(string sheetId, string sheetName)
    {
        var range = $"{sheetName}!A{HeaderRow + 1}:A";
        var request = _service.Spreadsheets.Values.Get(sheetId, range);
        var response = await request.ExecuteAsync();

        var links = new List<LinkRow>();
        if (response.Values is null)
            return links;

        for (var i = 0; i < response.Values.Count; i++)
        {
            var rowIndex = HeaderRow + 1 + i;
            var url = response.Values[i].Count > 0 ? response.Values[i][0]?.ToString() ?? string.Empty : string.Empty;
            links.Add(new LinkRow(rowIndex, url.Trim()));
        }

        return links;
    }

    public async Task WriteResultAsync(string sheetId, string sheetName, int row, DAADProgramInfo info)
    {
        var range = $"{sheetName}!C{row}:G{row}";
        var values = new List<IList<object>>
        {
            new List<object>
            {
                Math.Max(0, info.SemesterCount),
                info.TuitionFeeEur ?? string.Empty,
                info.AdmissionSemester ?? string.Empty,
                info.City ?? string.Empty,
                info.University ?? string.Empty
            }
        };

        var body = new ValueRange { Values = values };
        var request = _service.Spreadsheets.Values.Update(body, sheetId, range);
        request.ValueInputOption = SpreadsheetsResource.ValuesResource.UpdateRequest.ValueInputOptionEnum.RAW;
        await request.ExecuteAsync();
    }

    public async Task WriteHSKResultAsync(string sheetId, string sheetName, int row, HSKProgramInfo info)
    {
        // B=ProgramName, C=SemesterCount, D=TuitionFeeEur, E=AdmissionSemester, F=City, G=University
        var range = $"{sheetName}!B{row}:G{row}";
        var values = new List<IList<object>>
        {
            new List<object>
            {
                info.ProgramName ?? string.Empty,
                Math.Max(0, info.SemesterCount),
                info.TuitionFeeEur ?? string.Empty,
                info.AdmissionSemester ?? string.Empty,
                info.City ?? string.Empty,
                info.University ?? string.Empty
            }
        };

        var body = new ValueRange { Values = values };
        var request = _service.Spreadsheets.Values.Update(body, sheetId, range);
        request.ValueInputOption = SpreadsheetsResource.ValuesResource.UpdateRequest.ValueInputOptionEnum.RAW;
        await request.ExecuteAsync();
    }
}
