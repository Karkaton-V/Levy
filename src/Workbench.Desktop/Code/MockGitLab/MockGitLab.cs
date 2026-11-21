using System.IO;
using System.Text.Json;

namespace Workbench.Desktop;

// One issue in the demo GitLab.
public sealed class MockIssue
{
    public int Iid { get; set; }
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public List<string> Labels { get; set; } = [];
    public string State { get; set; } = "opened";
    public string? Assignee { get; set; }
    public string Author { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
}

// One merge request in the demo GitLab: always closes one issue, starts as a draft, and merges only when GitLab's rules allow.
public sealed class MockMergeRequest
{
    public int Iid { get; set; }
    public int ClosesIssue { get; set; }
    public string Title { get; set; } = "";
    public string SourceBranch { get; set; } = "";
    public string Author { get; set; } = "";
    public string State { get; set; } = "opened";
    public bool Draft { get; set; } = true;
    public string? Pipeline { get; set; }
    public List<string> ApprovedBy { get; set; } = [];
}

public sealed class MockProject
{
    public string Path { get; set; } = "";
    public int NextIssue { get; set; } = 1;
    public int NextMergeRequest { get; set; } = 1;
    public List<MockIssue> Issues { get; set; } = [];
    public List<MockMergeRequest> MergeRequests { get; set; } = [];
}

// Everything the demo GitLab holds, saved as one file.
public sealed class MockGitLabState
{
    public List<string> People { get; set; } = [];
    public int RequiredApprovals { get; set; } = 1;
    public List<MockProject> Projects { get; set; } = [];
}

// A stand-in for a self-managed GitLab, kept in %LOCALAPPDATA%\ValuationWorkbench\mock-gitlab.json and started from Assets/MockGitLab/seed.json.
// It answers Levy like the real API (IGitLab) and lets the Mock GitLab window play developer and reviewer: assign, start, open a
// merge request, run the pipeline, mark ready, approve and merge. Merging follows GitLab's usual project rules: the pipeline must pass,
// the merge request can't be a draft, and it needs approvals from someone other than its author.
public sealed class MockGitLab : IGitLab
{
    static readonly string FilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ValuationWorkbench", "mock-gitlab.json");
    static readonly JsonSerializerOptions Json = new(SupabaseClient.Json) { WriteIndented = true };

    readonly string _url;
    MockGitLabState _state;

    public MockGitLab(GitLabSettings settings)
    {
        _url = settings.Url.TrimEnd('/');
        _state = Load();
        foreach (var path in settings.Projects.Values) Project(path);
    }

    // Raised after any change, so Levy's Intake page can pull the new status.
    public event Action? Changed;

    public IReadOnlyList<string> People => _state.People;
    public int RequiredApprovals => _state.RequiredApprovals;
    public IReadOnlyList<MockProject> Projects => _state.Projects;
    public string Host => new Uri(_url).Host;

    public string IssueUrl(string project, int iid) => $"{_url}/{project}/-/issues/{iid}";
    public string MergeRequestUrl(string project, int iid) => $"{_url}/{project}/-/merge_requests/{iid}";

    // A link Levy shows ("https://gitlab.mock/group/project/-/issues/7") read back as project and issue, for opening the Mock GitLab window.
    public bool TryParseIssueUrl(Uri link, out string project, out int iid)
    {
        project = ""; iid = 0;
        if (!link.AbsoluteUri.StartsWith(_url + "/", StringComparison.OrdinalIgnoreCase)) return false;
        var parts = link.AbsolutePath.Trim('/').Split("/-/issues/");
        if (parts.Length != 2 || !int.TryParse(parts[1], out iid)) return false;
        project = parts[0];
        return true;
    }

    // IGitLab: what Levy's Intake page uses. ------------------------------------------------------------------------------

    public Task<GitLabIssue> CreateIssueAsync(string project, string title, string description, IEnumerable<string> labels)
    {
        var p = Project(project);
        var issue = new MockIssue { Iid = p.NextIssue++, Title = title, Description = description, Labels = labels.ToList(), Author = "Levy intake" };
        p.Issues.Add(issue);
        Save();
        return Task.FromResult(ToGitLab(project, issue));
    }

    public Task<GitLabIssue> GetIssueAsync(string project, int iid) => Task.FromResult(ToGitLab(project, Issue(project, iid)));

    public Task<IReadOnlyList<GitLabMergeRequest>> MergeRequestsForAsync(string project, int iid) =>
        Task.FromResult<IReadOnlyList<GitLabMergeRequest>>(Project(project).MergeRequests.Where(m => m.ClosesIssue == iid)
            .Select(m => new GitLabMergeRequest(m.Iid, m.Title, m.State, MergeRequestUrl(project, m.Iid), m.Draft, m.Pipeline)).ToList());

    // Developer and reviewer actions, from the Mock GitLab window. Each returns null when it worked, or why it can't. -----------------

    public string? Assign(string project, int iid, string person) => Change(() => Issue(project, iid).Assignee = person);

    // Starting work adds the workflow::doing label (what a team's board would use), which Levy reads as "In progress".
    public string? StartWork(string project, int iid) => Change(() =>
    {
        var issue = Issue(project, iid);
        if (!issue.Labels.Contains("workflow::doing")) issue.Labels.Add("workflow::doing");
    });

    public string? CloseIssue(string project, int iid) => Change(() => Issue(project, iid).State = "closed");

    public string? ReopenIssue(string project, int iid) => Change(() => Issue(project, iid).State = "opened");

    // A draft merge request on a branch named after the issue, whose description closes it.
    public string? OpenMergeRequest(string project, int iid, string author)
    {
        var issue = Issue(project, iid);
        if (issue.State != "opened") return "Reopen the issue before starting a merge request for it.";
        var p = Project(project);
        if (p.MergeRequests.Any(m => m.ClosesIssue == iid && m.State == "opened")) return "This issue already has an open merge request.";
        p.MergeRequests.Add(new MockMergeRequest
        {
            Iid = p.NextMergeRequest++, ClosesIssue = iid, Author = author,
            Title = $"Resolve \"{issue.Title}\"", SourceBranch = $"{iid}-{Slug(issue.Title)}",
        });
        issue.Assignee ??= author;
        return Change(() => { });
    }

    // A push: the pipeline runs, then finishes (the window waits a moment in between, like a real runner).
    public string? StartPipeline(string project, int mergeRequest) => Change(() => OpenMergeRequestOrThrow(project, mergeRequest).Pipeline = "running");

    public string? FinishPipeline(string project, int mergeRequest, bool passed) => Change(() =>
    {
        var mr = MergeRequest(project, mergeRequest);
        mr.Pipeline = passed ? "success" : "failed";
        mr.ApprovedBy.Clear(); // New commits reset approvals, as most review rules require.
    });

    public string? MarkReady(string project, int mergeRequest) => Change(() => OpenMergeRequestOrThrow(project, mergeRequest).Draft = false);

    public string? Approve(string project, int mergeRequest, string reviewer)
    {
        var mr = MergeRequest(project, mergeRequest);
        if (mr.State != "opened") return "Only open merge requests can be approved.";
        if (mr.Author == reviewer) return "Authors can't approve their own merge request. Switch \"Acting as\" to a reviewer.";
        if (mr.ApprovedBy.Contains(reviewer)) return $"{reviewer} has already approved it.";
        return Change(() => mr.ApprovedBy.Add(reviewer));
    }

    // Merges when the rules allow, which closes the issue it resolves.
    public string? Merge(string project, int mergeRequest)
    {
        var mr = MergeRequest(project, mergeRequest);
        if (WhyNotMergeable(mr) is { } reason) return reason;
        return Change(() =>
        {
            mr.State = "merged";
            Issue(project, mr.ClosesIssue).State = "closed";
        });
    }

    public string? CloseMergeRequest(string project, int mergeRequest) => Change(() => OpenMergeRequestOrThrow(project, mergeRequest).State = "closed");

    // The first rule a merge request still fails, in the order GitLab shows them; null when it can merge.
    public string? WhyNotMergeable(MockMergeRequest mr) =>
        mr.State != "opened" ? "This merge request is not open."
        : mr.Draft ? "Mark it ready first: drafts can't be merged."
        : mr.Pipeline is null ? "Run the pipeline first."
        : mr.Pipeline == "running" ? "Wait for the pipeline to finish."
        : mr.Pipeline != "success" ? "The pipeline failed. Push a fix and run it again."
        : mr.ApprovedBy.Count < _state.RequiredApprovals ? $"Needs {_state.RequiredApprovals - mr.ApprovedBy.Count} more approval from a reviewer."
        : null;

    // Starts the demo over from the seed file.
    public void Reset()
    {
        try { File.Delete(FilePath); } catch (IOException) { }
        _state = Load();
        Changed?.Invoke();
    }

    // -------------------------------------------------------------------------------------------------------------------

    MockProject Project(string path)
    {
        var project = _state.Projects.FirstOrDefault(p => p.Path.Equals(path, StringComparison.OrdinalIgnoreCase));
        if (project is not null) return project;
        project = new MockProject { Path = path };
        _state.Projects.Add(project);
        return project;
    }

    MockIssue Issue(string project, int iid) => Project(project).Issues.FirstOrDefault(i => i.Iid == iid)
        ?? throw new SupabaseException($"The mock GitLab has no issue {project}#{iid}. It may have been reset.");

    MockMergeRequest MergeRequest(string project, int iid) => Project(project).MergeRequests.FirstOrDefault(m => m.Iid == iid)
        ?? throw new SupabaseException($"The mock GitLab has no merge request {project}!{iid}.");

    MockMergeRequest OpenMergeRequestOrThrow(string project, int iid)
    {
        var mr = MergeRequest(project, iid);
        return mr.State == "opened" ? mr : throw new SupabaseException("That merge request is no longer open.");
    }

    GitLabIssue ToGitLab(string project, MockIssue issue) =>
        new(issue.Iid, IssueUrl(project, issue.Iid), issue.State, issue.Labels, issue.Assignee is null ? 0 : 1);

    string? Change(Action change)
    {
        try { change(); }
        catch (SupabaseException e) { return e.Message; }
        Save();
        Changed?.Invoke();
        return null;
    }

    void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(_state, Json));
    }

    // The saved demo, or a fresh one from the seed: its projects with their issues numbered from 1.
    static MockGitLabState Load()
    {
        try
        {
            if (File.Exists(FilePath) && JsonSerializer.Deserialize<MockGitLabState>(File.ReadAllText(FilePath), Json) is { } saved) return saved;
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { }
        var seed = JsonSerializer.Deserialize<MockGitLabState>(Shared.ReadAsset("MockGitLab", "seed.json"), Json) ?? new MockGitLabState();
        foreach (var project in seed.Projects)
        {
            var created = DateTimeOffset.Now.AddDays(-project.Issues.Count - 3);
            foreach (var issue in project.Issues)
            {
                issue.Iid = project.NextIssue++;
                issue.Author = "Levy intake";
                issue.CreatedAt = created = created.AddDays(1);
            }
        }
        return seed;
    }

    static string Slug(string title)
    {
        var words = new string(title.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : ' ').ToArray()).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return string.Join('-', words.Take(5));
    }
}
