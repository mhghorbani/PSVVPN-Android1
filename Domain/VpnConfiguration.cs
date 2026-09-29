namespace NPViera.Domain;

public sealed class VpnConfiguration
{
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 443;
    public string ServerName { get; set; } = string.Empty;
    public string ServerCertificateSha256 { get; set; } = string.Empty;
    public string VpnAddress { get; set; } = "10.66.66.2";
    public int VpnPrefixLength { get; set; } = 24;
    public int Mtu { get; set; } = 1400;
    public string[] DnsServers { get; set; } = Array.Empty<string>();
    public string[] Routes { get; set; } = new[] { "0.0.0.0/0" };
    public int Version { get; set; } = 1;
}
