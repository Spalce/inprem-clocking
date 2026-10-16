using Inprem.Platform.Application.Billing;
using Inprem.Platform.Domain.Billing;
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
