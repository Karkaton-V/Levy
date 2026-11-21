using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Workbench.Desktop;

// The signed-in user, from GoTrue's user object; the name comes from the full_name given at sign-up.
public sealed record AuthUser(string Id, string? Email, Dictionary<string, JsonElement>? UserMetadata)
{
    public string FullName =>
        UserMetadata is not null && UserMetadata.TryGetValue("full_name", out var name) && name.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(name.GetString())
            ? name.GetString()!.Trim()
            : Email?.Split('@')[0] ?? "";
}

public sealed record AuthSession(string AccessToken, string RefreshToken, DateTimeOffset ExpiresAt, AuthUser User);

// A refusal from Supabase (wrong password, rule broken) or a failure to reach it, with a message fit to show the user.
public sealed class SupabaseException(string message, HttpStatusCode? status = null) : Exception(message)
{
    public HttpStatusCode? Status { get; } = status;
    public bool IsRejection => Status is >= HttpStatusCode.BadRequest and < HttpStatusCode.InternalServerError;
}

// Plain HttpClient against Supabase Auth (GoTrue) and the REST API (PostgREST), using the URL and publishable key in Assets/Account/supabase.json.
// Rows are sent and read as snake_case JSON; DateOnly properties go out as yyyy-MM-dd.
public sealed class SupabaseClient
{
    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    readonly HttpClient _http;
    readonly SemaphoreSlim _refreshing = new(1, 1);

    public AuthSession? Session { get; private set; }
    public AuthUser? User => Session?.User;

    public SupabaseClient(string url, string publishableKey)
    {
        _http = new HttpClient { BaseAddress = new Uri(url.TrimEnd('/') + "/"), Timeout = TimeSpan.FromSeconds(20) };
        _http.DefaultRequestHeaders.Add("apikey", publishableKey);
    }

    public static SupabaseClient FromAssets()
    {
        using var config = JsonDocument.Parse(Shared.ReadAsset("Account", "supabase.json"));
        return new(config.RootElement.GetProperty("url").GetString()!, config.RootElement.GetProperty("publishableKey").GetString()!);
    }

    // Auth ---------------------------------------------------------------------------------------------------------

    // Creates the account; with a company code it asks to join that company and waits for an admin. Returns false when Supabase wants the email confirmed first.
    public async Task<bool> SignUpAsync(string fullName, string email, string password, string? companyCode = null)
    {
        var body = new { email, password, data = new { full_name = fullName, company_code = string.IsNullOrWhiteSpace(companyCode) ? null : companyCode.Trim() } };
        using var reply = await SendAsync(HttpMethod.Post, "auth/v1/signup", body, authorize: false);
        var json = await reply.Content.ReadFromJsonAsync<JsonElement>(Json);
        if (!json.TryGetProperty("access_token", out _)) return false;
        Accept(json);
        return true;
    }

    public async Task SignInAsync(string email, string password)
    {
        using var reply = await SendAsync(HttpMethod.Post, "auth/v1/token?grant_type=password", new { email, password }, authorize: false);
        Accept(await reply.Content.ReadFromJsonAsync<JsonElement>(Json));
    }

    // Signs back in with the refresh token saved at the last sign-in. Returns false when there is none or Supabase refuses it;
    // a network failure keeps the saved token for next time and is passed on to the caller.
    public async Task<bool> RestoreAsync()
    {
        var token = SessionStore.Load();
        if (token is null) return false;
        try
        {
            await RefreshAsync(token);
            return true;
        }
        catch (SupabaseException e) when (e.IsRejection)
        {
            SessionStore.Clear();
            return false;
        }
    }

    public async Task SignOutAsync()
    {
        try
        {
            if (Session is not null) using (await SendAsync(HttpMethod.Post, "auth/v1/logout", null)) { }
        }
        catch (SupabaseException) { } // Signed out locally even if the server can't be reached.
        finally
        {
            Session = null;
            SessionStore.Clear();
        }
    }

    async Task RefreshAsync(string refreshToken)
    {
        using var reply = await SendAsync(HttpMethod.Post, "auth/v1/token?grant_type=refresh_token", new { refresh_token = refreshToken }, authorize: false);
        Accept(await reply.Content.ReadFromJsonAsync<JsonElement>(Json));
    }

    // Refreshes the access token when it expires within a minute.
    async Task EnsureFreshAsync()
    {
        if (Session is null) throw new SupabaseException("You are signed out. Sign in again to continue.", HttpStatusCode.Unauthorized);
        if (Session.ExpiresAt - DateTimeOffset.UtcNow > TimeSpan.FromSeconds(60)) return;
        await _refreshing.WaitAsync();
        try
        {
            if (Session.ExpiresAt - DateTimeOffset.UtcNow <= TimeSpan.FromSeconds(60)) await RefreshAsync(Session.RefreshToken);
        }
        finally { _refreshing.Release(); }
    }

    void Accept(JsonElement json)
    {
        var expiresAt = json.TryGetProperty("expires_at", out var at) && at.ValueKind == JsonValueKind.Number
            ? DateTimeOffset.FromUnixTimeSeconds(at.GetInt64())
            : DateTimeOffset.UtcNow.AddSeconds(json.GetProperty("expires_in").GetInt32());
        Session = new AuthSession(
            json.GetProperty("access_token").GetString()!,
            json.GetProperty("refresh_token").GetString()!,
            expiresAt,
            json.GetProperty("user").Deserialize<AuthUser>(Json)!);
        SessionStore.Save(Session.RefreshToken);
    }

    // Rows -----------------------------------------------------------------------------------------------------------

    // GET /rest/v1/{table}?{query}, e.g. GetAsync<Job>("jobs", "select=*&order=due_date").
    public async Task<List<T>> GetAsync<T>(string table, string query = "select=*")
    {
        using var reply = await SendAsync(HttpMethod.Get, $"rest/v1/{table}?{query}", null);
        return await reply.Content.ReadFromJsonAsync<List<T>>(Json) ?? [];
    }

    // Adds a row and returns it as stored, with its generated id and defaults.
    public async Task<T> InsertAsync<T>(string table, object row)
    {
        using var reply = await SendAsync(HttpMethod.Post, $"rest/v1/{table}", row, representation: true);
        return (await reply.Content.ReadFromJsonAsync<List<T>>(Json))!.Single();
    }

    // Changes the given columns of one row and returns it as stored.
    public async Task<T> UpdateAsync<T>(string table, Guid id, object changes)
    {
        using var reply = await SendAsync(HttpMethod.Patch, $"rest/v1/{table}?id=eq.{id}", changes, representation: true);
        var rows = await reply.Content.ReadFromJsonAsync<List<T>>(Json);
        return rows is { Count: 1 } ? rows[0] : throw new SupabaseException("That record no longer exists, or you can no longer edit it.");
    }

    // POST /rest/v1/rpc/{function} with named arguments; returns its rows. Functions check who is calling themselves.
    public async Task<List<T>> RpcAsync<T>(string function, object? args = null)
    {
        using var reply = await SendAsync(HttpMethod.Post, $"rest/v1/rpc/{function}", args ?? new { });
        return await reply.Content.ReadFromJsonAsync<List<T>>(Json) ?? [];
    }

    // The company a sign-up code belongs to, or null; works signed out.
    public async Task<string?> CompanyForCodeAsync(string code)
    {
        using var reply = await SendAsync(HttpMethod.Post, "rest/v1/rpc/company_for_code", new { code }, authorize: false);
        var json = await reply.Content.ReadFromJsonAsync<JsonElement>(Json);
        return json.ValueKind == JsonValueKind.String ? json.GetString() : null;
    }

    async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? body, bool authorize = true, bool representation = false)
    {
        if (authorize) await EnsureFreshAsync();
        using var request = new HttpRequestMessage(method, path);
        if (authorize) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Session!.AccessToken);
        if (representation) request.Headers.Add("Prefer", "return=representation");
        if (body is not null) request.Content = JsonContent.Create(body, body.GetType(), options: Json);

        HttpResponseMessage reply;
        try { reply = await _http.SendAsync(request); }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            throw new SupabaseException("Couldn't reach the server. Check your internet connection and try again.");
        }
        if (reply.IsSuccessStatusCode) return reply;
        using (reply) throw new SupabaseException(await ErrorMessageAsync(reply), reply.StatusCode);
    }

    // GoTrue says msg or error_description, PostgREST says message; fall back to the status.
    static async Task<string> ErrorMessageAsync(HttpResponseMessage reply)
    {
        try
        {
            var json = await reply.Content.ReadFromJsonAsync<JsonElement>();
            foreach (var key in new[] { "msg", "error_description", "message", "error" })
                if (json.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString()))
                    return Sentence(value.GetString()!);
        }
        catch (JsonException) { }
        return $"The server refused the request ({(int)reply.StatusCode} {reply.ReasonPhrase}).";
    }

    static string Sentence(string text) => text.EndsWith('.') ? text : text + ".";

    // The refresh token, encrypted for the current Windows user (DPAPI) in %LOCALAPPDATA%\ValuationWorkbench\session.bin.
    static class SessionStore
    {
        static readonly string FilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ValuationWorkbench", "session.bin");

        public static string? Load()
        {
            try { return File.Exists(FilePath) ? Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(FilePath), null, DataProtectionScope.CurrentUser)) : null; }
            catch (Exception e) when (e is IOException or CryptographicException or UnauthorizedAccessException) { return null; }
        }

        public static void Save(string refreshToken)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllBytes(FilePath, ProtectedData.Protect(Encoding.UTF8.GetBytes(refreshToken), null, DataProtectionScope.CurrentUser));
        }

        public static void Clear()
        {
            try { File.Delete(FilePath); } catch (IOException) { }
        }
    }
}
