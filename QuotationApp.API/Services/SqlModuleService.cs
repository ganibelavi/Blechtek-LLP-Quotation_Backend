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
                    Pillar = m.Pillar,
                    Module = m.ModuleName
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
    public async Task AddModuleAsync(string pillar, string moduleName)
    {
        var entity = new ModuleEntity
        {
            Pillar = pillar,
            ModuleName = moduleName
        };

        _dbContext.Modules.Add(entity);
        await _dbContext.SaveChangesAsync();
        
        // Invalidate cache
        _cache = null;
    }

    /// <summary>
    /// Updates an existing module.
    /// </summary>
    public async Task UpdateModuleAsync(int id, string pillar, string moduleName)
    {
        var entity = await _dbContext.Modules.FindAsync(id);
        if (entity == null) throw new KeyNotFoundException($"Module with id {id} not found");

        entity.Pillar = pillar;
        entity.ModuleName = moduleName;
        await _dbContext.SaveChangesAsync();
        
        // Invalidate cache
        _cache = null;
    }

    /// <summary>
    /// Deletes a module from the database.
    /// </summary>
    public async Task DeleteModuleAsync(int id)
    {
        var entity = await _dbContext.Modules.FindAsync(id);
        if (entity == null) throw new KeyNotFoundException($"Module with id {id} not found");

        _dbContext.Modules.Remove(entity);
        await _dbContext.SaveChangesAsync();
        
        // Invalidate cache
        _cache = null;
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
                    ModuleName = module.Module
                });
            }
        }

        await _dbContext.SaveChangesAsync();
        _cache = null;
    }
}