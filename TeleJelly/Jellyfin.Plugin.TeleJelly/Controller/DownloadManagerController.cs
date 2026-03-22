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
    private readonly IDownloadOrchestrator _orchestrator;

    public DownloadManagerController(IDownloadOrchestrator orchestrator)
    {
        _orchestrator = orchestrator;
    }

    [HttpGet("downloads")]
    public ActionResult<IEnumerable<ManagedDownload>> GetDownloads()
    {
        return Ok(_orchestrator.GetAllDownloads().OrderByDescending(d => d.StartedAt));
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
