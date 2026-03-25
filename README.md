[![AI Slop Inside](https://sladge.net/badge.svg)](https://sladge.net)

# GermanUniversityAgent

Small console app that scrapes DAAD program pages and writes structured study program data into Google Sheets.

**Core technologies**
- .NET 8
- C#
- Google Sheets API v4
- Google APIs Auth (Service Account)
- Semantic Kernel (OpenAI connector)
- AngleSharp (HTML parsing)

**Required environment**
- .NET SDK 8.x
- A Google Cloud service account JSON with access to the target Google Sheet
- A Google Sheet ID and sheet name
- A DeepSeek-compatible OpenAI API key and endpoint (configured in `appsettings.json` or env vars)

**Record model and constraints**

| Field | Type | Constraints |
| --- | --- | --- |
| `semester_count` | integer | Non-negative; parsed from “Standard period of study (amount)” or similar labels. |
| `tuition_fee_eur` | string (number) | Numeric string in EUR, no currency symbol; “0” if none. |
| `admission_semester` | string | One of `winter only`, `summer only`, `winter and summer`. |
| `city` | string | Free text; should be a city name. |
| `university` | string | Free text; official university name. |

**Configuration**
- `DeepSeek:ApiKey`
- `DeepSeek:ApiBase` (optional, default `https://api.deepseek.com/v1`)
- `DeepSeek:Model` (optional, default `deepseek-chat`)
- `GoogleSheets:ApplicationCredentials` (path to service account JSON)
- `GoogleSheets:SpreadsheetId`
- `GoogleSheets:SheetName`

