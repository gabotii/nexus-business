using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Nexus.Application.Abstractions;
using Nexus.Application.Common;
using Nexus.Application.Tenancy;
using Nexus.Domain.Common;
using Nexus.Domain.Customers;
using Nexus.Domain.Tenancy;

namespace Nexus.Application.Customers;

/// <summary>
/// Customers of the current company: search, create, edit (optimistic concurrency), archive, tags and notes. The
/// company's duplicate rule is enforced under a per-company advisory lock, so two people cannot create the same
/// customer at the same time.
/// </summary>
public sealed class CustomerService(
    IAppDbContext db,
    IDatabaseTools tools,
    ITenantContext tenant,
    ICurrentUser currentUser,
    IUserDirectory users,
    IAuditLog audit,
    IValidator<SaveCustomerRequest> saveValidator,
    IValidator<CustomerQuery> queryValidator,
    IValidator<AddNoteRequest> noteValidator,
    TimeProvider clock)
{
    public async Task<PagedResult<CustomerListItem>> ListAsync(CustomerQuery query, CancellationToken ct)
    {
        tenant.Demand(Permissions.CustomersView);
        await queryValidator.ValidateAndThrowAsync(query, ct);

        var customers = db.Customers.AsNoTracking().Where(c => (c.ArchivedAt != null) == query.Archived);
        var term = TextNormalization.ForSearch(query.Q);
        if (term.Length > 0)
        {
            var pattern = Paging.ContainsPattern(term);
            customers = customers.Where(c => EF.Functions.Like(c.SearchText, pattern, "\\"));
        }

        if (query.TagId is { } tagId)
        {
            customers = customers.Where(c => c.Tags.Any(t => t.TagId == tagId));
        }

        customers = query.Sort == CustomerSort.Recent
            ? customers.OrderByDescending(c => c.CreatedAt).ThenBy(c => c.Id)
            : customers.OrderBy(c => c.Name).ThenBy(c => c.Id);

        var page = await customers
            .Select(c => new
            {
                c.Id, c.Kind, c.Name, c.LegalName, c.Email, c.Phone, c.TaxId, c.City, c.ArchivedAt, c.CreatedAt,
                TagIds = c.Tags.Select(t => t.TagId).ToList(),
            })
            .ToPageAsync(query.Page, query.PageSize, ct);
        var tags = await TagLookupAsync(ct);

        return new PagedResult<CustomerListItem>(
            [.. page.Items.Select(c => new CustomerListItem(c.Id, c.Kind, c.Name, c.LegalName, c.Email, c.Phone, c.TaxId,
                c.City, TagRefs(c.TagIds, tags), c.ArchivedAt is not null, c.CreatedAt))],
            page.Total, page.Page, page.PageSize);
    }

    public async Task<CustomerDto> GetAsync(Guid customerId, CancellationToken ct)
    {
        tenant.Demand(Permissions.CustomersView);
        var customer = await FindAsync(customerId, ct);
        return ToDto(customer, await TagLookupAsync(ct));
    }

    public async Task<CustomerDto> CreateAsync(SaveCustomerRequest request, CancellationToken ct)
    {
        tenant.Demand(Permissions.CustomersManage);
        await saveValidator.ValidateAndThrowAsync(request, ct);
        var details = ToDetails(request);
        var tagIds = await ValidTagIdsAsync(request.TagIds, ct);

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await tools.LockAsync(DuplicateLockKey, ct);
        await EnsureNotDuplicateAsync(details, exceptId: null, ct);

        var customer = new Customer(tenant.TenantId, details, currentUser.UserId, clock.GetUtcNow());
        customer.SetTags(tagIds);
        db.Customers.Add(customer);
        audit.Tenant("customers.created", "customer", customer.Id, new { customer.Kind });
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        return ToDto(customer, await TagLookupAsync(ct));
    }

    public async Task<CustomerDto> UpdateAsync(Guid customerId, SaveCustomerRequest request, CancellationToken ct)
    {
        tenant.Demand(Permissions.CustomersManage);
        await saveValidator.ValidateAndThrowAsync(request, ct);
        var version = request.Version ?? throw VersionRequired();
        var details = ToDetails(request);
        var customer = await FindAsync(customerId, ct);
        var tagIds = await ValidTagIdsAsync(request.TagIds, ct);

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await tools.LockAsync(DuplicateLockKey, ct);
        await EnsureNotDuplicateAsync(details, customer.Id, ct);

        var before = Snapshot(customer);
        customer.Update(details, currentUser.UserId, clock.GetUtcNow());
        customer.SetTags(tagIds);
        audit.Tenant("customers.updated", "customer", customer.Id,
            new { Changed = ChangedFields(before, Snapshot(customer)) });
        await db.SaveExpectingAsync(customer, version, ct);
        await transaction.CommitAsync(ct);

        return ToDto(customer, await TagLookupAsync(ct));
    }

    public async Task ArchiveAsync(Guid customerId, CancellationToken ct)
    {
        tenant.Demand(Permissions.CustomersManage);
        var customer = await FindAsync(customerId, ct);
        customer.Archive(clock.GetUtcNow());
        audit.Tenant("customers.archived", "customer", customer.Id);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Restoring re-checks the duplicate rule: the archived record may have been re-created meanwhile.</summary>
    public async Task RestoreAsync(Guid customerId, CancellationToken ct)
    {
        tenant.Demand(Permissions.CustomersManage);
        var customer = await FindAsync(customerId, ct);

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await tools.LockAsync(DuplicateLockKey, ct);
        await EnsureNotDuplicateAsync(DetailsOf(customer), customer.Id, ct, activeOnly: true);
        customer.Restore();
        audit.Tenant("customers.restored", "customer", customer.Id);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    public async Task<IReadOnlyList<CustomerNoteDto>> ListNotesAsync(Guid customerId, CancellationToken ct)
    {
        tenant.Demand(Permissions.CustomersView);
        await EnsureExistsAsync(customerId, ct);
        var notes = await db.CustomerNotes.AsNoTracking()
            .Where(n => n.CustomerId == customerId)
            .OrderByDescending(n => n.CreatedAt)
            .ToListAsync(ct);
        var authors = await users.GetManyAsync(notes.Select(n => n.AuthorId), ct);
        return [.. notes.Select(n => ToDto(n, authors))];
    }

    public async Task<CustomerNoteDto> AddNoteAsync(Guid customerId, AddNoteRequest request, CancellationToken ct)
    {
        tenant.Demand(Permissions.CustomersManage);
        await noteValidator.ValidateAndThrowAsync(request, ct);
        await EnsureExistsAsync(customerId, ct);

        var note = new CustomerNote(tenant.TenantId, customerId, request.Body, currentUser.UserId, clock.GetUtcNow());
        db.CustomerNotes.Add(note);
        // The note text is free text and never goes to the audit log (ADR-0009).
        audit.Tenant("customers.note_added", "customer", customerId, new { NoteId = note.Id });
        await db.SaveChangesAsync(ct);

        return ToDto(note, await users.GetManyAsync([note.AuthorId], ct));
    }

    /// <summary>Only the author deletes a note.</summary>
    public async Task DeleteNoteAsync(Guid customerId, Guid noteId, CancellationToken ct)
    {
        tenant.Demand(Permissions.CustomersManage);
        var note = await db.CustomerNotes.SingleOrDefaultAsync(n => n.Id == noteId && n.CustomerId == customerId, ct)
                   ?? throw new NotFoundException("La nota no existe.");
        if (note.AuthorId != currentUser.UserId)
        {
            throw new ForbiddenException("note_not_yours", "Solo quien escribió la nota puede eliminarla.");
        }

        db.CustomerNotes.Remove(note);
        audit.Tenant("customers.note_deleted", "customer", customerId, new { NoteId = note.Id });
        await db.SaveChangesAsync(ct);
    }

    private string DuplicateLockKey => $"customers:{tenant.TenantId}";

    private async Task<Customer> FindAsync(Guid customerId, CancellationToken ct) =>
        await db.Customers.Include(c => c.Tags).SingleOrDefaultAsync(c => c.Id == customerId, ct)
        ?? throw new NotFoundException("El cliente no existe.");

    private async Task EnsureExistsAsync(Guid customerId, CancellationToken ct)
    {
        if (!await db.Customers.AnyAsync(c => c.Id == customerId, ct))
        {
            throw new NotFoundException("El cliente no existe.");
        }
    }

    /// <summary>Tags must exist in this company; an unknown id (or another company's) is "not found".</summary>
    private async Task<IReadOnlyCollection<Guid>> ValidTagIdsAsync(IReadOnlyList<Guid>? requested, CancellationToken ct)
    {
        var ids = (requested ?? []).Distinct().ToList();
        if (ids.Count > 0 && await db.Tags.CountAsync(t => ids.Contains(t.Id), ct) != ids.Count)
        {
            throw new NotFoundException("Alguna de las etiquetas no existe.");
        }

        return ids;
    }

    private async Task EnsureNotDuplicateAsync(CustomerDetails details, Guid? exceptId, CancellationToken ct,
        bool activeOnly = false)
    {
        var rule = await db.Tenants.Where(t => t.Id == tenant.TenantId).Select(t => t.CustomerDuplicateRule).SingleAsync(ct);
        var email = rule is CustomerDuplicateRule.Email or CustomerDuplicateRule.EmailOrTaxId
            ? TextNormalization.Email(details.Email)
            : null;
        var taxId = rule is CustomerDuplicateRule.TaxId or CustomerDuplicateRule.EmailOrTaxId
            ? TextNormalization.TaxId(details.TaxId)
            : null;
        if (email is null && taxId is null)
        {
            return;
        }

        var duplicate = await db.Customers.AsNoTracking()
            .Where(c => c.Id != exceptId && (!activeOnly || c.ArchivedAt == null))
            .Where(c => (email != null && c.NormalizedEmail == email) || (taxId != null && c.NormalizedTaxId == taxId))
            .OrderBy(c => c.ArchivedAt != null)
            .Select(c => new { c.Id, c.Name, c.NormalizedEmail, c.ArchivedAt })
            .FirstOrDefaultAsync(ct);
        if (duplicate is null)
        {
            return;
        }

        var byEmail = email is not null && duplicate.NormalizedEmail == email;
        var field = byEmail ? "el mismo correo electrónico" : "el mismo NIF/CIF";
        var archived = duplicate.ArchivedAt is not null ? " (archivado)" : string.Empty;
        throw new ConflictException("customer_duplicate", $"Ya existe un cliente con {field}: {duplicate.Name}{archived}.",
            new Dictionary<string, object?>
            {
                ["existingCustomerId"] = duplicate.Id,
                ["field"] = byEmail ? "email" : "taxId",
                ["archived"] = duplicate.ArchivedAt is not null,
            });
    }

    private async Task<Dictionary<Guid, TagRef>> TagLookupAsync(CancellationToken ct) =>
        await db.Tags.AsNoTracking()
            .Select(t => new TagRef(t.Id, t.Name, t.Color))
            .ToDictionaryAsync(t => t.Id, ct);

    private static List<TagRef> TagRefs(IEnumerable<Guid> ids, Dictionary<Guid, TagRef> tags) =>
        [.. ids.Select(id => tags.GetValueOrDefault(id)).OfType<TagRef>().OrderBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase)];

    private static CustomerDetails ToDetails(SaveCustomerRequest r) =>
        new(r.Kind, r.Name, r.LegalName, r.TaxId, r.Email, r.Phone, (r.Address ?? new AddressDto(null, null, null, null, null)).ToDomain());

    private static CustomerDetails DetailsOf(Customer c) =>
        new(c.Kind, c.Name, c.LegalName, c.TaxId, c.Email, c.Phone, c.Address);

    private static CustomerDto ToDto(Customer c, Dictionary<Guid, TagRef> tags) => new(
        c.Id, c.Kind, c.Name, c.LegalName, c.TaxId, c.Email, c.Phone, AddressDto.From(c.Address),
        TagRefs(c.Tags.Select(t => t.TagId), tags), c.IsArchived, c.CreatedAt, c.UpdatedAt, c.Version);

    private CustomerNoteDto ToDto(CustomerNote n, IReadOnlyDictionary<Guid, UserSummary> authors) =>
        new(n.Id, n.Body, authors.GetValueOrDefault(n.AuthorId)?.DisplayName ?? "—", n.AuthorId == currentUser.UserId,
            n.CreatedAt);

    private static Dictionary<string, string?> Snapshot(Customer c) => new()
    {
        ["kind"] = c.Kind.ToString(),
        ["name"] = c.Name,
        ["legalName"] = c.LegalName,
        ["taxId"] = c.TaxId,
        ["email"] = c.Email,
        ["phone"] = c.Phone,
        ["address"] = string.Join('|', c.AddressLine, c.City, c.PostalCode, c.Region, c.CountryCode),
        ["tags"] = string.Join(',', c.Tags.Select(t => t.TagId).Order()),
    };

    /// <summary>Audit metadata lists which fields changed, never their values (personal data, ADR-0009).</summary>
    private static string[] ChangedFields(Dictionary<string, string?> before, Dictionary<string, string?> after) =>
        [.. before.Keys.Where(k => before[k] != after[k])];

    private static ValidationException VersionRequired() =>
        new([new FluentValidation.Results.ValidationFailure("version", "Falta la versión del registro.")]);
}

/// <summary>Customer tags of the current company.</summary>
public sealed class TagService(
    IAppDbContext db,
    IDatabaseTools tools,
    ITenantContext tenant,
    IAuditLog audit,
    IValidator<SaveTagRequest> validator,
    TimeProvider clock)
{
    internal const string NameConstraint = "ux_tags_tenant_name";

    public async Task<IReadOnlyList<TagDto>> ListAsync(CancellationToken ct)
    {
        tenant.Demand(Permissions.CustomersView);
        var tags = await db.Tags.AsNoTracking()
            .Select(t => new TagDto(t.Id, t.Name, t.Color,
                db.CustomerTags.Count(ct2 => ct2.TagId == t.Id && db.Customers.Any(c => c.Id == ct2.CustomerId && c.ArchivedAt == null))))
            .ToListAsync(ct);
        return [.. tags.OrderBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase)];
    }

    public async Task<TagDto> CreateAsync(SaveTagRequest request, CancellationToken ct)
    {
        tenant.Demand(Permissions.CustomersManage);
        await validator.ValidateAndThrowAsync(request, ct);
        await EnsureNameFreeAsync(request.Name, null, ct);
        var tag = new Tag(tenant.TenantId, request.Name, request.Color, clock.GetUtcNow());
        db.Tags.Add(tag);
        audit.Tenant("tags.created", "tag", tag.Id, new { tag.Name, tag.Color });
        await SaveAsync(ct);
        return new TagDto(tag.Id, tag.Name, tag.Color, 0);
    }

    public async Task<TagDto> UpdateAsync(Guid tagId, SaveTagRequest request, CancellationToken ct)
    {
        tenant.Demand(Permissions.CustomersManage);
        await validator.ValidateAndThrowAsync(request, ct);
        var tag = await db.Tags.SingleOrDefaultAsync(t => t.Id == tagId, ct) ?? throw new NotFoundException("La etiqueta no existe.");
        await EnsureNameFreeAsync(request.Name, tag.Id, ct);
        tag.Update(request.Name, request.Color);
        audit.Tenant("tags.updated", "tag", tag.Id, new { tag.Name, tag.Color });
        await SaveAsync(ct);
        return new TagDto(tag.Id, tag.Name, tag.Color, await db.CustomerTags.CountAsync(t => t.TagId == tag.Id, ct));
    }

    /// <summary>Deleting a tag removes it from every customer (the links cascade).</summary>
    public async Task DeleteAsync(Guid tagId, CancellationToken ct)
    {
        tenant.Demand(Permissions.CustomersManage);
        var tag = await db.Tags.SingleOrDefaultAsync(t => t.Id == tagId, ct) ?? throw new NotFoundException("La etiqueta no existe.");
        db.Tags.Remove(tag);
        audit.Tenant("tags.deleted", "tag", tag.Id, new { tag.Name });
        await db.SaveChangesAsync(ct);
    }

    private async Task EnsureNameFreeAsync(string name, Guid? exceptId, CancellationToken ct)
    {
        var normalized = TextNormalization.ForSearch(name);
        if (await db.Tags.AnyAsync(t => t.NormalizedName == normalized && t.Id != exceptId, ct))
        {
            throw NameTaken();
        }
    }

    private async Task SaveAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (tools.IsUniqueViolation(ex, NameConstraint))
        {
            throw NameTaken();
        }
    }

    private static ConflictException NameTaken() => new("tag_name_taken", "Ya existe una etiqueta con ese nombre.");
}
