using Android.App;
using Android.Content;
using Android.Net;
using Android.OS;
using Java.IO;
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

            _ = RunTunnel(intent, cts.Token);
        }

        return StartCommandResult.Sticky;
    }

    void StartForegroundNotification()
    {
        const string channelId = "psvvpn";

        if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
        {
            var manager =
                (NotificationManager)GetSystemService(
                    NotificationService)!;

            var channel =
                new NotificationChannel(
                    channelId,
                    "PSVVPN",
                    NotificationImportance.Low);

            manager.CreateNotificationChannel(channel);
        }

        Notification.Builder builder;

        if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
        {
            builder =
                new Notification.Builder(
                    this,
                    channelId);
        }
        else
        {
            builder =
                new Notification.Builder(this);
        }

        var notification =
            builder
                .SetContentTitle("PSVVPN")
                .SetContentText("VPN در حال اجراست")
                .SetOngoing(true)
                .SetSmallIcon(
                    global::Android.Resource.Drawable
                        .StatSysWarning)
                .Build();

        StartForeground(1101, notification);
    }

    async Task RunTunnel(
        Intent intent,
        CancellationToken ct)
    {
        TcpClient? tcp = null;
        SslStream? ssl = null;

        ParcelFileDescriptor? inputDescriptor = null;
        ParcelFileDescriptor? outputDescriptor = null;

        try
        {
            var pfxPath =
                intent.GetStringExtra("pfx");

            var password =
                intent.GetStringExtra("password");

            if (string.IsNullOrWhiteSpace(pfxPath))
            {
                throw new Exception(
                    "مسیر فایل PFX مشخص نشده است.");
            }

            var certificate =
                new X509Certificate2(
                    pfxPath,
                    password,
                    X509KeyStorageFlags.Exportable);

            tcp = new TcpClient();

            await tcp.ConnectAsync(
                "89.163.206.27",
                443,
                ct);

            /*
             * Protect در binding فعلی .NET Android
             * یک File Descriptor عددی دریافت می‌کند.
             */
            int socketFd =
                (int)tcp.Client.Handle;

            if (!Protect(socketFd))
            {
                throw new Exception(
                    "امکان Protect کردن سوکت VPN وجود ندارد.");
            }

            ssl =
                new SslStream(
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

            var vpnBuilder =
                new Builder(this);

            vpnBuilder.SetSession("PSVVPN");
            vpnBuilder.SetMtu(1400);

            vpnBuilder.AddAddress(
                "10.66.66.2",
                24);

            vpnBuilder.AddRoute(
                "0.0.0.0",
                0);

            vpnBuilder.AddDnsServer(
                "1.1.1.1");

            tun = vpnBuilder.Establish();

            if (tun == null)
            {
                throw new Exception(
                    "رابط TUN ایجاد نشد.");
            }

            inputDescriptor = tun.Dup();
            outputDescriptor = tun.Dup();

            using var tunInput =
                new ParcelFileDescriptor
                    .AutoCloseInputStream(
                        inputDescriptor);

            using var tunOutput =
                new ParcelFileDescriptor
                    .AutoCloseOutputStream(
                        outputDescriptor);

            inputDescriptor = null;
            outputDescriptor = null;

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
        catch (System.OperationCanceledException)
        {
            // قطع عادی VPN
        }
        catch (Exception)
        {
            // بعداً لاگ UI اضافه می‌شود.
        }
        finally
        {
            try
            {
                inputDescriptor?.Close();
            }
            catch
            {
            }

            try
            {
                outputDescriptor?.Close();
            }
            catch
            {
            }

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

    bool ValidateServerCertificate(
        object sender,
        X509Certificate? certificate,
        X509Chain? chain,
        SslPolicyErrors errors)
    {
        /*
         * فقط برای Pilot.
         * قبل از نسخه Production باید
         * Certificate Pinning فعال شود.
         */
        return true;
    }

    static async Task PumpTunToTls(
        Java.IO.InputStream tunInput,
        System.IO.Stream tlsStream,
        CancellationToken ct)
    {
        var buffer =
            new byte[65535];

        while (!ct.IsCancellationRequested)
        {
            int length =
                await Task.Run(
                    () => tunInput.Read(
                        buffer,
                        0,
                        buffer.Length),
                    ct);

            if (length <= 0)
                break;

            var header =
                new byte[]
                {
                    (byte)(length >> 24),
                    (byte)(length >> 16),
                    (byte)(length >> 8),
                    (byte)length
                };

            await tlsStream.WriteAsync(
                header.AsMemory(),
                ct);

            await tlsStream.WriteAsync(
                buffer.AsMemory(
                    0,
                    length),
                ct);

            await tlsStream.FlushAsync(ct);
        }
    }

    static async Task PumpTlsToTun(
        System.IO.Stream tlsStream,
        Java.IO.OutputStream tunOutput,
        CancellationToken ct)
    {
        var header = new byte[4];

        while (!ct.IsCancellationRequested)
        {
            await ReadExact(
                tlsStream,
                header,
                ct);

            int length =
                (header[0] << 24) |
                (header[1] << 16) |
                (header[2] << 8) |
                header[3];

            if (length <= 0 ||
                length > 65535)
            {
                throw new System.IO.IOException(
                    "Invalid VPN packet length.");
            }

            var packet =
                new byte[length];

            await ReadExact(
                tlsStream,
                packet,
                ct);

            await Task.Run(
                () =>
                {
                    tunOutput.Write(
                        packet,
                        0,
                        packet.Length);

                    tunOutput.Flush();
                },
                ct);
        }
    }

    static async Task ReadExact(
        System.IO.Stream stream,
        byte[] buffer,
        CancellationToken ct)
    {
        int offset = 0;

        while (offset < buffer.Length)
        {
            int count =
                await stream.ReadAsync(
                    buffer.AsMemory(
                        offset,
                        buffer.Length - offset),
                    ct);

            if (count <= 0)
            {
                throw new System.IO.EndOfStreamException();
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
        }
        catch
        {
        }

        try
        {
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
