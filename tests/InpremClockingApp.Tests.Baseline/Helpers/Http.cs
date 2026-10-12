using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using UglyToad.PdfPig;
using Xunit;

namespace InpremClockingApp.Tests.Baseline.Helpers;

public static class Http
{
    public static async Task<IDocument> GetPageAsync(this HttpClient client, string path)
    {
        using var response = await client.GetAsync(path);
        Assert.True(response.StatusCode == HttpStatusCode.OK,
            $"GET {path} returned {(int)response.StatusCode} {response.Headers.Location}");
        return await response.ReadDocumentAsync();
    }

    public static async Task<IDocument> ReadDocumentAsync(this HttpResponseMessage response)
    {
        var html = await response.Content.ReadAsStringAsync();
        return new HtmlParser().ParseDocument(html);
    }

    public static async Task<JsonElement> ReadJsonAsync(this HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    public static async Task<JsonElement> GetJsonAsync(this HttpClient client, string path)
    {
        using var response = await client.GetAsync(path);
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"GET {path} returned {(int)response.StatusCode}");
        return await response.ReadJsonAsync();
    }

    /// <summary>Kiosk API answers are JSON strings, e.g. <c>"You have successfully clocked in"</c>.</summary>
    public static async Task<(HttpStatusCode Status, string Message)> GetMessageAsync(this HttpClient client, string path)
    {
        using var response = await client.GetAsync(path);
        var json = await response.ReadJsonAsync();
        var message = json.ValueKind == JsonValueKind.String ? json.GetString()! : json.GetRawText();
        return (response.StatusCode, message);
    }

    /// <summary>Same request shape the pages' fetch() calls send: a JSON body.</summary>
    public static Task<HttpResponseMessage> PostJsonAsync(this HttpClient client, string path, object body) =>
        client.PostAsync(path, new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"));

    /// <summary>Same request shape a plain HTML form submit sends.</summary>
    public static Task<HttpResponseMessage> PostFormAsync(this HttpClient client, string path, IDictionary<string, string> fields) =>
        client.PostAsync(path, new FormUrlEncodedContent(fields));

    /// <summary>Downloads a PDF, checks it is one, and returns its text (words joined by single spaces).</summary>
    public static async Task<string> GetPdfTextAsync(this HttpClient client, string path)
    {
        using var response = await client.GetAsync(path);
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"GET {path} returned {(int)response.StatusCode}");
        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
        return PdfText(await response.Content.ReadAsByteArrayAsync());
    }

    public static string PdfText(byte[] bytes)
    {
        Assert.True(bytes.Length > 4 && Encoding.ASCII.GetString(bytes, 0, 4) == "%PDF", "Response is not a PDF.");
        using var pdf = PdfDocument.Open(bytes);
        return string.Join(" ", pdf.GetPages().SelectMany(p => p.GetWords()).Select(w => w.Text));
    }

    /// <summary>Text of an element with whitespace collapsed, for stable comparisons.</summary>
    public static string CleanText(this IElement element) =>
        string.Join(" ", element.TextContent.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    /// <summary>The value cell next to a summary-table header cell (th) with the given text.</summary>
    public static string SummaryValue(this IDocument document, string header)
    {
        var th = document.QuerySelectorAll("th").FirstOrDefault(e => e.CleanText() == header)
            ?? throw new InvalidOperationException($"No <th> '{header}' on the page.");
        return th.ParentElement!.QuerySelector("td")!.CleanText();
    }
}
