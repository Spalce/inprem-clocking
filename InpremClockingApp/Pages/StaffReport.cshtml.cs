using Microsoft.AspNetCore.Mvc.RazorPages;
using InpremClockingApp.Services;
using InpremClockingApp.Models;
using System.Threading.Tasks;

namespace InpremClockingApp.Pages;

public class StaffReport : PageModel
{
    private readonly StaffService _service;
    public List<InpremClockingApp.Models.Staff> StaffList { get; set; } = new List<InpremClockingApp.Models.Staff>();

    public StaffReport(StaffService service)
    {
        _service = service;
    }

    public async Task OnGetAsync()
    {
        StaffList = (await _service.GetAll().ConfigureAwait(false)).ToList();
    }
}
