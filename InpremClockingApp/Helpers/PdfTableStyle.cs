using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace InpremClockingApp.Helpers;

// Shared cell styling for every QuestPDF table in the app (Dashboard monthly breakdown,
// Hours Worked / Clocking Report / roster PDFs for both Staff and Volunteer) - one definition of
// "the table design" instead of each report hand-rolling its own Background/Border/FontSize combo,
// which is how the header and body styling drifted out of sync with each other in the first place.
// Font sizes are picked so a "dd/MM/yyyy HH:mm" timestamp fits on one line inside a normal
// (~90pt) column without an awkward mid-token wrap.
public static class PdfTableStyle
{
    public static IContainer HeaderCell(this IContainer container) =>
        container.Background(Colors.BlueGrey.Darken4).Padding(5).AlignLeft();

    public static void HeaderText(this IContainer container, string text) =>
        container.HeaderCell().Text(text).FontColor(Colors.White).FontSize(10).Bold();

    public static IContainer BodyCell(this IContainer container) =>
        container.Border(1).BorderColor(Colors.Grey.Lighten2).Padding(5).AlignLeft();

    public static void BodyText(this IContainer container, string text) =>
        container.BodyCell().Text(text).FontSize(9);
}
