using System.Net;
using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;

namespace InpremClockingApp.Tests.Support.Hosting;

/// <summary>Signs in through the real Identity login page (GET the form, POST it back).</summary>
public static class SignIn
{
    public const string LoginPath = "/Identity/Account/Login";

    /// <summary>Posts the login form and returns the raw response (redirects are not followed).</summary>
    public static async Task<HttpResponseMessage> PostLoginAsync(
        HttpClient client, string email, string password, string? returnUrl = null)
    {
        var path = returnUrl == null ? LoginPath : $"{LoginPath}?returnUrl={Uri.EscapeDataString(returnUrl)}";

        using var page = await client.GetAsync(path).ConfigureAwait(false);
        page.EnsureSuccessStatusCode();
        var html = await page.Content.ReadAsStringAsync().ConfigureAwait(false);

        var document = new HtmlParser().ParseDocument(html);
        var form = document.Forms.FirstOrDefault(f => f.Method.Equals("post", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("Login page has no POST form.");

        var fields = new List<KeyValuePair<string, string>>();
        foreach (var hidden in form.Elements.OfType<IHtmlInputElement>().Where(i => i.Type == "hidden" && !string.IsNullOrEmpty(i.Name)))
        {
            fields.Add(new(hidden.Name!, hidden.Value));
        }
        fields.Add(new("Input.Email", email));
        fields.Add(new("Input.Password", password));
        fields.Add(new("Input.RememberMe", "false"));

        var action = string.IsNullOrEmpty(form.GetAttribute("action")) ? path : form.GetAttribute("action")!;
        return await client.PostAsync(action, new FormUrlEncodedContent(fields)).ConfigureAwait(false);
    }

    /// <summary>Signs in and fails loudly unless the app redirects (success); returns the redirect target.</summary>
    public static async Task<string> AsAsync(HttpClient client, string email, string password, string? returnUrl = null)
    {
        using var response = await PostLoginAsync(client, email, password, returnUrl).ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.Redirect || response.Headers.Location == null)
        {
            throw new InvalidOperationException(
                $"Sign-in as {email} did not redirect (status {(int)response.StatusCode}).");
        }
        return response.Headers.Location.OriginalString;
    }
}
