using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Jellyfin.Plugin.TeleJelly.Classes.Models;
using Jellyfin.Plugin.TeleJelly.Services.Download;
using Jellyfin.Plugin.TeleJelly.Services.Download.Health;
using Jellyfin.Plugin.TeleJelly.Services.Logging;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.TeleJelly.Controller;

[ApiController]
[Route("TeleJelly/DownloadManager")]
[Authorize(Policy = "RequiresElevation")]
public class DownloadManagerController : ControllerBase
{
    private readonly IServiceHealthMonitor _healthMonitor;
    private readonly IDownloadManagerLogStore _logStore;
    private readonly IDownloadOrchestrator _orchestrator;

    public DownloadManagerController(
        IDownloadOrchestrator orchestrator,
        IServiceHealthMonitor healthMonitor,
        IDownloadManagerLogStore logStore)
    {
        _orchestrator = orchestrator;
        _healthMonitor = healthMonitor;
        _logStore = logStore;
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

    [HttpGet("downloads/{id}")]
    public ActionResult<ManagedDownload> GetDownload(Guid id)
    {
        var download = _orchestrator.GetDownload(id);
        if (download == null)
        {
            return NotFound();
        }

        return Ok(download);
    }

    [HttpGet("health")]
    public ActionResult<IEnumerable<ServiceHealthStatus>> GetHealth()
    {
        return Ok(_healthMonitor.GetAllServiceHealth());
    }

    [HttpGet("logs")]
    public ActionResult<IEnumerable<DownloadManagerLogEntry>> GetLogs([FromQuery] int limit = 200)
    {
        return Ok(_logStore.GetRecent(limit));
    }

    [HttpPost("downloads/{id}/cancel")]
    public async Task<IActionResult> CancelDownload(Guid id)
    {
        var canceled = await _orchestrator.CancelDownloadAsync(id, HttpContext.RequestAborted);
        if (!canceled)
        {
            return NotFound();
        }

        return Ok();
    }

    [HttpPost("downloads/{id}/retry")]
    public async Task<IActionResult> RetryDownload(Guid id)
    {
        var retried = await _orchestrator.RetryDownloadAsync(id, HttpContext.RequestAborted);
        if (!retried)
        {
            return NotFound();
        }

        return Ok();
    }

    [HttpDelete("downloads/{id}")]
    public async Task<IActionResult> RemoveDownload(Guid id, [FromQuery] bool deleteFiles = false)
    {
        var removed = await _orchestrator.RemoveDownloadAsync(id, deleteFiles, HttpContext.RequestAborted);
        if (!removed)
        {
            return NotFound();
        }

        return NoContent();
    }
}
