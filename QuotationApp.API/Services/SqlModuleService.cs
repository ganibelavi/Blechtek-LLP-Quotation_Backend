using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Hosting;
using QuotationApp.API.Data;
using QuotationApp.API.Models;

namespace QuotationApp.API.Services;

/// <summary>
/// SQL-backed implementation of IModuleService using Entity Framework Core.
/// Replaces the JSON-file-based ModuleService.
/// </summary>
public class SqlModuleService : IModuleService
{
    private readonly QuotationDbContext _dbContext;
    private List<ModuleItem>? _cache;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public SqlModuleService(QuotationDbContext dbContext, IOptions<QuotationSettings> settings, IWebHostEnvironment env)
    {
        _dbContext = dbContext;
    }

    public async Task<List<ModuleItem>> GetModulesAsync()
    {
        if (_cache != null) return _cache;

        await _lock.WaitAsync();
        try
        {
            if (_cache != null) return _cache;

            var modules = await _dbContext.Modules
                .AsNoTracking()
                .OrderBy(m => m.Pillar)
                .ThenBy(m => m.ModuleName)
                .Select(m => new ModuleItem
                {
                    Id = m.Id,
                    Pillar = m.Pillar,
                    Module = m.ModuleName,
                    ModuleName = m.ModuleName,
                    Price = m.Price,
                    HsnCode = m.HsnCode,
                    SacCode = m.SacCode,
                    ReverseChargeDefault = m.ReverseChargeDefault,
                    ImplementationEffortCost = m.ImplementationEffortCost,
                })
                .ToListAsync();

            _cache = modules ?? new List<ModuleItem>();
            return _cache;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Adds a new module to the database.
    /// </summary>
    public async Task<ModuleItem> AddModuleAsync(ModuleUpsertRequest request)
    {
        var pillar = request.Pillar.Trim();
        var moduleName = request.ModuleName.Trim();
        var duplicateNameExists = await _dbContext.Modules
            .AnyAsync(item => item.ModuleName == moduleName);

        if (duplicateNameExists)
        {
            throw new InvalidOperationException(
                $"A module with the name \"{moduleName}\" already exists. Use a different ModuleName.");
        }

        var entity = new ModuleEntity
        {
            Pillar = pillar,
            ModuleName = moduleName,
            Price = request.Price,
            HsnCode = request.HsnCode,
            SacCode = request.SacCode,
            ReverseChargeDefault = request.ReverseChargeDefault,
            ImplementationEffortCost = request.ImplementationEffortCost,
        };

        _dbContext.Modules.Add(entity);
        await _dbContext.SaveChangesAsync();
        _cache = null;
        return ToModuleItem(entity);
    }

    /// <summary>
    /// Updates an existing module.
    /// </summary>
    public async Task<ModuleItem?> UpdateModuleAsync(int id, ModuleUpsertRequest request)
    {
        var entity = await _dbContext.Modules.FindAsync(id);
        if (entity == null) return null;

        var pillar = request.Pillar.Trim();
        var moduleName = request.ModuleName.Trim();
        var moduleNameChanged = !string.Equals(
            entity.ModuleName,
            moduleName,
            StringComparison.Ordinal);

        if (moduleNameChanged)
        {
            var isUsedInQuotation = await _dbContext.QuotationModules
                .AnyAsync(item => item.ModuleName == entity.ModuleName);
            if (isUsedInQuotation)
            {
                throw new InvalidOperationException(
                    "This module is already used in a quotation, so its name cannot be changed. " +
                    "Create a new module instead to preserve quotation history.");
            }

            var duplicateNameExists = await _dbContext.Modules
                .AnyAsync(item => item.Id != id && item.ModuleName == moduleName);
            if (duplicateNameExists)
            {
                throw new InvalidOperationException(
                    $"A module with the name \"{moduleName}\" already exists. Use a different ModuleName.");
            }

            // ModuleName is an alternate key. EF Core does not permit changing key
            // values on a tracked entity, so use a parameterized SQL update here.
            await _dbContext.Database.ExecuteSqlInterpolatedAsync($@"
                UPDATE [Modules]
                SET [Pillar] = {pillar},
                    [ModuleName] = {moduleName},
                    [Price] = {request.Price},
                    [HsnCode] = {request.HsnCode},
                    [SacCode] = {request.SacCode},
                    [ReverseChargeDefault] = {request.ReverseChargeDefault},
                    [ImplementationEffortCost] = {request.ImplementationEffortCost}
                WHERE [Id] = {id}");

            _cache = null;
            return new ModuleItem
            {
                Id = id,
                Pillar = pillar,
                Module = moduleName,
                ModuleName = moduleName,
                Price = request.Price,
                HsnCode = request.HsnCode,
                SacCode = request.SacCode,
                ReverseChargeDefault = request.ReverseChargeDefault,
                ImplementationEffortCost = request.ImplementationEffortCost,
            };
        }

        entity.Pillar = pillar;
        entity.Price = request.Price;
        entity.HsnCode = request.HsnCode;
        entity.SacCode = request.SacCode;
        entity.ReverseChargeDefault = request.ReverseChargeDefault;
        entity.ImplementationEffortCost = request.ImplementationEffortCost;
        await _dbContext.SaveChangesAsync();
        _cache = null;
        return ToModuleItem(entity);
    }

    /// <summary>
    /// Deletes a module from the database.
    /// </summary>
    public async Task<bool> DeleteModuleAsync(int id)
    {
        var entity = await _dbContext.Modules.FindAsync(id);
        if (entity == null) return false;

        _dbContext.Modules.Remove(entity);
        await _dbContext.SaveChangesAsync();
        _cache = null;
        return true;
    }

    /// <summary>
    /// Seeds the database with initial module data from JSON file (run once on startup).
    /// </summary>
    public async Task SeedFromJsonAsync(string jsonFilePath)
    {
        if (!File.Exists(jsonFilePath)) return;

        var json = await File.ReadAllTextAsync(jsonFilePath);
        var modules = JsonSerializer.Deserialize<List<ModuleItem>>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? new List<ModuleItem>();

        foreach (var module in modules)
        {
            var exists = await _dbContext.Modules.AnyAsync(m => m.ModuleName == module.Module);
            if (!exists)
            {
                _dbContext.Modules.Add(new ModuleEntity
                {
                    Pillar = module.Pillar,
                    ModuleName = module.Module,
                    Price = module.Price
                });
            }
        }

        await _dbContext.SaveChangesAsync();
        _cache = null;
    }

    private static ModuleItem ToModuleItem(ModuleEntity entity) => new()
    {
        Id = entity.Id,
        Pillar = entity.Pillar,
        Module = entity.ModuleName,
        ModuleName = entity.ModuleName,
        Price = entity.Price,
        HsnCode = entity.HsnCode,
        SacCode = entity.SacCode,
        ReverseChargeDefault = entity.ReverseChargeDefault,
        ImplementationEffortCost = entity.ImplementationEffortCost
    };
}
