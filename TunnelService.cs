using Android.App;
using Android.Content;
using Android.Net;
using Android.OS;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;

namespace PSVVPN.Android;

[Service(Name="com.psvvpn.client.TunnelService", Permission="android.permission.BIND_VPN_SERVICE", Exported=false,
         ForegroundServiceType=Android.Content.PM.ForegroundService.TypeSpecialUse)]
[IntentFilter(new[]{"android.net.VpnService"})]
public class TunnelService : VpnService
{
    CancellationTokenSource? cts; ParcelFileDescriptor? tun;

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int id) {
        if(intent?.Action=="STOP"){StopTunnel();return StartCommandResult.NotSticky;}
        if(intent?.Action=="START"){StartForegroundNow();cts?.Cancel();cts=new();_=Run(intent,cts.Token);}
        return StartCommandResult.Sticky;
    }

    void StartForegroundNow() {
        const string ch="psvvpn";
        if(Build.VERSION.SdkInt>=BuildVersionCodes.O) {
            var m=(NotificationManager)GetSystemService(NotificationService)!;
            m.CreateNotificationChannel(new NotificationChannel(ch,"PSVVPN",NotificationImportance.Low));
        }
        var n=new Notification.Builder(this,Build.VERSION.SdkInt>=BuildVersionCodes.O?ch:null)
            .SetContentTitle("PSVVPN").SetContentText("VPN در حال اجراست")
            .SetSmallIcon(Android.Resource.Drawable.StatSysWarning).Build();
        StartForeground(1101,n);
    }

    async Task Run(Intent i,CancellationToken ct) {
        try {
            var pfx=i.GetStringExtra("pfx")!; var pass=i.GetStringExtra("password")!;
            var cert=new X509Certificate2(pfx,pass,X509KeyStorageFlags.Exportable);
            var tcp=new TcpClient();
            await tcp.ConnectAsync("89.163.206.27",443,ct);
            var ssl=new SslStream(tcp.GetStream(),false,(s,c,chain,e)=>true);
            await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions{
                TargetHost="89.163.206.27",
                ClientCertificates=new X509CertificateCollection{cert}
            },ct);

            tun=new Builder(this).SetSession("PSVVPN").AddAddress("10.66.66.2",24)
                .AddRoute("0.0.0.0",0).AddDnsServer("1.1.1.1").SetMtu(1400).Establish();
            if(tun==null) throw new Exception("TUN ایجاد نشد");
            using var fs=new FileStream(tun.FileDescriptor!,FileAccess.ReadWrite);
            var up=PumpTunToTls(fs,ssl,ct); var down=PumpTlsToTun(ssl,fs,ct);
            await Task.WhenAny(up,down);
        } catch { StopTunnel(); }
    }

    static async Task PumpTunToTls(Stream tun,Stream tls,CancellationToken ct) {
        var b=new byte[65535];
        while(!ct.IsCancellationRequested) {
            var n=await tun.ReadAsync(b,ct); if(n<=0)break;
            var h=new byte[]{(byte)(n>>24),(byte)(n>>16),(byte)(n>>8),(byte)n};
            await tls.WriteAsync(h,ct); await tls.WriteAsync(b.AsMemory(0,n),ct); await tls.FlushAsync(ct);
        }
    }
    static async Task PumpTlsToTun(Stream tls,Stream tun,CancellationToken ct) {
        var h=new byte[4];
        while(!ct.IsCancellationRequested) {
            await ReadExact(tls,h,ct);
            int n=(h[0]<<24)|(h[1]<<16)|(h[2]<<8)|h[3];
            if(n<=0||n>65535)throw new IOException("packet");
            var b=new byte[n]; await ReadExact(tls,b,ct); await tun.WriteAsync(b,ct);
        }
    }
    static async Task ReadExact(Stream s,byte[] b,CancellationToken ct) {
        int o=0; while(o<b.Length){int n=await s.ReadAsync(b.AsMemory(o),ct);if(n<=0)throw new EndOfStreamException();o+=n;}
    }
    void StopTunnel(){try{cts?.Cancel();tun?.Close();}catch{}tun=null;StopForeground(true);StopSelf();}
    public override void OnDestroy(){StopTunnel();base.OnDestroy();}
}
