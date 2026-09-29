using Android.App;
using Android.Content;
using Android.Net;
using Android.OS;
using Java.IO;
using NPViera.Domain;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;

namespace PSVVPN.Android;

[Service(Name="com.psvvpn.client.TunnelService",Permission="android.permission.BIND_VPN_SERVICE",Exported=false,
    ForegroundServiceType=global::Android.Content.PM.ForegroundService.TypeSpecialUse)]
[IntentFilter(new[]{"android.net.VpnService"})]
public class TunnelService : VpnService
{
    CancellationTokenSource? cts; ParcelFileDescriptor? tun;

    public override StartCommandResult OnStartCommand(Intent? intent,StartCommandFlags flags,int startId)
    {
        if(intent?.Action=="STOP"){StopTunnel();return StartCommandResult.NotSticky;}
        if(intent?.Action=="START"){StartForegroundNotification();cts?.Cancel();cts=new();_=RunTunnel(intent,cts.Token);}
        return StartCommandResult.Sticky;
    }

    void StartForegroundNotification()
    {
        const string id="npviera_vpn";
        if(Build.VERSION.SdkInt>=BuildVersionCodes.O){
            var m=(NotificationManager)GetSystemService(NotificationService)!;
            m.CreateNotificationChannel(new NotificationChannel(id,"NPViera VPN",NotificationImportance.Low));
        }
        var b=Build.VERSION.SdkInt>=BuildVersionCodes.O?new Notification.Builder(this,id):new Notification.Builder(this);
        StartForeground(1101,b.SetContentTitle("NPViera").SetContentText("VPN در حال اجراست").SetOngoing(true)
            .SetSmallIcon(global::Android.Resource.Drawable.StatSysWarning).Build());
    }

    async Task RunTunnel(Intent intent,CancellationToken ct)
    {
        TcpClient? tcp=null;SslStream? ssl=null;ParcelFileDescriptor? inFd=null,outFd=null;
        try{
            var cfgPath=intent.GetStringExtra("config");var pfx=intent.GetStringExtra("pfx");var pwd=intent.GetStringExtra("password");
            if(string.IsNullOrWhiteSpace(cfgPath)||string.IsNullOrWhiteSpace(pfx))throw new Exception("NPV-ID-001");
            var cfg=JsonSerializer.Deserialize<VpnConfiguration>(File.ReadAllText(cfgPath),new JsonSerializerOptions{PropertyNameCaseInsensitive=true})
                ??throw new Exception("NPV-CFG-001");
            var cert=new X509Certificate2(pfx,pwd,X509KeyStorageFlags.Exportable);
            tcp=new TcpClient();await tcp.ConnectAsync(cfg.Host,cfg.Port,ct);
            if(!Protect((int)tcp.Client.Handle))throw new Exception("NPV-VPN-001");
            ssl=new SslStream(tcp.GetStream(),false,(s,c,ch,e)=>ValidateServerCertificate(cfg,c,e));
            await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions{
                TargetHost=string.IsNullOrWhiteSpace(cfg.ServerName)?cfg.Host:cfg.ServerName,
                ClientCertificates=new X509CertificateCollection{cert}},ct);

            var b=new Builder(this);b.SetSession("NPViera");b.SetMtu(cfg.Mtu);b.AddAddress(cfg.VpnAddress,cfg.VpnPrefixLength);
            foreach(var dns in cfg.DnsServers)if(!string.IsNullOrWhiteSpace(dns))b.AddDnsServer(dns);
            foreach(var route in cfg.Routes){var p=route.Split('/');if(p.Length==2&&int.TryParse(p[1],out var prefix))b.AddRoute(p[0],prefix);}
            tun=b.Establish()??throw new Exception("NPV-VPN-001");
            inFd=tun.Dup();outFd=tun.Dup();
            using var input=new ParcelFileDescriptor.AutoCloseInputStream(inFd);using var output=new ParcelFileDescriptor.AutoCloseOutputStream(outFd);
            inFd=null;outFd=null;
            await Task.WhenAny(PumpTunToTls(input,ssl,ct),PumpTlsToTun(ssl,output,ct));
        }catch(OperationCanceledException){}catch(Exception){}finally{
            try{inFd?.Close();}catch{} try{outFd?.Close();}catch{} try{ssl?.Dispose();}catch{} try{tcp?.Dispose();}catch{} StopTunnel();
        }
    }

    static bool ValidateServerCertificate(VpnConfiguration cfg,X509Certificate? certificate,SslPolicyErrors errors)
    {
        if(certificate==null||errors!=SslPolicyErrors.None)return false;
        if(string.IsNullOrWhiteSpace(cfg.ServerCertificateSha256))return true;
        using var sha=SHA256.Create();var actual=Convert.ToHexString(sha.ComputeHash(certificate.GetRawCertData()));
        var expected=cfg.ServerCertificateSha256.Replace(":","").Replace(" ","").ToUpperInvariant();
        return CryptographicOperations.FixedTimeEquals(System.Text.Encoding.ASCII.GetBytes(actual),System.Text.Encoding.ASCII.GetBytes(expected));
    }

    static async Task PumpTunToTls(Java.IO.InputStream input,Stream tls,CancellationToken ct)
    {
        var buffer=new byte[65535];
        while(!ct.IsCancellationRequested){
            var n=await Task.Run(()=>input.Read(buffer,0,buffer.Length),ct);if(n<=0)break;
            var h=new[]{(byte)(n>>24),(byte)(n>>16),(byte)(n>>8),(byte)n};
            await tls.WriteAsync(h.AsMemory(),ct);await tls.WriteAsync(buffer.AsMemory(0,n),ct);await tls.FlushAsync(ct);
        }
    }

    static async Task PumpTlsToTun(Stream tls,Java.IO.OutputStream output,CancellationToken ct)
    {
        var h=new byte[4];
        while(!ct.IsCancellationRequested){
            await ReadExact(tls,h,ct);var n=(h[0]<<24)|(h[1]<<16)|(h[2]<<8)|h[3];
            if(n<=0||n>65535)throw new IOException("Invalid VPN packet length.");
            var p=new byte[n];await ReadExact(tls,p,ct);await Task.Run(()=>{output.Write(p,0,p.Length);output.Flush();},ct);
        }
    }

    static async Task ReadExact(Stream s,byte[] b,CancellationToken ct)
    {
        var o=0;while(o<b.Length){var n=await s.ReadAsync(b.AsMemory(o,b.Length-o),ct);if(n<=0)throw new EndOfStreamException();o+=n;}
    }

    void StopTunnel(){try{cts?.Cancel();}catch{}cts=null;try{tun?.Close();}catch{}tun=null;StopForeground(true);StopSelf();}
    public override void OnDestroy(){try{cts?.Cancel();}catch{}try{tun?.Close();}catch{}cts=null;tun=null;base.OnDestroy();}
}
