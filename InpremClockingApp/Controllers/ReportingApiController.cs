using BoldReports.Web.ReportDesigner;
using BoldReports.Web.ReportViewer;
using Microsoft.AspNetCore.Mvc;

namespace InpremClockingApp.Controllers;

[Route("api/[controller]/[action]")]
public class ReportingApiController : Controller, IReportDesignerController
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
        return ReportDesignerHelper.GetImage(key, image, this);
    }

    public object GetResource(ReportResource resource)
    {
        return ReportHelper.GetResource(resource, this, _cache);
    }

    [NonAction]
    public void OnInitReportOptions(ReportViewerOptions reportOption)
    {
        //You can update report options here
    }

    [NonAction]
    public void OnReportLoaded(ReportViewerOptions reportOption)
    {
        //You can update report options here
    }

    [HttpPost]
    public object PostDesignerAction([FromBody] Dictionary<string, object> jsonResult)
    {
        return ReportDesignerHelper.ProcessDesigner(jsonResult, this, null, _cache);
    }

    public object PostFormDesignerAction()
    {
        return ReportDesignerHelper.ProcessDesigner(null, this, null, _cache);
    }

    public object PostFormReportAction()
    {
        return ReportHelper.ProcessReport(null, this, _cache);
    }

    [HttpPost]
    public object PostReportAction([FromBody] Dictionary<string, object> jsonResult)
    {
        return ReportHelper.ProcessReport(jsonResult, this, _cache);
    }

    [NonAction]
    public bool SetData(string key, string itemId, ItemInfo itemData, out string errorMessage)
    {
        errorMessage = string.Empty;
        if (itemData.Data != null)
        {
            System.IO.File.WriteAllBytes(GetFilePath(itemId, key), itemData.Data);
        }
        else if (itemData.PostedFile != null)
        {
            var fileName = itemId;
            if (string.IsNullOrEmpty(itemId))
            {
                fileName = Path.GetFileName(itemData.PostedFile.FileName);
            }

            using (MemoryStream stream = new MemoryStream())
            {
                itemData.PostedFile.OpenReadStream().CopyTo(stream);
                byte[] bytes = stream.ToArray();
                var writePath = GetFilePath(fileName, key);

                System.IO.File.WriteAllBytes(writePath, bytes);
                stream.Close();
                stream.Dispose();
            }
        }
        return true;
    }
    [NonAction]
    public ResourceInfo GetData(string key, string itemId)
    {
        var resource = new ResourceInfo();
        try
        {
            var filePath = GetFilePath(itemId, key);
            if (itemId.Equals(Path.GetFileName(filePath), StringComparison.InvariantCultureIgnoreCase) && System.IO.File.Exists(filePath))
            {
                resource.Data = System.IO.File.ReadAllBytes(filePath);
            }
            else
            {
                resource.ErrorMessage = "File not found from the specified path";
            }
        }
        catch (Exception ex)
        {
            resource.ErrorMessage = ex.Message;
        }
        return resource;
    }

    [HttpPost]
    public void UploadReportAction()
    {
        ReportDesignerHelper.ProcessDesigner(null, this, Request.Form.Files[0], _cache);
    }
}
