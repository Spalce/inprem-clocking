namespace InpremClockingApp.Models;

public class ReportModel
{
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public long Id { get; set; }
    public string? Url { get; set; }
    public string? Title { get; set; }
}
