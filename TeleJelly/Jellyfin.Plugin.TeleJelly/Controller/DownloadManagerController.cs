using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Jellyfin.Plugin.TeleJelly.Classes.Models;
using Jellyfin.Plugin.TeleJelly.Services.Download;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.TeleJelly.Controller;

[ApiController]
[Route("TeleJelly/DownloadManager")]
public class DownloadManagerController : ControllerBase
{
    private readonly IServiceHealthMonitor _healthMonitor;
    private readonly IDownloadOrchestrator _orchestrator;

    public DownloadManagerController(IDownloadOrchestrator orchestrator, IServiceHealthMonitor healthMonitor)
    {
        _orchestrator = orchestrator;
        _healthMonitor = healthMonitor;
    }

    [HttpGet("downloads")]
    public ActionResult<IEnumerable<ManagedDownload>> GetDownloads([FromQuery] string? status = null)
    {
        var downloads = _orchestrator.GetAllDownloads();
        if (!string.IsNullOrWhiteSpace(status) && !status.Equals("All", StringComparison.OrdinalIgnoreCase))
        {
            downloads = downloads.Where(d => d.Status.ToString().Equals(status, StringComparison.OrdinalIgnoreCase));
        }

        return Ok(downloads.OrderByDescending(d => d.StartedAt));
    }

    [HttpGet("health")]
    public ActionResult<IEnumerable<ServiceHealthStatus>> GetHealth()
    {
        return Ok(_healthMonitor.GetAllServiceHealth());
    }

    [HttpPost("downloads/{id}/cancel")]
    public async Task<IActionResult> CancelDownload(Guid id)
    {
        var download = _orchestrator.GetDownload(id);
        if (download == null)
        {
            return NotFound();
        }

        await _orchestrator.UpdateDownloadStatus(id, DownloadStatus.Canceled);
        return Ok();
    }

    [HttpDelete("downloads/{id}")]
    public IActionResult RemoveDownload(Guid id)
    {
        // In a real implementation, this would also clean up files.
        // For now, we'll just remove it from the orchestrator's list.
        // This method would need to be added to the orchestrator.
        // _orchestrator.RemoveDownload(id);
        return NoContent();
    }
}
