using System.Text;
using QuestPDF.Fluent;
using QuestPDF.Helpers;

namespace InpremClockingApp.Helpers;

// Shared by the Manage Staff / Manage Volunteers pages' export buttons (Copy/CSV/Excel/PDF/Print)
// - both pages have the same roster shape (Id/FirstName/LastName/Email/Gender/Phone/Zip), so the
// file-building logic lives here once instead of being duplicated per page.
public static class RosterExport
{
    public record Row(long Id, string FirstName, string LastName, string Email, string Gender, string Phone, string Zip);

    private static readonly string[] Headers = { "ID", "First Name", "Last Name", "Email", "Gender", "Phone", "Zip" };

    private static IEnumerable<string[]> ToCells(IEnumerable<Row> rows) =>
        rows.Select(r => new[] { r.Id.ToString(), r.FirstName, r.LastName, r.Email, r.Gender, r.Phone, r.Zip });

    public static byte[] BuildCsv(IEnumerable<Row> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(",", Headers.Select(EscapeCsv)));
        foreach (var cells in ToCells(rows))
        {
            sb.AppendLine(string.Join(",", cells.Select(EscapeCsv)));
        }
        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    private static string EscapeCsv(string value)
    {
        value ??= "";
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
        {
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }
        return value;
    }

    // Excel doesn't require a real .xlsx package to open a roster export - an HTML table served
    // with the application/vnd.ms-excel content type opens directly in Excel (it shows a one-time
    // "format differs from extension" warning, which is the standard trade-off for this approach
    // versus pulling in a dedicated OOXML library for what's otherwise a simple tabular export).
    public static byte[] BuildExcelHtml(string title, IEnumerable<Row> rows)
    {
        var sb = new StringBuilder();
        sb.Append("<html><head><meta charset=\"utf-8\"></head><body>");
        sb.Append($"<h3>{System.Net.WebUtility.HtmlEncode(title)}</h3>");
        sb.Append("<table border=\"1\"><tr>");
        foreach (var h in Headers) sb.Append($"<th>{System.Net.WebUtility.HtmlEncode(h)}</th>");
        sb.Append("</tr>");
        foreach (var cells in ToCells(rows))
        {
            sb.Append("<tr>");
            foreach (var c in cells) sb.Append($"<td>{System.Net.WebUtility.HtmlEncode(c ?? "")}</td>");
            sb.Append("</tr>");
        }
        sb.Append("</table></body></html>");
        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    public static byte[] BuildPdf(string tenantName, string title, IEnumerable<Row> rows)
    {
        var rowList = rows.ToList();

        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4.Landscape());
                page.Margin(40);

                page.Header()
                    .Column(header =>
                    {
                        header.Item().AlignCenter().Text(tenantName).FontSize(18).Bold();
                        header.Item().PaddingTop(5).LineHorizontal(1);
                    });

                page.Content()
                    .PaddingTop(20)
                    .Column(column =>
                    {
                        column.Spacing(10);

                        column.Item().Text(title).FontSize(15).Bold();
                        column.Item().Text($"Total: {rowList.Count}").FontSize(11);

                        column.Item()
                            .PaddingTop(10)
                            .Table(table =>
                            {
                                table.ColumnsDefinition(columns =>
                                {
                                    columns.RelativeColumn(1);
                                    columns.RelativeColumn(2);
                                    columns.RelativeColumn(2);
                                    columns.RelativeColumn(3);
                                    columns.RelativeColumn(1);
                                    columns.RelativeColumn(2);
                                    columns.RelativeColumn(1);
                                });

                                table.Header(header =>
                                {
                                    foreach (var h in Headers) header.Cell().Text(h).Bold();
                                });

                                foreach (var r in rowList)
                                {
                                    table.Cell().Text(r.Id.ToString());
                                    table.Cell().Text(r.FirstName ?? "");
                                    table.Cell().Text(r.LastName ?? "");
                                    table.Cell().Text(r.Email ?? "");
                                    table.Cell().Text(r.Gender ?? "");
                                    table.Cell().Text(r.Phone ?? "");
                                    table.Cell().Text(r.Zip ?? "");
                                }
                            });
                    });

                page.Footer()
                    .AlignCenter()
                    .Column(footer =>
                    {
                        footer.Item().Text("Please contact us for further information.").FontSize(9);
                        footer.Item().PaddingTop(3).Text("Inprem Admin").FontSize(9).Bold();
                    });
            });
        }).GeneratePdf();
    }
}
