using NPViera.Domain;

namespace NPViera.Configuration;

public static class ConfigurationValidator
{
    public static bool IsValid(VpnConfiguration? config) =>
        config is not null &&
        !string.IsNullOrWhiteSpace(config.Host) &&
        config.Port is > 0 and <= 65535 &&
        config.Mtu is >= 576 and <= 9000 &&
        !string.IsNullOrWhiteSpace(config.VpnAddress);
}
