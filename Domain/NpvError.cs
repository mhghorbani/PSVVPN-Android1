namespace NPViera.Domain;

public sealed record NpvError(string Code, string UserMessage, string? TechnicalDetail = null);

public static class NpvErrors
{
    public static NpvError MissingIdentity(string? detail = null) => new("NPV-ID-001", "هویت دستگاه در دسترس نیست.", detail);
    public static NpvError InvalidConfiguration(string? detail = null) => new("NPV-CFG-001", "تنظیمات اتصال معتبر نیست.", detail);
    public static NpvError NetworkUnavailable(string? detail = null) => new("NPV-NET-001", "اینترنت در دسترس نیست.", detail);
    public static NpvError TlsFailure(string? detail = null) => new("NPV-TLS-001", "اعتبارسنجی ارتباط امن ناموفق بود.", detail);
    public static NpvError TunnelFailure(string? detail = null) => new("NPV-VPN-001", "ایجاد تونل VPN ناموفق بود.", detail);
}
