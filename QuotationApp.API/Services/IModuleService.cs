using QuotationApp.API.Models;

namespace QuotationApp.API.Services;

public interface IModuleService
{
    Task<List<ModuleItem>> GetModulesAsync();
}
