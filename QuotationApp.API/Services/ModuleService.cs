using System.Text.Json;
using Microsoft.Extensions.Options;
using QuotationApp.API.Models;

namespace QuotationApp.API.Services;

/// <summary>
/// Loads the master module ("Scope") list from Data/modules.json.
/// Swap this out for an EF Core / SQL-backed implementation later without touching callers -
/// only IModuleService is referenced elsewhere.
/// </summary>
public class ModuleService : IModuleService
{
    private readonly string _modulesFile;
    private List<ModuleItem>? _cache;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public ModuleService(IOptions<QuotationSettings> settings, IWebHostEnvironment env)
    {
        _modulesFile = Path.Combine(env.ContentRootPath, settings.Value.ModulesFile);
    }

    public async Task<List<ModuleItem>> GetModulesAsync()
    {
        if (_cache != null) return _cache;

        await _lock.WaitAsync();
        try
        {
            if (_cache != null) return _cache;

            if (!File.Exists(_modulesFile))
                throw new FileNotFoundException($"Module master list not found at '{_modulesFile}'.");

            var json = await File.ReadAllTextAsync(_modulesFile);
            _cache = JsonSerializer.Deserialize<List<ModuleItem>>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? new List<ModuleItem>();

            return _cache;
        }
        finally
        {
            _lock.Release();
        }
    }
}
