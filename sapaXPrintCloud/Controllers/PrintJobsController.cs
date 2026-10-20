using Microsoft.AspNetCore.Mvc;
using sapaXPrintCloud.Models;
using sapaXPrintCloud.Services;

namespace sapaXPrintCloud.Controllers
{
    [ApiController]
    [Route("api/print-jobs")]
    public class PrintJobsController : ControllerBase
    {
        // ==================================================
        // POST: SINGLE PrintNode-style API (PDF OR ZPL)
        // ==================================================
        [HttpPost]
        public IActionResult CreateJob([FromBody] CloudPrintJob job)
        {
            if (job == null)
                return BadRequest("Job payload required");

            if (string.IsNullOrWhiteSpace(job.PrinterId))
                return BadRequest("PrinterId required");

            if (string.IsNullOrWhiteSpace(job.FileBase64) &&
                string.IsNullOrWhiteSpace(job.Zpl))
                return BadRequest("Either FileBase64 or Zpl required");

            job.Id = Guid.NewGuid();
            job.Status = CloudPrintJobStatus.Queued;
            job.CreatedAt = DateTime.UtcNow;

            CloudPrintJobStore.Enqueue(job);

            return Ok(new
            {
                id = job.Id,
                printerId = job.PrinterId,
                fileName = job.FileName,
                fileBase64 = job.FileBase64,
                zpl = job.Zpl
            });
        }

        // ==================================================
        // GET: Agent pulls next job (PDF OR ZPL)
        // ==================================================
        [HttpGet("next")]
        public IActionResult GetNextJob(
        [FromHeader(Name = "X-Agent-Id")] string agentId)
        {
            if (string.IsNullOrWhiteSpace(agentId))
                return BadRequest("X-Agent-Id header required");

            var job = CloudPrintJobStore.Dequeue(agentId);

            if (job == null)
                return NoContent();

            return Ok(new
            {
                id = job.Id,
                printerId = job.PrinterId,
                fileName = job.FileName,
                fileBase64 = job.FileBase64,
                zpl = job.Zpl
            });
        }

        // ==================================================
        // POST: Job completion callback
        // ==================================================
        [HttpPost("{id}/complete")]
        public IActionResult CompleteJob(
            Guid id,
            [FromBody] JobCompletionDto result)
        {
            CloudPrintJobStore.Complete(id, result.Success, result.Error);
            return Ok();
        }

    }
}
