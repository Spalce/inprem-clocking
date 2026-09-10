// BoldReports removed: controller will use plain file handling for report files if needed
using Microsoft.AspNetCore.Mvc;

namespace InpremClockingApp.Controllers;

[Route("api/[controller]/[action]")]
public class ReportingApiController : Controller
{
    private Microsoft.Extensions.Caching.Memory.IMemoryCache _cache;
    private IWebHostEnvironment _hostingEnvironment;

    public ReportingApiController(Microsoft.Extensions.Caching.Memory.IMemoryCache memoryCache, IWebHostEnvironment hostingEnvironment)
    {
        _cache = memoryCache;
        _hostingEnvironment = hostingEnvironment;
    }

    [NonAction]
    private string GetFilePath(string itemName, string key)
    {
        string dirPath = Path.Combine(_hostingEnvironment.WebRootPath + "\\" + "Cache", key);

        if (!Directory.Exists(dirPath))
        {
            Directory.CreateDirectory(dirPath);
        }

        return Path.Combine(dirPath, itemName);
    }

    public object GetImage(string key, string image)
    {
        // Previously used BoldReports helpers to serve images. Now serve file bytes directly from Cache folder.
        var path = GetFilePath(image, key);
        if (System.IO.File.Exists(path))
        {
            var bytes = System.IO.File.ReadAllBytes(path);
            return File(bytes, "application/octet-stream");
        }
        return NotFound();
    }


    [HttpPost]
    public IActionResult PostDesignerAction()
    {
        // Placeholder for designer actions. Without BoldReports, accept uploaded files and store in Cache.
        if (Request.Form?.Files?.Count > 0)
        {
            var file = Request.Form.Files[0];
            var filePath = GetFilePath(file.FileName, Guid.NewGuid().ToString());
            using (var fs = System.IO.File.Create(filePath))
            {
                file.CopyTo(fs);
            }
            return Ok(new { path = filePath });
        }
        return BadRequest();
    }

    // BoldReports-specific helpers removed. Controller now provides simple file upload and retrieval endpoints.

    [HttpPost]
    public IActionResult UploadReportAction()
    {
        if (Request.Form?.Files?.Count > 0)
        {
            var file = Request.Form.Files[0];
            var writePath = GetFilePath(file.FileName, Guid.NewGuid().ToString());
            using (var stream = System.IO.File.Create(writePath))
            {
                file.CopyTo(stream);
            }
            return Ok(new { path = writePath });
        }
        return BadRequest();
    }
}
