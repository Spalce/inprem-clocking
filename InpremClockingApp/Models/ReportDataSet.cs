namespace InpremClockingApp.Models;

public class ReportDataSet<T>
{
    public bool Success { get; set; }
    public Detail? Detail { get; set; }
    public List<T>? Contents { get; set; }
}

public class Detail
{
    public string? Company { get; set; }
    public string? Address { get; set; }
    public string? Contact { get; set; }
    public string? Duration { get; set; }
}

public class Content
{
    public string? Name { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Gender { get; set; }
    public string? Date { get; set; }
    public string? Zip { get; set; }
    public string? Address { get; set; }
}

public class Clockings
{
    public string? Name { get; set; }
    public string? ClockIn { get; set; }
    public string? ClockOut { get; set; }
    public string? BreakStart { get; set; }
    public string? BreakEnd { get; set; }
    public string? Hours { get; set; }
    public string? Date { get; set; }
}
