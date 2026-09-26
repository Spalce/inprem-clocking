using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using InpremClockingApp.Services;
using InpremClockingApp.Models;
using System.Threading.Tasks;

namespace InpremClockingApp.Pages;

public class StaffReport : PageModel
{
    private readonly StaffService _service;
    public List<InpremClockingApp.Models.Staff> StaffList { get; set; } = new List<InpremClockingApp.Models.Staff>();

    // Named "pageNumber" rather than "page" because "page" is a reserved Razor Pages route
    // value (the page's own path) - a query string "page" is intercepted by route-value model
    // binding before it ever reaches a same-named property.
    [BindProperty(SupportsGet = true)]
    public int PageNumber { get; set; } = 1;

    [BindProperty(SupportsGet = true)]
    public int PageSize { get; set; } = 20;

    public StaffReport(StaffService service)
    {
        _service = service;
    }

    public async Task OnGetAsync()
    {
        var result = await _service.SearchByName(null, PageNumber, PageSize).ConfigureAwait(false);
        StaffList = result.Items.ToList();

        ViewData["TotalCount"] = result.TotalCount;
        ViewData["Page"] = result.Page;
        ViewData["PageSize"] = result.PageSize;
        ViewData["TotalPages"] = result.TotalPages;
    }
}
