namespace Daraban.Agent.Core.Agents;

/// <summary>
/// Parses one entry of AgentOptions.RemoteHosts, e.g.:
///   ssh://root:mypassword@10.0.0.5
///   winrm://Administrator:mypassword@10.0.0.6:5985
///   winrm://Administrator:mypassword@10.0.0.6:5986   (5986 => https)
/// </summary>
public sealed record RemoteHostSpec(string Scheme, string Host, int Port, string Username, string Password)
{
    public static RemoteHostSpec Parse(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new FormatException("Remote host entry is empty.");

        Uri uri;
        try
        {
            uri = new Uri(connectionString);
        }
        catch (UriFormatException ex)
        {
            throw new FormatException(
                $"Could not parse remote host entry '{connectionString}'. " +
                "Expected format: ssh://user:password@host[:port] or winrm://user:password@host[:port]", ex);
        }

        var scheme = uri.Scheme.ToLowerInvariant();
        if (scheme is not ("ssh" or "winrm"))
            throw new FormatException($"Unsupported remote host scheme '{scheme}' in '{connectionString}'. Use ssh:// or winrm://.");

        var userInfo = uri.UserInfo.Split(':', 2);
        var username = userInfo.Length > 0 ? Uri.UnescapeDataString(userInfo[0]) : "";
        var password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : "";

        if (string.IsNullOrWhiteSpace(username))
            throw new FormatException($"Remote host entry '{connectionString}' is missing a username (expected user:password@host).");

        var defaultPort = scheme == "winrm" ? 5985 : 22;
        var port = uri.Port > 0 ? uri.Port : defaultPort;

        return new RemoteHostSpec(scheme, uri.Host, port, username, password);
    }

    public bool WinrmHttps => Scheme == "winrm" && Port == 5986;
}
