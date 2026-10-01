using InpremClockingApp.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using QuestPDF.Fluent;
using QuestPDF.Helpers;

namespace InpremClockingApp.Controllers.Api;

// SuperAdmin-only - invoices are platform/provider data, not something any tenant Admin reaches
// (see multi-tenancy.md Part 2, Phase 8 and ROLES.md). Same QuestPDF pattern as
// StaffReportsController/VolunteerReportsController, but the "from" identity here is the
// platform operator (appsettings "Platform:*"), not a tenant's own ICurrentTenantProfile - this
// PDF is billed FROM the provider TO a tenant, not a tenant's own internal report.
[Authorize(Policy = "SuperAdminOnly")]
[ApiController]
[Route("api/invoices")]
public class InvoicesController : ControllerBase
{
    private readonly BillingService _billing;
    private readonly TenantAdminService _tenants;
    private readonly IConfiguration _config;

    public InvoicesController(BillingService billing, TenantAdminService tenants, IConfiguration config)
    {
        _billing = billing;
        _tenants = tenants;
        _config = config;
    }

    [HttpGet("{id:int}/pdf")]
    public async Task<IActionResult> GetInvoicePdf(int id)
    {
        var invoice = await _billing.GetInvoiceByIdAsync(id);
        if (invoice == null) return NotFound();

        var tenant = await _tenants.GetTenantByIdAsync(invoice.TenantId);
        if (tenant == null) return NotFound();

        var operatorName = _config["Platform:OperatorName"] ?? "Platform Operator";
        var operatorAddress = _config["Platform:OperatorAddress"];
        var operatorContact = _config["Platform:OperatorContactInfo"];

        var pdfBytes = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(50);

                page.Header()
                    .Column(header =>
                    {
                        header.Item().Row(row =>
                        {
                            row.RelativeItem().Column(from =>
                            {
                                from.Item().Text(operatorName).FontSize(16).Bold();
                                if (!string.IsNullOrWhiteSpace(operatorAddress))
                                    from.Item().Text(operatorAddress).FontSize(9);
                                if (!string.IsNullOrWhiteSpace(operatorContact))
                                    from.Item().Text(operatorContact).FontSize(9);
                            });

                            row.RelativeItem().AlignRight().Column(meta =>
                            {
                                meta.Item().Text("INVOICE").FontSize(18).Bold();
                                meta.Item().Text(invoice.InvoiceNumber).FontSize(11);
                            });
                        });

                        header.Item().PaddingTop(10).LineHorizontal(1);
                    });

                page.Content()
                    .PaddingTop(20)
                    .Column(column =>
                    {
                        column.Spacing(10);

                        column.Item().Row(row =>
                        {
                            row.RelativeItem().Column(billTo =>
                            {
                                billTo.Item().Text("Bill To").FontSize(10).Bold();
                                billTo.Item().Text(tenant.Name).FontSize(11);
                                if (!string.IsNullOrWhiteSpace(tenant.Address))
                                    billTo.Item().Text(tenant.Address).FontSize(9);
                                if (!string.IsNullOrWhiteSpace(tenant.ContactInfo))
                                    billTo.Item().Text(tenant.ContactInfo).FontSize(9);
                            });

                            row.RelativeItem().AlignRight().Column(dates =>
                            {
                                dates.Item().Text($"Issued: {invoice.IssuedDate:dd MMM yyyy}").FontSize(9);
                                dates.Item().Text($"Due: {invoice.DueDate:dd MMM yyyy}").FontSize(9);
                                dates.Item().Text($"Status: {invoice.Status}").FontSize(9).Bold();
                            });
                        });

                        column.Item()
                            .PaddingTop(10)
                            .Text($"Billing Period: {invoice.PeriodStart:dd MMM yyyy} - {invoice.PeriodEnd:dd MMM yyyy}")
                            .FontSize(10);

                        column.Item()
                            .PaddingTop(15)
                            .Table(table =>
                            {
                                table.ColumnsDefinition(columns =>
                                {
                                    columns.RelativeColumn(3);
                                    columns.RelativeColumn(1);
                                });

                                table.Header(h =>
                                {
                                    h.Cell().Text("Description").Bold();
                                    h.Cell().AlignRight().Text("Amount").Bold();
                                });

                                table.Cell().Text($"Subscription ({invoice.PeriodStart:dd MMM yyyy} - {invoice.PeriodEnd:dd MMM yyyy})");
                                table.Cell().AlignRight().Text($"{invoice.Amount:0.00} {invoice.Currency}");
                            });

                        column.Item()
                            .PaddingTop(10)
                            .AlignRight()
                            .Text($"Total Due: {invoice.Amount:0.00} {invoice.Currency}")
                            .FontSize(13)
                            .Bold();

                        if (invoice.Status == Models.Billing.InvoiceStatus.Paid)
                        {
                            column.Item()
                                .PaddingTop(10)
                                .Text($"Paid {invoice.PaidDate:dd MMM yyyy}" + (string.IsNullOrWhiteSpace(invoice.PaymentMethod) ? "" : $" via {invoice.PaymentMethod}"))
                                .FontSize(10);
                        }

                        if (!string.IsNullOrWhiteSpace(invoice.Notes))
                        {
                            column.Item().PaddingTop(10).Text($"Notes: {invoice.Notes}").FontSize(9);
                        }
                    });

                page.Footer()
                    .AlignCenter()
                    .Column(footer =>
                    {
                        footer.Item().Text("Please contact us with any billing questions.").FontSize(9);
                        footer.Item().PaddingTop(3).Text(operatorName).FontSize(9).Bold();
                    });
            });
        }).GeneratePdf();

        return File(pdfBytes, "application/pdf", $"{invoice.InvoiceNumber}.pdf");
    }
}
