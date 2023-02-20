using BoldReports.Models.ReportViewer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace InpremClockingApp.Pages;

public class VolunteerClockingReport : PageModel
{
    public IActionResult OnGet()
    {
        List<ReportParameter> parameters = new List<ReportParameter>();
        parameters.Add(new ReportParameter
        {
            Name = "StartDate",
            Values = new List<string> { "2016-01-01" }
        });
        parameters.Add(new ReportParameter
        {
            Name = "EndDate",
            Values = new List<string> { DateTime.Now.Date.ToString("yyyy-MM-dd") }
        });
        ViewData["parameters"] = parameters;
        return Page();
    }
}
