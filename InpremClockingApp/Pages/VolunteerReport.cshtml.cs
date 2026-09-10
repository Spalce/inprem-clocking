using Microsoft.AspNetCore.Mvc.RazorPages;
using InpremClockingApp.Services;
using InpremClockingApp.Models;
using System.Threading.Tasks;

namespace InpremClockingApp.Pages;

public class VolunteerReport : PageModel
{
    private readonly VolunteerService _service;
    public List<InpremClockingApp.Models.Volunteer> VolunteerList { get; set; } = new List<InpremClockingApp.Models.Volunteer>();

    public VolunteerReport(VolunteerService service)
    {
        _service = service;
    }

    public async Task OnGetAsync()
    {
        VolunteerList = (await _service.GetAll().ConfigureAwait(false)).ToList();
    }
}