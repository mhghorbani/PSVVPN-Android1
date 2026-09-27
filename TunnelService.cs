using Android.App;
using Android.Content;
using Android.Net;
using Android.OS;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;

namespace PSVVPN.Android;

[Service(
    Name = "com.psvvpn.client.TunnelService",
    Permission = "android.permission.BIND_VPN_SERVICE",
    Exported = false,
    ForegroundServiceType =
        global::Android.Content.PM.ForegroundService.TypeSpecialUse)]
[IntentFilter(new[] { "android.net.VpnService" })]
public class TunnelService : VpnService
{
    CancellationTokenSource? cts;
    ParcelFileDescriptor? tun;

    public override StartCommandResult OnStartCommand(
        Intent? intent,
        StartCommandFlags flags,
        int startId)
    {
        if (intent?.Action == "STOP")
        {
            StopTunnel();

            return StartCommandResult.NotSticky;
        }

        if (intent?.Action == "START")
        {
            StartForegroundNotification();

            cts?.Cancel();

            cts = new CancellationTokenSource();

            _ = RunTunnel(
                intent,
                cts.Token);
        }

        return StartCommandResult.Sticky;
    }

    void StartForegroundNotification()
    {
        const string channelId =
            "psvvpn";

        if (Build.VERSION.SdkInt >=
            BuildVersionCodes.O)
        {
            var manager =
                (NotificationManager)
                GetSystemService(
                    NotificationService)!;

            var channel =
                new NotificationChannel(
                    channelId,
                    "PSVVPN",
                    NotificationImportance.Low);

            manager.CreateNotificationChannel(
                channel);
        }

        var builder =
            Build.VERSION.SdkInt >=
            BuildVersionCodes.O
            ? new Notification.Builder(
                this,
                channelId)
            : new Notification.Builder(this);

        var notification =
            builder
                .SetContentTitle("PSVVPN")
                .SetContentText(
                    "VPN در حال اجراست")
                .SetOngoing(true)
                .SetSmallIcon(
                    global::Android.Resource.Drawable
                        .StatSysWarning)
                .Build();

        StartForeground(
            1101,
            notification);
    }

    async Task RunTunnel(
        Intent intent,
        CancellationToken ct)
    {
        TcpClient? tcp = null;
        SslStream? ssl = null;

        try
        {
            var pfxPath =
                intent.GetStringExtra("pfx");

            var password =
                intent.GetStringExtra(
                    "password");

            if (string.IsNullOrEmpty(pfxPath))
                throw new Exception(
                    "مسیر PFX مشخص نشده است");

            var certificate =
                new X509Certificate2(
                    pfxPath,
                    password,
                    X509KeyStorageFlags.Exportable);

            tcp = new TcpClient();

            /*
             * مهم:
             * سوکت باید قبل از اضافه شدن Route
             * به VPN از خود VPN مستثنی شود.
             */
            if (!Protect(tcp.Client))
            {
                throw new Exception(
                    "Protect socket failed");
            }

            await tcp.ConnectAsync(
                "89.163.206.27",
                443,
                ct);

            ssl = new SslStream(
                tcp.GetStream(),
                false,
                ValidateServerCertificate);

            var sslOptions =
                new SslClientAuthenticationOptions
                {
                    TargetHost =
                        "89.163.206.27",

                    ClientCertificates =
                        new X509CertificateCollection
                        {
                            certificate
                        }
                };

            await ssl.AuthenticateAsClientAsync(
                sslOptions,
                ct);

            var builder =
                new Builder(this);

            builder.SetSession(
                "PSVVPN");

            builder.SetMtu(
                1400);

            builder.AddAddress(
                "10.66.66.2",
                24);

            builder.AddRoute(
                "0.0.0.0",
                0);

            builder.AddDnsServer(
                "1.1.1.1");

            tun =
                builder.Establish();

            if (tun == null)
            {
                throw new Exception(
                    "TUN ایجاد نشد");
            }

            /*
             * برای Input و Output دو descriptor
             * مستقل می‌سازیم تا بسته‌شدن یکی
             * باعث بسته‌شدن دیگری نشود.
             */
            var inputDescriptor =
                tun.Dup();

            var outputDescriptor =
                tun.Dup();

            using var tunInput =
                new ParcelFileDescriptor
                    .AutoCloseInputStream(
                        inputDescriptor);

            using var tunOutput =
                new ParcelFileDescriptor
                    .AutoCloseOutputStream(
                        outputDescriptor);

            var uploadTask =
                PumpTunToTls(
                    tunInput,
                    ssl,
                    ct);

            var downloadTask =
                PumpTlsToTun(
                    ssl,
                    tunOutput,
                    ct);

            await Task.WhenAny(
                uploadTask,
                downloadTask);
        }
        catch (OperationCanceledException)
        {
            // قطع عادی VPN
        }
        catch (Exception)
        {
            // در نسخه بعدی لاگ UI اضافه می‌شود
        }
        finally
        {
            try
            {
                ssl?.Dispose();
            }
            catch
            {
            }

            try
            {
                tcp?.Dispose();
            }
            catch
            {
            }

            StopTunnel();
        }
    }

    /*
     * فعلاً TLS handshake را می‌پذیریم تا
     * ارتباط Pilot تست شود.
     *
     * قبل از نسخه Production باید SHA256
     * گواهی سرور جدید Pin شود.
     */
    bool ValidateServerCertificate(
        object sender,
        X509Certificate? certificate,
        X509Chain? chain,
        SslPolicyErrors errors)
    {
        return true;
    }

    static async Task PumpTunToTls(
        Stream tunStream,
        Stream tlsStream,
        CancellationToken ct)
    {
        var buffer =
            new byte[65535];

        while (!ct.IsCancellationRequested)
        {
            var length =
                await tunStream.ReadAsync(
                    buffer.AsMemory(
                        0,
                        buffer.Length),
                    ct);

            if (length <= 0)
                break;

            /*
             * Packet framing:
             * 4 byte big-endian packet size
             */
            var header =
                new byte[]
                {
                    (byte)(length >> 24),
                    (byte)(length >> 16),
                    (byte)(length >> 8),
                    (byte)length
                };

            await tlsStream.WriteAsync(
                header,
                ct);

            await tlsStream.WriteAsync(
                buffer.AsMemory(
                    0,
                    length),
                ct);

            await tlsStream.FlushAsync(
                ct);
        }
    }

    static async Task PumpTlsToTun(
        Stream tlsStream,
        Stream tunStream,
        CancellationToken ct)
    {
        var header =
            new byte[4];

        while (!ct.IsCancellationRequested)
        {
            await ReadExact(
                tlsStream,
                header,
                ct);

            var length =
                (header[0] << 24) |
                (header[1] << 16) |
                (header[2] << 8) |
                header[3];

            if (length <= 0 ||
                length > 65535)
            {
                throw new IOException(
                    "Invalid VPN packet");
            }

            var packet =
                new byte[length];

            await ReadExact(
                tlsStream,
                packet,
                ct);

            await tunStream.WriteAsync(
                packet.AsMemory(
                    0,
                    packet.Length),
                ct);

            await tunStream.FlushAsync(
                ct);
        }
    }

    static async Task ReadExact(
        Stream stream,
        byte[] buffer,
        CancellationToken ct)
    {
        var offset = 0;

        while (offset <
               buffer.Length)
        {
            var count =
                await stream.ReadAsync(
                    buffer.AsMemory(
                        offset,
                        buffer.Length -
                        offset),
                    ct);

            if (count <= 0)
            {
                throw new
                    EndOfStreamException();
            }

            offset += count;
        }
    }

    void StopTunnel()
    {
        try
        {
            cts?.Cancel();
        }
        catch
        {
        }

        cts = null;

        try
        {
            tun?.Close();
        }
        catch
        {
        }

        tun = null;

        StopForeground(true);
        StopSelf();
    }

    public override void OnDestroy()
    {
        try
        {
            cts?.Cancel();
            tun?.Close();
        }
        catch
        {
        }

        cts = null;
        tun = null;

        base.OnDestroy();
    }
}
