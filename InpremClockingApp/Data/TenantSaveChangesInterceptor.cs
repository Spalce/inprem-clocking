using InpremClockingApp.Models;
using InpremClockingApp.Models.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata;

namespace InpremClockingApp.Data;

/// <summary>
/// Tenant rules for every save through <see cref="ApplicationDbContext"/> (C2-1, multi-tenancy
/// Phase 0 step 0.5). It replaces the context's own TenantId stamping.
/// <list type="bullet">
/// <item><b>Added</b>: a missing TenantId is stamped with the current organization; a row for
/// another organization is refused.</item>
/// <item><b>Modified / Deleted</b>: the row's TenantId is read from the database (not from the
/// entity, which a request may have built itself) and must be the current organization's. This
/// is what stops a page from changing or deleting another organization's row by posting its id
/// (finding F-08).</item>
/// <item><b>TenantId never changes</b> on an existing row (AppUser included).</item>
/// </list>
/// The rules apply when an organization is set for the request. Without one (sign-in, startup
/// seeding, platform-operator pages, background work) only "TenantId never changes" applies; those
/// callers cannot reach organization data through the query filters anyway.
/// </summary>
public sealed class TenantSaveChangesInterceptor : SaveChangesInterceptor
{
    /// <summary>Entities owned by one organization through a non-null <c>int TenantId</c>.</summary>
    public static readonly IReadOnlySet<Type> TenantOwned = new HashSet<Type>
    {
        typeof(Staff), typeof(Volunteer), typeof(ClockingStaff), typeof(Clocking),
        typeof(Models.Billing.Subscription), typeof(Models.Billing.Invoice),
    };

    /// <summary>
    /// One shared, stateless instance: EF Core builds an internal service provider per distinct
    /// set of options, so a new interceptor per context would soon exhaust them. The organization
    /// is read from the context being saved.
    /// </summary>
    public static readonly TenantSaveChangesInterceptor Instance = new();

    private TenantSaveChangesInterceptor()
    {
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        if (eventData.Context != null) ApplyAsync(eventData.Context, sync: true, CancellationToken.None).GetAwaiter().GetResult();
        return result;
    }

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (eventData.Context != null) await ApplyAsync(eventData.Context, sync: false, cancellationToken).ConfigureAwait(false);
        return result;
    }

    private async Task ApplyAsync(DbContext context, bool sync, CancellationToken cancellationToken)
    {
        if (context is not ApplicationDbContext app) return;
        var tenantId = app.CurrentTenantId;

        foreach (var entry in context.ChangeTracker.Entries().ToList())
        {
            var clrType = entry.Metadata.ClrType;

            if (clrType == typeof(AppUser))
            {
                // A new account created inside an organization belongs to it (as before, e.g. the
                // Register page); accounts are otherwise governed by memberships, not this column.
                var user = (AppUser)entry.Entity;
                if (entry.State == EntityState.Added && user.TenantId == null && tenantId.HasValue) user.TenantId = tenantId.Value;
                PreventTenantIdChange(entry);
                continue;
            }

            if (clrType == typeof(Tenant))
            {
                if (tenantId.HasValue && entry.State is EntityState.Modified or EntityState.Deleted && ((Tenant)entry.Entity).Id != tenantId.Value)
                {
                    throw Refused(entry, "another organization's record");
                }
                continue;
            }

            if (!TenantOwned.Contains(clrType)) continue;

            var property = entry.Property(nameof(Staff.TenantId));
            switch (entry.State)
            {
                case EntityState.Added:
                    if (tenantId.HasValue)
                    {
                        // TenantId is part of an alternate key (M4), so EF gives a new row a temporary
                        // placeholder until saved: treat it like "not set".
                        if (property.IsTemporary || (int)property.CurrentValue! == 0)
                        {
                            property.CurrentValue = tenantId.Value;
                            property.IsTemporary = false;
                        }
                        else if ((int)property.CurrentValue! != tenantId.Value)
                        {
                            throw Refused(entry, "a row for another organization");
                        }
                    }
                    break;

                case EntityState.Modified:
                case EntityState.Deleted:
                    PreventTenantIdChange(entry);
                    if (tenantId.HasValue)
                    {
                        var stored = await StoredTenantIdAsync(context, entry, sync, cancellationToken).ConfigureAwait(false);
                        // Row already gone: let EF report its own concurrency error, as before.
                        if (stored.HasValue && stored.Value != tenantId.Value)
                        {
                            throw Refused(entry, "another organization's row");
                        }
                    }
                    break;
            }
        }
    }

    /// <summary>
    /// The row's TenantId as stored, read with plain SQL by primary key: query filters cannot hide
    /// it (another organization's row must be seen to be refused) and the entity's own values,
    /// which a request may have supplied, are not trusted.
    /// </summary>
    private static async Task<int?> StoredTenantIdAsync(DbContext context, EntityEntry entry, bool sync, CancellationToken cancellationToken)
    {
        var entity = entry.Metadata;
        var table = entity.GetTableName()!;
        // The column lookup needs the schema exactly as mapped (null = the default schema).
        var store = StoreObjectIdentifier.Table(table, entity.GetSchema());
        var schema = entity.GetSchema() ?? "dbo";
        var key = entity.FindPrimaryKey()!.Properties.Single();
        var keyColumn = key.GetColumnName(store)!;
        var tenantColumn = entity.FindProperty(nameof(Staff.TenantId))!.GetColumnName(store)!;
        var keyValue = entry.Property(key.Name).OriginalValue!;

        var sql = $"SELECT [{tenantColumn}] AS [Value] FROM [{schema}].[{table}] WHERE [{keyColumn}] = {{0}}";
        var query = context.Database.SqlQueryRaw<int>(sql, keyValue);
        var values = sync ? query.ToList() : await query.ToListAsync(cancellationToken).ConfigureAwait(false);
        return values.Count == 0 ? null : values[0];
    }

    private static void PreventTenantIdChange(EntityEntry entry)
    {
        if (entry.State != EntityState.Modified) return;
        var property = entry.Metadata.FindProperty(nameof(Staff.TenantId));
        if (property == null) return;

        var tenant = entry.Property(property.Name);
        if (tenant.IsModified && !Equals(tenant.OriginalValue, tenant.CurrentValue))
        {
            throw Refused(entry, "a change of organization (TenantId)");
        }
    }

    private static TenantIsolationException Refused(EntityEntry entry, string what) =>
        new($"Refused to save {entry.Metadata.ClrType.Name} ({entry.State}): {what}.");
}

/// <summary>A save that would cross organizations. Never caught by app code: it ends the request.</summary>
public sealed class TenantIsolationException : InvalidOperationException
{
    public TenantIsolationException(string message) : base(message)
    {
    }
}
