using Android.App;
using Android.Content;
using Android.Net;
using Android.OS;
using Android.Views;
using Android.Widget;
using System.IO.Compression;
using System.Text.Json;

namespace PSVVPN.Android;

[Activity(Label="PSVVPN", MainLauncher=true, Exported=true)]
public class MainActivity : Activity
{
    const int PickZip=1001, VpnReq=1002;
    TextView status=null!; EditText password=null!; Button connect=null!;
    string? configPath,pfxPath;

    protected override void OnCreate(Bundle? b) {
        base.OnCreate(b);
        var root=new LinearLayout(this){Orientation=Orientation.Vertical};
        root.SetPadding(48,70,48,30);
        var title=new TextView(this){Text="PSVVPN",TextSize=32,Gravity=GravityFlags.Center};
        var sub=new TextView(this){Text="Pishgaman Sepand Viera\n89.163.206.27 : 443",TextSize=16,Gravity=GravityFlags.Center};
        var import=new Button(this){Text="وارد کردن ZIP هویت دستگاه"};
        password=new EditText(this){Hint="رمز فایل PFX",InputType=Android.Text.InputTypes.ClassText|Android.Text.InputTypes.TextVariationPassword};
        connect=new Button(this){Text="اتصال",Enabled=false};
        var disconnect=new Button(this){Text="قطع اتصال"};
        status=new TextView(this){Text="وضعیت: ابتدا فایل ZIP را انتخاب کنید",TextSize=16};
        foreach(var v in new View[]{title,sub,import,password,connect,disconnect,status})
            root.AddView(v,new LinearLayout.LayoutParams(-1,-2){TopMargin=18});
        SetContentView(root);

        import.Click+=(_,__)=>{
            var i=new Intent(Intent.ActionOpenDocument);
            i.AddCategory(Intent.CategoryOpenable);
            i.SetType("application/zip");
            StartActivityForResult(i,PickZip);
        };
        connect.Click+=(_,__)=>StartVpn();
        disconnect.Click+=(_,__)=>{
            StartService(new Intent(this,typeof(TunnelService)).SetAction("STOP"));
            status.Text="وضعیت: قطع شد";
        };
    }

    protected override void OnActivityResult(int r, Result c, Intent? d) {
        base.OnActivityResult(r,c,d);
        if(r==PickZip && c==Result.Ok && d?.Data!=null) {
            try {
                using var input=ContentResolver!.OpenInputStream(d.Data)!;
                var dir=Path.Combine(FilesDir!.AbsolutePath,"identity");
                Directory.CreateDirectory(dir);
                using var z=new ZipArchive(input,ZipArchiveMode.Read);
                var cj=z.GetEntry("client.json")??throw new Exception("client.json پیدا نشد");
                var pf=z.GetEntry("client.pfx")??throw new Exception("client.pfx پیدا نشد");
                configPath=Path.Combine(dir,"client.json");
                pfxPath=Path.Combine(dir,"client.pfx");
                cj.ExtractToFile(configPath,true);
                pf.ExtractToFile(pfxPath,true);
                var cfg=JsonSerializer.Deserialize<ClientConfig>(File.ReadAllText(configPath))??new();
                cfg.Host="89.163.206.27"; cfg.Port=443;
                File.WriteAllText(configPath,JsonSerializer.Serialize(cfg));
                status.Text="وضعیت: ZIP با موفقیت وارد شد";
                connect.Enabled=true;
            } catch(Exception ex) { status.Text="خطای ZIP: "+ex.Message; }
        } else if(r==VpnReq && c==Result.Ok) LaunchService();
    }

    void StartVpn() {
        if(string.IsNullOrWhiteSpace(password.Text)){status.Text="رمز PFX را وارد کنید";return;}
        var prep=VpnService.Prepare(this);
        if(prep!=null) StartActivityForResult(prep,VpnReq); else LaunchService();
    }
    void LaunchService() {
        var i=new Intent(this,typeof(TunnelService));
        i.SetAction("START");
        i.PutExtra("config",configPath); i.PutExtra("pfx",pfxPath); i.PutExtra("password",password.Text);
        if(Build.VERSION.SdkInt>=BuildVersionCodes.O) StartForegroundService(i); else StartService(i);
        status.Text="وضعیت: در حال اتصال...";
    }
}
public class ClientConfig {
    public string Host{get;set;}="89.163.206.27";
    public int Port{get;set;}=443;
    public string ServerCertificateSha256{get;set;}="";
    public string CertificateFile{get;set;}="client.pfx";
    public string AdapterName{get;set;}="PSVVPN";
}
