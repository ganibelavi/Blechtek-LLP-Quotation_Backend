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

        try
        {
            var module = await _moduleService.AddModuleAsync(request);
            return CreatedAtAction(nameof(GetAll), new { id = module.Id }, module);
        }
        catch (Microsoft.EntityFrameworkCore.DbUpdateException)
        {
            // Likely a unique constraint violation on ModuleName - return a friendly conflict response
            return Conflict(new { error = "A module with this name already exists." });
        }
        catch (Exception)
        {
            // Unexpected error - return a generic server error message
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = "Could not create the module in the database." });
        }
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
