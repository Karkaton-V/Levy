using System.Diagnostics;
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

    // The GitLab username, when the account signed in with GitLab.
    public string? GitLabUsername =>
        UserMetadata is not null && UserMetadata.TryGetValue("user_name", out var user) && user.ValueKind == JsonValueKind.String ? user.GetString() : null;
}

// A GitLab access token from "Sign in with GitLab", and when GitLab stops accepting it.
public sealed record GitLabToken(string AccessToken, DateTimeOffset ExpiresAt)
{
    public bool IsValid => ExpiresAt - DateTimeOffset.UtcNow > TimeSpan.FromMinutes(1);
}

// Where Levy's GitLab lives and which projects requests go to (Assets/Account/gitlab.json). Mock: a demo GitLab kept on this PC (Code/MockGitLab).
public sealed record GitLabSettings(string Url, int CallbackPort, string Scopes, Dictionary<string, string> Projects, bool Mock = false)
{
    public static GitLabSettings FromAssets() => JsonSerializer.Deserialize<GitLabSettings>(Shared.ReadAsset("Account", "gitlab.json"), SupabaseClient.Json)!;
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

    // GitLab's token for the signed-in person, while it lasts (GitLab tokens expire after about two hours; signing in with GitLab again renews it).
    public GitLabToken? GitLab { get; private set; } = TokenStore.LoadGitLab();
    public bool HasGitLab => GitLab?.IsValid == true;

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

    // Sign in with GitLab: the browser opens GitLab's sign-in (through Supabase), which comes back to a one-off local address with a code
    // that is exchanged for a session (PKCE, so no secret lives in the app). With onlyFor set, the code must belong to that user, so
    // reconnecting GitLab never swaps the signed-in account.
    public async Task SignInWithGitLabAsync(GitLabSettings settings, CancellationToken cancel, string? onlyFor = null)
    {
        var verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var callback = $"http://localhost:{settings.CallbackPort}/gitlab/";
        using var listener = new HttpListener();
        listener.Prefixes.Add(callback);
        try { listener.Start(); }
        catch (HttpListenerException) { throw new SupabaseException($"Port {settings.CallbackPort} is in use, so GitLab can't hand the sign-in back. Close whatever is using it and try again."); }

        var authorize = $"{_http.BaseAddress}auth/v1/authorize?provider=gitlab&redirect_to={Uri.EscapeDataString(callback)}" +
                        $"&code_challenge={challenge}&code_challenge_method=s256&scopes={Uri.EscapeDataString(settings.Scopes)}";
        await CheckProviderAsync(authorize);
        Process.Start(new ProcessStartInfo(authorize) { UseShellExecute = true });

        HttpListenerContext context;
        try { context = await listener.GetContextAsync().WaitAsync(TimeSpan.FromMinutes(5), cancel); }
        catch (TimeoutException) { throw new SupabaseException("GitLab sign-in timed out. Try again."); }
        var code = context.Request.QueryString["code"];
        var problem = context.Request.QueryString["error_description"];
        await Reply(context, code is null ? "Sign-in didn't finish. You can close this tab and try again in Levy." : "Signed in. You can close this tab and go back to Levy.");
        if (code is null) throw new SupabaseException(problem is { Length: > 0 } ? Sentence(problem.Replace('+', ' ')) : "GitLab sign-in was cancelled.");

        using var reply = await SendAsync(HttpMethod.Post, "auth/v1/token?grant_type=pkce", new { auth_code = code, code_verifier = verifier }, authorize: false);
        var json = await reply.Content.ReadFromJsonAsync<JsonElement>(Json);
        if (onlyFor is not null && json.GetProperty("user").GetProperty("id").GetString() != onlyFor)
            throw new SupabaseException("That GitLab account belongs to a different Levy account. Sign in to GitLab as yourself and try again.");
        Accept(json);
    }

    // Supabase answers the sign-in address with a redirect to GitLab when GitLab sign-in is set up, and an error when it isn't;
    // asking first means a missing setup shows here instead of as an error page in the browser.
    static async Task CheckProviderAsync(string authorize)
    {
        using var probe = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(15) };
        HttpResponseMessage reply;
        try { reply = await probe.GetAsync(authorize); }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException) { throw new SupabaseException("Couldn't reach the sign-in server. Check your internet connection and try again."); }
        using (reply)
        {
            if ((int)reply.StatusCode is >= 300 and < 400) return;
            var detail = await ErrorMessageAsync(reply);
            throw new SupabaseException(detail.Contains("not enabled", StringComparison.OrdinalIgnoreCase)
                ? "Sign in with GitLab isn't set up yet: turn on the GitLab provider in Supabase (Authentication → Sign In / Providers) with your GitLab server's address."
                : detail);
        }
    }

    static async Task Reply(HttpListenerContext context, string message)
    {
        var page = Encoding.UTF8.GetBytes($"<!DOCTYPE html><meta charset=utf-8><title>Levy</title><body style=\"font:16px Segoe UI,sans-serif;margin:3em\">{WebUtility.HtmlEncode(message)}</body>");
        context.Response.ContentType = "text/html; charset=utf-8";
        context.Response.ContentLength64 = page.Length;
        await context.Response.OutputStream.WriteAsync(page);
        context.Response.Close();
    }

    static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    // Signs back in with the refresh token saved at the last sign-in. Returns false when there is none or Supabase refuses it;
    // a network failure keeps the saved token for next time and is passed on to the caller.
    public async Task<bool> RestoreAsync()
    {
        var token = TokenStore.LoadSession();
        if (token is null) return false;
        try
        {
            await RefreshAsync(token);
            return true;
        }
        catch (SupabaseException e) when (e.IsRejection)
        {
            TokenStore.Clear();
            return false;
        }
    }

    // Signs out here; with everywhere, also ends the sessions on every other device.
    public async Task SignOutAsync(bool everywhere = false)
    {
        try
        {
            if (Session is not null) using (await SendAsync(HttpMethod.Post, everywhere ? "auth/v1/logout?scope=global" : "auth/v1/logout", null)) { }
        }
        catch (SupabaseException) { } // Signed out locally even if the server can't be reached.
        finally
        {
            Session = null;
            GitLab = null;
            TokenStore.Clear();
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
        TokenStore.SaveSession(Session.RefreshToken);
        // Only a GitLab sign-in carries GitLab's own token; Supabase doesn't send its expiry, and GitLab's tokens last two hours.
        if (json.TryGetProperty("provider_token", out var provider) && provider.ValueKind == JsonValueKind.String && provider.GetString() is { Length: > 0 } token)
        {
            GitLab = new GitLabToken(token, DateTimeOffset.UtcNow.AddHours(2));
            TokenStore.SaveGitLab(GitLab);
        }
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

    // Calls a function that returns nothing (rpc/{function}).
    public async Task CallAsync(string function, object? args = null)
    {
        using (await SendAsync(HttpMethod.Post, $"rest/v1/rpc/{function}", args ?? new { })) { }
    }

    // The company a sign-up code belongs to, or null; works signed out.
    public async Task<string?> CompanyForCodeAsync(string code)
    {
        using var reply = await SendAsync(HttpMethod.Post, "rest/v1/rpc/company_for_code", new { code }, authorize: false);
        var json = await reply.Content.ReadFromJsonAsync<JsonElement>(Json);
        return json.ValueKind == JsonValueKind.String ? json.GetString() : null;
    }

    // Sets a new password for the signed-in account.
    public async Task ChangePasswordAsync(string password)
    {
        using (await SendAsync(HttpMethod.Put, "auth/v1/user", new { password })) { }
    }

    // Storage -------------------------------------------------------------------------------------------------------------

    // Saves a file at bucket/path (replacing what's there); the bucket's policies decide who may.
    public async Task UploadAsync(string bucket, string path, byte[] bytes, string contentType)
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        using (await SendAsync(HttpMethod.Post, $"storage/v1/object/{bucket}/{path}", null, content: content, upsert: true)) { }
    }

    // A private file's bytes, or null when it isn't there or the caller may not see it.
    public async Task<byte[]?> DownloadAsync(string bucket, string path)
    {
        try
        {
            using var reply = await SendAsync(HttpMethod.Get, $"storage/v1/object/authenticated/{bucket}/{path}", null);
            return await reply.Content.ReadAsByteArrayAsync();
        }
        catch (SupabaseException e) when (e.IsRejection) { return null; }
    }

    public async Task RemoveAsync(string bucket, string path)
    {
        try { using (await SendAsync(HttpMethod.Delete, $"storage/v1/object/{bucket}/{path}", null)) { } }
        catch (SupabaseException e) when (e.IsRejection) { } // Already gone.
    }

    async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? body, bool authorize = true, bool representation = false,
        HttpContent? content = null, bool upsert = false)
    {
        if (authorize) await EnsureFreshAsync();
        using var request = new HttpRequestMessage(method, path);
        if (authorize) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Session!.AccessToken);
        if (representation) request.Headers.Add("Prefer", "return=representation");
        if (upsert) request.Headers.Add("x-upsert", "true");
        if (body is not null) request.Content = JsonContent.Create(body, body.GetType(), options: Json);
        else if (content is not null) request.Content = content;

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

    // The Supabase refresh token and the GitLab token, encrypted for the current Windows user (DPAPI) in %LOCALAPPDATA%\ValuationWorkbench.
    static class TokenStore
    {
        static readonly string Folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ValuationWorkbench");
        static readonly string SessionFile = Path.Combine(Folder, "session.bin");
        static readonly string GitLabFile = Path.Combine(Folder, "gitlab.bin");

        public static string? LoadSession() => Read(SessionFile);
        public static void SaveSession(string refreshToken) => Write(SessionFile, refreshToken);

        public static GitLabToken? LoadGitLab()
        {
            var parts = Read(GitLabFile)?.Split('\n');
            return parts is [var token, var expires] && long.TryParse(expires, out var unix) ? new GitLabToken(token, DateTimeOffset.FromUnixTimeSeconds(unix)) : null;
        }

        public static void SaveGitLab(GitLabToken token) => Write(GitLabFile, token.AccessToken + "\n" + token.ExpiresAt.ToUnixTimeSeconds());

        public static void Clear()
        {
            foreach (var file in new[] { SessionFile, GitLabFile })
                try { File.Delete(file); } catch (IOException) { }
        }

        static string? Read(string file)
        {
            try { return File.Exists(file) ? Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(file), null, DataProtectionScope.CurrentUser)) : null; }
            catch (Exception e) when (e is IOException or CryptographicException or UnauthorizedAccessException) { return null; }
        }

        static void Write(string file, string text)
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllBytes(file, ProtectedData.Protect(Encoding.UTF8.GetBytes(text), null, DataProtectionScope.CurrentUser));
        }
    }
}
