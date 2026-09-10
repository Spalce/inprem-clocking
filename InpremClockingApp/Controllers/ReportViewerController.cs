using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;

namespace InpremClockingApp.Controllers
{
    [Route("api/[controller]/[action]")]
    public class ReportViewerController : Controller
    {
        private IMemoryCache _cache;
        private IWebHostEnvironment _hostingEnvironment;

        public ReportViewerController(IMemoryCache memoryCache, IWebHostEnvironment hostingEnvironment)
        {
            _cache = memoryCache;
            _hostingEnvironment = hostingEnvironment;
        }

        // Minimal replacement for BoldReports PostReportAction.
        // Accepts a JSON body with a "reportPath" property and returns the RDL file bytes from wwwroot/Resources.
        [HttpPost]
        public IActionResult PostReportAction([FromBody] Dictionary<string, object> jsonArray)
        {
            if (jsonArray != null && jsonArray.TryGetValue("reportPath", out var rpObj) && rpObj is string rp && !string.IsNullOrWhiteSpace(rp))
            {
                var basePath = _hostingEnvironment.WebRootPath;
                var filePath = Path.Combine(basePath, "Resources", rp);
                if (System.IO.File.Exists(filePath))
                {
                    var bytes = System.IO.File.ReadAllBytes(filePath);
                    return File(bytes, "application/octet-stream", Path.GetFileName(filePath));
                }
                return NotFound();
            }
            return BadRequest();
        }

        [HttpPost]
        public IActionResult PostFormReportAction()
        {
            // No report processing available without BoldReports; return 501 Not Implemented to indicate the functionality was removed.
            return StatusCode(501, "Report processing removed. Use server-side report generation.");
        }
    }
}
