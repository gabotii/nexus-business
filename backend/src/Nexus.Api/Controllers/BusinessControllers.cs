using Microsoft.AspNetCore.Mvc;
using Nexus.Api.Web;
using Nexus.Application.Catalog;
using Nexus.Application.Common;
using Nexus.Application.Customers;
using Nexus.Application.Tenancy;
using Nexus.Domain.Common;
using Nexus.Domain.Files;

namespace Nexus.Api.Controllers;

[Route(Prefix + "/customers")]
public sealed class CustomersController(CustomerService customers) : TenantControllerBase
{
    /// <summary>Paged list. <c>q</c> searches name, legal name, e-mail, tax id, phone and city (accent-insensitive).</summary>
    [HttpGet]
    public Task<PagedResult<CustomerListItem>> List([FromQuery] CustomerQuery query, CancellationToken ct) =>
        customers.ListAsync(query, ct);

    [HttpGet("{customerId:guid}")]
    public Task<CustomerDto> Get(Guid customerId, CancellationToken ct) => customers.GetAsync(customerId, ct);

    /// <summary>409 <c>customer_duplicate</c> (with <c>existingCustomerId</c>) when the company's duplicate rule matches.</summary>
    [HttpPost]
    public async Task<CreatedResult> Create(SaveCustomerRequest request, CancellationToken ct)
    {
        var customer = await customers.CreateAsync(request, ct);
        return Created($"{Request.Path}/{customer.Id}", customer);
    }

    /// <summary>Requires the <c>version</c> that was read; 409 <c>concurrency_conflict</c> if someone saved meanwhile.</summary>
    [HttpPut("{customerId:guid}")]
    public Task<CustomerDto> Update(Guid customerId, SaveCustomerRequest request, CancellationToken ct) =>
        customers.UpdateAsync(customerId, request, ct);

    [HttpPost("{customerId:guid}/archive")]
    public async Task<NoContentResult> Archive(Guid customerId, CancellationToken ct)
    {
        await customers.ArchiveAsync(customerId, ct);
        return NoContent();
    }

    [HttpPost("{customerId:guid}/restore")]
    public async Task<NoContentResult> Restore(Guid customerId, CancellationToken ct)
    {
        await customers.RestoreAsync(customerId, ct);
        return NoContent();
    }

    [HttpGet("{customerId:guid}/notes")]
    public Task<IReadOnlyList<CustomerNoteDto>> Notes(Guid customerId, CancellationToken ct) =>
        customers.ListNotesAsync(customerId, ct);

    [HttpPost("{customerId:guid}/notes")]
    public async Task<CreatedResult> AddNote(Guid customerId, AddNoteRequest request, CancellationToken ct)
    {
        var note = await customers.AddNoteAsync(customerId, request, ct);
        return Created($"{Request.Path}/{note.Id}", note);
    }

    [HttpDelete("{customerId:guid}/notes/{noteId:guid}")]
    public async Task<NoContentResult> DeleteNote(Guid customerId, Guid noteId, CancellationToken ct)
    {
        await customers.DeleteNoteAsync(customerId, noteId, ct);
        return NoContent();
    }
}

[Route(Prefix + "/tags")]
public sealed class TagsController(TagService tags) : TenantControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<TagDto>> List(CancellationToken ct) => tags.ListAsync(ct);

    [HttpPost]
    public async Task<CreatedResult> Create(SaveTagRequest request, CancellationToken ct)
    {
        var tag = await tags.CreateAsync(request, ct);
        return Created($"{Request.Path}/{tag.Id}", tag);
    }

    [HttpPut("{tagId:guid}")]
    public Task<TagDto> Update(Guid tagId, SaveTagRequest request, CancellationToken ct) => tags.UpdateAsync(tagId, request, ct);

    [HttpDelete("{tagId:guid}")]
    public async Task<NoContentResult> Delete(Guid tagId, CancellationToken ct)
    {
        await tags.DeleteAsync(tagId, ct);
        return NoContent();
    }
}

[Route(Prefix + "/product-categories")]
public sealed class ProductCategoriesController(CatalogService catalog) : TenantControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<CategoryDto>> List(CancellationToken ct) => catalog.ListCategoriesAsync(ct);

    [HttpPost]
    public async Task<CreatedResult> Create(SaveCategoryRequest request, CancellationToken ct)
    {
        var category = await catalog.CreateCategoryAsync(request, ct);
        return Created($"{Request.Path}/{category.Id}", category);
    }

    [HttpPut("{categoryId:guid}")]
    public Task<CategoryDto> Rename(Guid categoryId, SaveCategoryRequest request, CancellationToken ct) =>
        catalog.RenameCategoryAsync(categoryId, request, ct);

    [HttpPost("{categoryId:guid}/archive")]
    public async Task<NoContentResult> Archive(Guid categoryId, CancellationToken ct)
    {
        await catalog.SetCategoryArchivedAsync(categoryId, archived: true, ct);
        return NoContent();
    }

    [HttpPost("{categoryId:guid}/restore")]
    public async Task<NoContentResult> Restore(Guid categoryId, CancellationToken ct)
    {
        await catalog.SetCategoryArchivedAsync(categoryId, archived: false, ct);
        return NoContent();
    }
}

[Route(Prefix + "/products")]
public sealed class ProductsController(CatalogService catalog) : TenantControllerBase
{
    /// <summary>Paged list. <c>cost</c> is null unless the caller has <c>products.view_cost</c>.</summary>
    [HttpGet]
    public Task<PagedResult<ProductListItem>> List([FromQuery] ProductQuery query, CancellationToken ct) =>
        catalog.ListProductsAsync(query, ct);

    [HttpGet("{productId:guid}")]
    public Task<ProductDto> Get(Guid productId, CancellationToken ct) => catalog.GetProductAsync(productId, ct);

    [HttpPost]
    public async Task<CreatedResult> Create(SaveProductRequest request, CancellationToken ct)
    {
        var product = await catalog.CreateProductAsync(request, ct);
        return Created($"{Request.Path}/{product.Id}", product);
    }

    [HttpPut("{productId:guid}")]
    public Task<ProductDto> Update(Guid productId, SaveProductRequest request, CancellationToken ct) =>
        catalog.UpdateProductAsync(productId, request, ct);

    [HttpPost("{productId:guid}/archive")]
    public async Task<NoContentResult> Archive(Guid productId, CancellationToken ct)
    {
        await catalog.SetProductArchivedAsync(productId, archived: true, ct);
        return NoContent();
    }

    [HttpPost("{productId:guid}/restore")]
    public async Task<NoContentResult> Restore(Guid productId, CancellationToken ct)
    {
        await catalog.SetProductArchivedAsync(productId, archived: false, ct);
        return NoContent();
    }
}

[Route(Prefix + "/settings")]
public sealed class SettingsController(TenantSettingsService settings) : TenantControllerBase
{
    [HttpGet]
    public Task<TenantSettingsDto> Get(CancellationToken ct) => settings.GetAsync(ct);

    [HttpPut]
    public Task<TenantSettingsDto> Update(UpdateTenantSettingsRequest request, CancellationToken ct) =>
        settings.UpdateAsync(request, ct);

    /// <summary>Multipart upload (field <c>file</c>). PNG, JPEG or WebP up to 512 KB, recognised by content.</summary>
    [HttpPut("logo")]
    [RequestSizeLimit(ImageFormats.MaxLogoBytes + 64 * 1024)]
    public async Task<TenantSettingsDto> UploadLogo(IFormFile file, CancellationToken ct)
    {
        if (file.Length > ImageFormats.MaxLogoBytes)
        {
            throw new DomainException("logo_size", "El logo debe ocupar como máximo 512 KB.");
        }

        using var buffer = new MemoryStream((int)file.Length);
        await file.CopyToAsync(buffer, ct);
        return await settings.SetLogoAsync(buffer.GetBuffer().AsMemory(0, (int)buffer.Length), ct);
    }

    [HttpDelete("logo")]
    public async Task<NoContentResult> RemoveLogo(CancellationToken ct)
    {
        await settings.RemoveLogoAsync(ct);
        return NoContent();
    }

    /// <summary>The logo bytes, for any member. Served inline under the API's deny-all CSP and nosniff.</summary>
    [HttpGet("logo")]
    public async Task<FileStreamResult> Logo(CancellationToken ct)
    {
        var logo = await settings.OpenLogoAsync(ct);
        Response.Headers.ContentDisposition = "inline; filename=\"logo\"";
        return File(logo.Content, logo.ContentType);
    }
}
