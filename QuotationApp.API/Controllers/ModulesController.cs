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

    [HttpPost]
    public async Task<ActionResult<ModuleItem>> Create(ModuleUpsertRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Pillar) || string.IsNullOrWhiteSpace(request.ModuleName))
            return BadRequest("Pillar and ModuleName are required.");

        var module = await _moduleService.AddModuleAsync(request);
        return CreatedAtAction(nameof(GetAll), new { id = module.Id }, module);
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<ModuleItem>> Update(int id, ModuleUpsertRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Pillar) || string.IsNullOrWhiteSpace(request.ModuleName))
            return BadRequest("Pillar and ModuleName are required.");

        try
        {
            var module = await _moduleService.UpdateModuleAsync(id, request);
            return module is null ? NotFound() : Ok(module);
        }
        catch (InvalidOperationException exception)
        {
            return Conflict(new { error = exception.Message });
        }
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
        => await _moduleService.DeleteModuleAsync(id) ? NoContent() : NotFound();
}
