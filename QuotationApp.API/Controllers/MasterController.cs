using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuotationApp.API.Data;
using QuotationApp.API.Models;

namespace QuotationApp.API.Controllers;

[ApiController]
[Route("api")]
public sealed class MasterController(QuotationDbContext db) : ControllerBase
{
    [HttpGet("customers")] public async Task<IActionResult> Customers() => Ok(await db.Customers.AsNoTracking().ToListAsync());
    [HttpGet("suppliers")] public async Task<IActionResult> Suppliers() => Ok(await db.Suppliers.AsNoTracking().ToListAsync());
    [HttpGet("company-profile")] public async Task<IActionResult> Profiles() => Ok(await db.CompanyProfiles.AsNoTracking().ToListAsync());
    [HttpGet("company-bank-accounts")] public async Task<IActionResult> Accounts() => Ok(await db.CompanyBankAccounts.AsNoTracking().ToListAsync());
    [HttpGet("gst-rates")] public async Task<IActionResult> Rates() => Ok(await db.GstRates.AsNoTracking().ToListAsync());
    [HttpGet("terms-templates")] public async Task<IActionResult> Terms() => Ok(await db.TermsTemplates.AsNoTracking().ToListAsync());

    [HttpPost("customers")] public Task<IActionResult> CreateCustomer(CustomerEntity value) => Save(value, db.Customers);
    [HttpPost("suppliers")] public Task<IActionResult> CreateSupplier(SupplierEntity value) => Save(value, db.Suppliers);
    [HttpPost("company-profile")] public Task<IActionResult> CreateProfile(CompanyProfileEntity value) => Save(value, db.CompanyProfiles);
    [HttpPost("company-bank-accounts")] public Task<IActionResult> CreateAccount(CompanyBankAccountEntity value) => Save(value, db.CompanyBankAccounts);
    [HttpPost("gst-rates")] public Task<IActionResult> CreateRate(GstRateEntity value) => Save(value, db.GstRates);
    [HttpPost("terms-templates")]
    public Task<IActionResult> CreateTerms(TermsTemplateEntity value) =>
        value.Type is "terms_of_sale" or "payment_terms" or "delivery_terms" ? Save(value, db.TermsTemplates) : Task.FromResult<IActionResult>(BadRequest(new { error = "Invalid terms template type." }));

    [HttpPut("customers/{id:int}")] public Task<IActionResult> UpdateCustomer(int id, CustomerEntity value) => Update(id, value, db.Customers);
    [HttpPut("suppliers/{id:int}")] public Task<IActionResult> UpdateSupplier(int id, SupplierEntity value) => Update(id, value, db.Suppliers);
    [HttpPut("company-profile/{id:int}")] public Task<IActionResult> UpdateProfile(int id, CompanyProfileEntity value) => Update(id, value, db.CompanyProfiles);
    [HttpPut("company-bank-accounts/{id:int}")] public Task<IActionResult> UpdateAccount(int id, CompanyBankAccountEntity value) => Update(id, value, db.CompanyBankAccounts);
    [HttpPut("gst-rates/{id:int}")] public Task<IActionResult> UpdateRate(int id, GstRateEntity value) => Update(id, value, db.GstRates);
    [HttpPut("terms-templates/{id:int}")] public Task<IActionResult> UpdateTerms(int id, TermsTemplateEntity value) => Update(id, value, db.TermsTemplates);

    [HttpDelete("customers/{id:int}")] public Task<IActionResult> DeleteCustomer(int id) => Delete(id, db.Customers);
    [HttpDelete("suppliers/{id:int}")] public Task<IActionResult> DeleteSupplier(int id) => Delete(id, db.Suppliers);
    [HttpDelete("company-profile/{id:int}")] public Task<IActionResult> DeleteProfile(int id) => Delete(id, db.CompanyProfiles);
    [HttpDelete("company-bank-accounts/{id:int}")] public Task<IActionResult> DeleteAccount(int id) => Delete(id, db.CompanyBankAccounts);
    [HttpDelete("gst-rates/{id:int}")] public Task<IActionResult> DeleteRate(int id) => Delete(id, db.GstRates);
    [HttpDelete("terms-templates/{id:int}")] public Task<IActionResult> DeleteTerms(int id) => Delete(id, db.TermsTemplates);

    private async Task<IActionResult> Save<TEntity>(TEntity value, DbSet<TEntity> set) where TEntity : class
    {
        set.Add(value);
        await db.SaveChangesAsync();
        return Created($"{Request.Path}/{GetId(value)}", value);
    }

    private async Task<IActionResult> Update<TEntity>(int id, TEntity value, DbSet<TEntity> set) where TEntity : class
    {
        var existing = await set.FindAsync(id);
        if (existing is null) return NotFound(new { error = "Record not found." });
        var entry = db.Entry(existing);
        foreach (var property in entry.Properties)
        {
            if (property.Metadata.IsPrimaryKey()) continue;

            var incomingProperty = value.GetType().GetProperty(property.Metadata.Name);
            if (incomingProperty is not null)
                property.CurrentValue = incomingProperty.GetValue(value);
        }
        await db.SaveChangesAsync();
        return Ok(existing);
    }

    private async Task<IActionResult> Delete<TEntity>(int id, DbSet<TEntity> set) where TEntity : class
    {
        var existing = await set.FindAsync(id);
        if (existing is null) return NotFound(new { error = "Record not found." });
        set.Remove(existing);
        await db.SaveChangesAsync();
        return NoContent();
    }

    private static object? GetId<TEntity>(TEntity value) where TEntity : class =>
        value.GetType().GetProperty("Id")?.GetValue(value);
}
