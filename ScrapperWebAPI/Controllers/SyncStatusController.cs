using Microsoft.AspNetCore.Mvc;
using ScrapperWebAPI.Services;

namespace ScrapperWebAPI.Controllers;

[Route("api/v1/[controller]")]
[ApiController]
public class SyncStatusController : ControllerBase
{
    [HttpGet]
    public IActionResult GetStatus()
    {
        var status = ProductSyncManager.GetCurrentStatus();
        return Ok(status);
    }

    [HttpGet("results")]
    public IActionResult GetLastResults()
    {
        var results = ProductSyncManager.GetLastResults();
        return Ok(results);
    }
}