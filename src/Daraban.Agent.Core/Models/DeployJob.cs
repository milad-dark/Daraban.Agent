namespace Daraban.Agent.Core.Models;

/// <summary>
/// A deploy job as returned by the server: one or more files to fetch, verify, then
/// a command to run once all files are staged locally (mirrors GLPI's job/associatedFiles/actions model).
/// </summary>
public sealed record DeployJob
{
    public string JobId { get; set; } = "";
    public string Name { get; set; } = "";
    public List<DeployFile> Files { get; set; } = [];
    public string InstallCommand { get; set; } = "";   // e.g. "msiexec /i app.msi /quiet" or "dpkg -i app.deb"
    public int TimeoutSeconds { get; set; } = 600;
}

public sealed record DeployFile
{
    public string Url { get; set; } = "";
    public string FileName { get; set; } = "";
    public string Sha256 { get; set; } = "";
}

public sealed record DeployJobResult
{
    public string JobId { get; set; } = "";
    public DeployStatus Status { get; set; }
    public string? Message { get; set; }
    public int? ExitCode { get; set; }
    public DateTime CompletedUtc { get; set; } = DateTime.UtcNow;
}
