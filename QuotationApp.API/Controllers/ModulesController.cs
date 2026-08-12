using Microsoft.AspNetCore.Mvc;
using QuotationApp.API.Models;
using QuotationApp.API.Services;

namespace QuotationApp.API.Controllers;

[ApiController]
[Route("api/modules")]
public class ModulesController : ControllerBase
{
    private readonly IModuleService _moduleService;

    public ModulesController(IModuleService moduleService) => _moduleService = moduleService;

    /// <summary>Master module ("Scope") list, grouped-ready for the checklist UI.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(List<ModuleItem>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<ModuleItem>>> GetAll()
        => Ok(await _moduleService.GetModulesAsync());
}
