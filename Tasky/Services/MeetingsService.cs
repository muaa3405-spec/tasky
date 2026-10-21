using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Identity.Client;
using Tasky.Models;

namespace Tasky.Services;

public class MeetingsService
{
    private static readonly string[] Scopes    = ["https://graph.microsoft.com/Calendars.Read"];
    private const           string   GraphBase = "https://graph.microsoft.com/v1.0";

    private IPublicClientApplication? _msal;
    private IAccount?                 _account;
    private readonly HttpClient       _http = new();
    private readonly string           _cachePath;

    public bool   IsConfigured => !string.IsNullOrWhiteSpace(App.Settings.Current.MsClientId);
    public bool   IsSignedIn   => _account != null;
    public string SignedInUser  => _account?.Username ?? "";

    public MeetingsService()
    {
        _cachePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Tasky", "ms_token.bin");
    }

    private IPublicClientApplication EnsureMsal()
    {
        if (_msal != null) return _msal;
        _msal = PublicClientApplicationBuilder
            .Create(App.Settings.Current.MsClientId.Trim())
            .WithAuthority("https://login.microsoftonline.com/consumers")
            .WithRedirectUri("http://localhost")
            .Build();
        RegisterCache(_msal.UserTokenCache);
        return _msal;
    }

    // Called on page open — restores a previous sign-in without showing a browser.
    public async Task<bool> TrySilentAsync()
    {
        if (!IsConfigured) return false;
        try
        {
            var app      = EnsureMsal();
            var accounts = (await app.GetAccountsAsync()).ToList();
            if (accounts.Count == 0) return false;
            _account = accounts[0];
            var result = await app.AcquireTokenSilent(Scopes, _account).ExecuteAsync();
            SetToken(result.AccessToken);
            return true;
        }
        catch { _account = null; return false; }
    }

    // Opens the system browser for interactive Microsoft sign-in.
    public async Task SignInAsync()
    {
        var result = await EnsureMsal()
            .AcquireTokenInteractive(Scopes)
            .WithUseEmbeddedWebView(false)
            .ExecuteAsync();
        _account = result.Account;
        SetToken(result.AccessToken);
    }

    public async Task SignOutAsync()
    {
        if (_msal != null)
            foreach (var a in await _msal.GetAccountsAsync())
                await _msal.RemoveAsync(a);
        _account = null;
        _http.DefaultRequestHeaders.Authorization = null;
        if (File.Exists(_cachePath)) File.Delete(_cachePath);
        // Reset so a new client ID can be used without restarting
        _msal = null;
    }

    // Returns events in [fromLocal, toLocal) converted to local time.
    public async Task<List<MeetingItem>> GetMeetingsAsync(DateTime fromLocal, DateTime toLocal)
    {
        if (!IsSignedIn) return [];
        if (!await RefreshTokenAsync()) return [];

        var from = fromLocal.ToUniversalTime();
        var to   = toLocal.ToUniversalTime();
        var url  = $"{GraphBase}/me/calendarview"
                 + $"?startDateTime={from:yyyy-MM-ddTHH:mm:ssZ}"
                 + $"&endDateTime={to:yyyy-MM-ddTHH:mm:ssZ}"
                 + "&$select=id,subject,start,end,organizer,isOnlineMeeting,onlineMeeting"
                 + "&$orderby=start/dateTime"
                 + "&$top=100";
        try
        {
            var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Add("Prefer", "outlook.timezone=\"UTC\"");
            var resp = await _http.SendAsync(req);
            if (!resp.IsSuccessStatusCode) return [];

            var json = await resp.Content.ReadAsStringAsync();
            var data = JsonSerializer.Deserialize<GraphCalendarResponse>(json,
                           new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            var list = new List<MeetingItem>();
            foreach (var ev in data?.Value ?? [])
            {
                if (!DateTime.TryParse(ev.Start?.DateTime, out var s)) continue;
                if (!DateTime.TryParse(ev.End?.DateTime,   out var e)) continue;
                var start = TimeZoneInfo.ConvertTimeFromUtc(
                    DateTime.SpecifyKind(s, DateTimeKind.Utc), TimeZoneInfo.Local);
                var end = TimeZoneInfo.ConvertTimeFromUtc(
                    DateTime.SpecifyKind(e, DateTimeKind.Utc), TimeZoneInfo.Local);
                list.Add(new MeetingItem
                {
                    Id            = ev.Id ?? "",
                    Subject       = string.IsNullOrWhiteSpace(ev.Subject) ? "(No title)" : ev.Subject,
                    Start         = start,
                    End           = end,
                    JoinUrl       = ev.OnlineMeeting?.JoinUrl,
                    OrganizerName = ev.Organizer?.EmailAddress?.Name ?? ""
                });
            }
            return list;
        }
        catch { return []; }
    }

    private void SetToken(string token) =>
        _http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);

    private async Task<bool> RefreshTokenAsync()
    {
        if (_account == null) return false;
        try
        {
            var result = await EnsureMsal()
                .AcquireTokenSilent(Scopes, _account)
                .ExecuteAsync();
            SetToken(result.AccessToken);
            return true;
        }
        catch (MsalUiRequiredException) { _account = null; return false; }
        catch { return false; }
    }

    private void RegisterCache(ITokenCache cache)
    {
        // The token cache holds a Microsoft refresh token, so it is stored encrypted
        // for the current Windows user (DPAPI) rather than as a plain file.
        cache.SetBeforeAccess(args =>
        {
            try
            {
                if (!File.Exists(_cachePath)) return;
                var stored = File.ReadAllBytes(_cachePath);
                // Files written by older versions are unencrypted; they are re-saved encrypted on next change.
                var plain  = Helpers.UserDataProtector.TryUnprotect(stored) ?? stored;
                args.TokenCache.DeserializeMsalV3(plain);
            }
            catch { }
        });
        cache.SetAfterAccess(args =>
        {
            try
            {
                if (args.HasStateChanged)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(_cachePath)!);
                    File.WriteAllBytes(_cachePath,
                        Helpers.UserDataProtector.Protect(args.TokenCache.SerializeMsalV3()));
                }
            }
            catch { }
        });
    }

    // ── Graph API JSON POCOs ─────────────────────────────────────────────────
    private sealed record GraphCalendarResponse(
        [property: JsonPropertyName("value")] List<GraphEvent>? Value);
    private sealed record GraphEvent(
        [property: JsonPropertyName("id")]             string? Id,
        [property: JsonPropertyName("subject")]        string? Subject,
        [property: JsonPropertyName("isOnlineMeeting")]bool    IsOnlineMeeting,
        [property: JsonPropertyName("start")]          GraphDt? Start,
        [property: JsonPropertyName("end")]            GraphDt? End,
        [property: JsonPropertyName("organizer")]      GraphOrganizer?     Organizer,
        [property: JsonPropertyName("onlineMeeting")]  GraphOnlineMeeting? OnlineMeeting);
    private sealed record GraphDt(
        [property: JsonPropertyName("dateTime")] string? DateTime,
        [property: JsonPropertyName("timeZone")] string? TimeZone);
    private sealed record GraphOrganizer(
        [property: JsonPropertyName("emailAddress")] GraphEmail? EmailAddress);
    private sealed record GraphEmail(
        [property: JsonPropertyName("name")]    string? Name,
        [property: JsonPropertyName("address")] string? Address);
    private sealed record GraphOnlineMeeting(
        [property: JsonPropertyName("joinUrl")] string? JoinUrl);
}
