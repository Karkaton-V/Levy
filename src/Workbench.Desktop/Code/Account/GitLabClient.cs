using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Workbench.Desktop;

// A GitLab issue as Levy needs it: where it is and whether it's open.
public sealed record GitLabIssue(int Iid, string WebUrl, string State, IReadOnlyList<string> Labels, int Assignees);

// A merge request linked to an issue, with its latest pipeline result (null when there's no pipeline).
public sealed record GitLabMergeRequest(int Iid, string Title, string State, string WebUrl, bool Draft, string? Pipeline);

// GitLab's REST API (v4) on the agency's server, acting as the signed-in person with the token from "Sign in with GitLab".
public sealed class GitLabClient
{
    readonly HttpClient _http;

    public GitLabClient(GitLabSettings settings, GitLabToken token)
    {
        _http = new HttpClient { BaseAddress = new Uri(settings.Url.TrimEnd('/') + "/api/v4/"), Timeout = TimeSpan.FromSeconds(20) };
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
    }

    // Opens an issue in a project ("group/project") with labels such as "type::bug" or "client::Bossier Parish".
    public async Task<GitLabIssue> CreateIssueAsync(string project, string title, string description, IEnumerable<string> labels)
    {
        var body = new Dictionary<string, string> { ["title"] = title, ["description"] = description, ["labels"] = string.Join(',', labels) };
        using var reply = await SendAsync(HttpMethod.Post, $"projects/{Uri.EscapeDataString(project)}/issues", new FormUrlEncodedContent(body));
        return ReadIssue(await reply.Content.ReadFromJsonAsync<JsonElement>());
    }

    public async Task<GitLabIssue> GetIssueAsync(string project, int iid)
    {
        using var reply = await SendAsync(HttpMethod.Get, $"projects/{Uri.EscapeDataString(project)}/issues/{iid}");
        return ReadIssue(await reply.Content.ReadFromJsonAsync<JsonElement>());
    }

    // Merge requests that mention or close the issue, each with its head pipeline's status.
    public async Task<IReadOnlyList<GitLabMergeRequest>> MergeRequestsForAsync(string project, int iid)
    {
        using var reply = await SendAsync(HttpMethod.Get, $"projects/{Uri.EscapeDataString(project)}/issues/{iid}/related_merge_requests");
        var list = new List<GitLabMergeRequest>();
        foreach (var mr in (await reply.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray())
        {
            var pipeline = mr.TryGetProperty("head_pipeline", out var head) && head.ValueKind == JsonValueKind.Object ? head.GetProperty("status").GetString() : null;
            if (pipeline is null && mr.GetProperty("state").GetString() == "opened")
                pipeline = await PipelineOfAsync(mr.GetProperty("project_id").GetInt32(), mr.GetProperty("iid").GetInt32());
            list.Add(new GitLabMergeRequest(mr.GetProperty("iid").GetInt32(), mr.GetProperty("title").GetString() ?? "", mr.GetProperty("state").GetString() ?? "",
                mr.GetProperty("web_url").GetString() ?? "", mr.TryGetProperty("draft", out var draft) && draft.ValueKind == JsonValueKind.True, pipeline));
        }
        return list;
    }

    // The related-MR list leaves out pipelines; the single-MR call has them.
    async Task<string?> PipelineOfAsync(int projectId, int mergeRequest)
    {
        using var reply = await SendAsync(HttpMethod.Get, $"projects/{projectId}/merge_requests/{mergeRequest}");
        var json = await reply.Content.ReadFromJsonAsync<JsonElement>();
        return json.TryGetProperty("head_pipeline", out var head) && head.ValueKind == JsonValueKind.Object ? head.GetProperty("status").GetString() : null;
    }

    static GitLabIssue ReadIssue(JsonElement json) => new(
        json.GetProperty("iid").GetInt32(),
        json.GetProperty("web_url").GetString() ?? "",
        json.GetProperty("state").GetString() ?? "",
        json.TryGetProperty("labels", out var labels) ? labels.EnumerateArray().Select(l => l.GetString() ?? "").ToList() : [],
        json.TryGetProperty("assignees", out var assignees) ? assignees.GetArrayLength() : 0);

    async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, HttpContent? content = null)
    {
        using var request = new HttpRequestMessage(method, path) { Content = content };
        HttpResponseMessage reply;
        try { reply = await _http.SendAsync(request); }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            throw new SupabaseException("Couldn't reach GitLab. Check the connection to the GitLab server and try again.");
        }
        if (reply.IsSuccessStatusCode) return reply;
        using (reply)
        {
            throw reply.StatusCode switch
            {
                HttpStatusCode.Unauthorized => new SupabaseException("GitLab no longer accepts your sign-in. Press Connect GitLab and sign in again.", reply.StatusCode),
                HttpStatusCode.Forbidden => new SupabaseException("Your GitLab account can't do that in this project. Ask a maintainer for Reporter access or higher.", reply.StatusCode),
                HttpStatusCode.NotFound => new SupabaseException($"GitLab couldn't find {path.Split('/')[1].Replace("%2F", "/")}. Check the project paths in Assets/Account/gitlab.json.", reply.StatusCode),
                _ => new SupabaseException($"GitLab refused the request ({(int)reply.StatusCode} {reply.ReasonPhrase}).", reply.StatusCode),
            };
        }
    }
}
