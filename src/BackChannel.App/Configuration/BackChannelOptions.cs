using System.Net;

namespace BackChannel.App.Configuration;

public sealed class BackChannelOptions
{
    public const string SectionName = "BackChannel";

    public string DisplayName { get; set; } = string.Empty;

    public string MachineName { get; set; } = string.Empty;

    public int DiscoveryPort { get; set; } = 52520;

    public string DiscoveryTarget { get; set; } = string.Empty;

    public int TcpPort { get; set; }

    public int MaximumConcurrentConnections { get; set; } = 32;

    public int AnnouncementIntervalSeconds { get; set; } = 30;

    public int PeerTimeoutSeconds { get; set; } = 90;

    public static bool IsValid(BackChannelOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return options.DiscoveryPort is > IPEndPoint.MinPort and <= IPEndPoint.MaxPort
               && options.TcpPort is >= IPEndPoint.MinPort and <= IPEndPoint.MaxPort
               && options.MaximumConcurrentConnections > 0
               && options.AnnouncementIntervalSeconds > 0
               && options.PeerTimeoutSeconds
                   > options.AnnouncementIntervalSeconds
               && IsOptionalNameValid(options.DisplayName)
               && IsOptionalNameValid(options.MachineName)
               && TryGetDiscoveryTarget(options, out _);
    }

    public static bool TryGetDiscoveryTarget(
        BackChannelOptions options,
        out IPEndPoint? endpoint)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.DiscoveryTarget))
        {
            endpoint = null;
            return true;
        }

        return IPEndPoint.TryParse(options.DiscoveryTarget.Trim(), out endpoint)
               && endpoint.Address.AddressFamily
                   == System.Net.Sockets.AddressFamily.InterNetwork
               && endpoint.Port > IPEndPoint.MinPort;
    }

    private static bool IsOptionalNameValid(string value) =>
        string.IsNullOrWhiteSpace(value) || value.Trim().Length <= 128;
}
