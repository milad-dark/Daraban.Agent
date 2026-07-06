namespace Daraban.Agent.Core.Models;

// ---------------------------------------------------------------------------
// Deploy (software push)
// ---------------------------------------------------------------------------

public enum DeployStatus
{
    Pending,
    Downloading,
    ChecksumFailed,
    Installing,
    Success,
    Failed
}




