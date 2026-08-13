using QuotationApp.API.Models;

namespace QuotationApp.API.Services;

public interface IModuleService
{
    Task<List<ModuleItem>> GetModulesAsync();
    Task<ModuleItem> AddModuleAsync(ModuleUpsertRequest request);
    Task<ModuleItem?> UpdateModuleAsync(int id, ModuleUpsertRequest request);
    Task<bool> DeleteModuleAsync(int id);
}
