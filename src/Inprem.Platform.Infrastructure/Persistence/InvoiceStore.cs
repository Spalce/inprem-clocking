using Inprem.Platform.Application.Billing;
using Inprem.Platform.Domain.Audit;
using Inprem.Platform.Domain.Billing;
using Inprem.Platform.Domain.Memberships;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Inprem.Platform.Infrastructure.Persistence;

public sealed class InvoiceStore : IInvoiceStore
{
    private readonly PlatformDbContext _db;

    public InvoiceStore(PlatformDbContext db) => _db = db;

    public async Task<string> NextNumberAsync(CancellationToken ct = default)
    {
        // A plain statement: SQL Server does not allow NEXT VALUE FOR inside the subquery EF would wrap a query in.
        var next = new SqlParameter("@next", System.Data.SqlDbType.BigInt) { Direction = System.Data.ParameterDirection.Output };
        await _db.Database.ExecuteSqlRawAsync(
            $"SET @next = NEXT VALUE FOR [{PlatformDbContext.Schema}].[{PlatformDbContext.InvoiceNumberSequence}];", new object[] { next }, ct).ConfigureAwait(false);
        return Invoice.FormatNumber((long)next.Value);
    }

    public async Task AddAsync(Invoice invoice, CancellationToken ct = default) => await _db.Invoices.AddAsync(invoice, ct).ConfigureAwait(false);

    public Task<Invoice?> FindAsync(Guid id, CancellationToken ct = default) => _db.Invoices.FirstOrDefaultAsync(i => i.Id == id, ct);

    public Task<Invoice?> FindByExternalIdAsync(string externalInvoiceId, CancellationToken ct = default) =>
        _db.Invoices.FirstOrDefaultAsync(i => i.ExternalInvoiceId == externalInvoiceId, ct);

    public async Task<IReadOnlyList<Invoice>> ListForTenantAsync(int tenantId, int max, CancellationToken ct = default) =>
        await _db.Invoices.AsNoTracking().Where(i => i.TenantId == tenantId)
            .OrderByDescending(i => i.IssueDate).ThenByDescending(i => i.Number).Take(max).ToListAsync(ct).ConfigureAwait(false);

    public async Task<InvoicePage> SearchAsync(InvoiceFilter filter, int pageSize, CancellationToken ct = default)
    {
        var query = _db.Invoices.AsNoTracking().AsQueryable();
        if (filter.TenantId != null) query = query.Where(i => i.TenantId == filter.TenantId);
        if (filter.Status != null) query = query.Where(i => i.Status == filter.Status);
        if (filter.Source != null) query = query.Where(i => i.Source == filter.Source);
        if (filter.From != null) query = query.Where(i => i.IssueDate >= filter.From);
        if (filter.To != null) query = query.Where(i => i.IssueDate <= filter.To);

        var total = await query.CountAsync(ct).ConfigureAwait(false);
        var pages = Math.Max(1, (total + pageSize - 1) / pageSize);
        var page = Math.Clamp(filter.Page, 1, pages);
        var rows = await (from i in query
                          join t in _db.Tenants.AsNoTracking() on i.TenantId equals t.Id into tenants
                          from t in tenants.DefaultIfEmpty()
                          orderby i.IssueDate descending, i.Number descending
                          select new { i.Id, i.Number, i.TenantId, Name = t == null ? null : t.Name, i.Source, i.Status, i.Total, i.PaidAmount, i.Currency, i.IssueDate, i.DueDate })
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct).ConfigureAwait(false);
        var items = rows.Select(r => new InvoiceListItem(r.Id, r.Number, r.TenantId, r.Name ?? $"#{r.TenantId}", r.Source, r.Status, r.Total,
            r.Status is InvoiceStatus.Cancelled or InvoiceStatus.Draft ? 0 : Math.Max(0, r.Total - r.PaidAmount), r.Currency, r.IssueDate, r.DueDate)).ToList();
        return new InvoicePage(items, total, page, pages);
    }

    public async Task<IReadOnlyList<Payment>> PaymentsForInvoiceAsync(Guid invoiceId, CancellationToken ct = default) =>
        await _db.Payments.Where(p => p.InvoiceId == invoiceId).OrderBy(p => p.PaidUtc).ToListAsync(ct).ConfigureAwait(false);

    public Task<Payment?> FindPaymentAsync(Guid paymentId, CancellationToken ct = default) => _db.Payments.FirstOrDefaultAsync(p => p.Id == paymentId, ct);

    public async Task<IReadOnlyList<PaymentRefund>> RefundsForPaymentsAsync(IReadOnlyCollection<Guid> paymentIds, CancellationToken ct = default) =>
        paymentIds.Count == 0 ? Array.Empty<PaymentRefund>()
            : await _db.PaymentRefunds.Where(r => paymentIds.Contains(r.PaymentId)).OrderBy(r => r.RefundedUtc).ToListAsync(ct).ConfigureAwait(false);

    public Task<bool> OfflineReferenceExistsAsync(string reference, CancellationToken ct = default) =>
        _db.Payments.AnyAsync(p => p.Source == PaymentSource.Offline && p.ExternalId == reference, ct);

    public async Task<bool> RefundExistsAsync(string? externalRefundId, CancellationToken ct = default) =>
        externalRefundId != null && await _db.PaymentRefunds.AnyAsync(r => r.ExternalRefundId == externalRefundId, ct).ConfigureAwait(false);

    public async Task AddPaymentAsync(Payment payment, CancellationToken ct = default) => await _db.Payments.AddAsync(payment, ct).ConfigureAwait(false);

    public async Task AddRefundAsync(PaymentRefund refund, CancellationToken ct = default) => await _db.PaymentRefunds.AddAsync(refund, ct).ConfigureAwait(false);

    public async Task AddAuditAsync(AuditEntry entry, CancellationToken ct = default) => await _db.AuditEntries.AddAsync(entry, ct).ConfigureAwait(false);

    public async Task<string?> TenantNameAsync(int tenantId, CancellationToken ct = default) =>
        await _db.Tenants.AsNoTracking().Where(t => t.Id == tenantId).Select(t => t.Name).FirstOrDefaultAsync(ct).ConfigureAwait(false);

    public async Task<IReadOnlyList<string>> OwnerEmailsAsync(int tenantId, CancellationToken ct = default) =>
        await (from m in _db.TenantMemberships.AsNoTracking()
               join a in _db.Accounts.AsNoTracking() on m.UserId equals a.Id
               where m.TenantId == tenantId && m.Role == MembershipRole.Owner && m.Status == MembershipStatus.Active && a.Email != null
               select a.Email!).Distinct().OrderBy(e => e).ToListAsync(ct).ConfigureAwait(false);

    public async Task<InvoiceSaveResult> SaveAsync(CancellationToken ct = default)
    {
        try
        {
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            return InvoiceSaveResult.Saved;
        }
        catch (DbUpdateException e) when (e.InnerException is SqlException { Number: 2601 or 2627 } sql && sql.Message.Contains("IX_Invoices_OnePerPeriod", StringComparison.Ordinal))
        {
            foreach (var entry in e.Entries) entry.State = EntityState.Detached;
            return InvoiceSaveResult.DuplicatePeriod;
        }
    }
}
