using Android.App;
using Android.Content;
using Android.Net;
using Android.OS;
using Android.Views;
using Android.Widget;
using NPViera.Configuration;
using NPViera.Domain;
using System.IO.Compression;
using System.Text.Json;

namespace PSVVPN.Android;

[Activity(Label="NPViera", MainLauncher=true, Exported=true)]
public class MainActivity : Activity
{
    const int PickZip=1001, VpnReq=1002;
    TextView status=null!, server=null!;
    EditText password=null!;
    Button connect=null!;
    string? configPath,pfxPath;
    VpnConfiguration? config;

    protected override void OnCreate(Bundle? state){base.OnCreate(state);BuildInterface();}

    void BuildInterface()
    {
        var scroll=new ScrollView(this);
        var root=new LinearLayout(this){Orientation=Orientation.Vertical};
        root.SetGravity(GravityFlags.CenterHorizontal); root.SetPadding(48,80,48,48);
        var title=new TextView(this){Text="NPViera",TextSize=34,Gravity=GravityFlags.Center};
        var sub=new TextView(this){Text="Secure Enterprise VPN",TextSize=16,Gravity=GravityFlags.Center};
        server=new TextView(this){Text="Server: not configured",TextSize=14,Gravity=GravityFlags.Center};
        var import=new Button(this){Text="انتخاب فایل هویت VPN"};
        password=new EditText(this){Hint="رمز فایل PFX",Gravity=GravityFlags.Center};
        password.InputType=global::Android.Text.InputTypes.ClassText|global::Android.Text.InputTypes.TextVariationPassword;
        connect=new Button(this){Text="اتصال",Enabled=false};
        var disconnect=new Button(this){Text="قطع اتصال"};
        status=new TextView(this){Text="وضعیت: آماده‌سازی",TextSize=15,Gravity=GravityFlags.Center};
        foreach(var v in new View[]{title,sub,server,import,password,connect,disconnect,status})
            root.AddView(v,new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent,ViewGroup.LayoutParams.WrapContent){TopMargin=14});
        scroll.AddView(root);SetContentView(scroll);

        import.Click+=(_,__)=>{var i=new Intent(Intent.ActionOpenDocument);i.AddCategory(Intent.CategoryOpenable);i.SetType("*/*");StartActivityForResult(i,PickZip);};
        connect.Click+=(_,__)=>StartVpn();
        disconnect.Click+=(_,__)=>{var i=new Intent(this,typeof(TunnelService));i.SetAction("STOP");StartService(i);status.Text="وضعیت: قطع شد";};
        status.Text="وضعیت: فایل هویت را انتخاب کنید";
    }

    protected override void OnActivityResult(int requestCode,Result resultCode,Intent? data)
    {
        base.OnActivityResult(requestCode,resultCode,data);
        try{
            if(requestCode==PickZip&&resultCode==Result.Ok&&data?.Data!=null){ImportIdentityZip(data.Data);return;}
            if(requestCode==VpnReq){if(resultCode==Result.Ok)LaunchService();else status.Text="مجوز VPN تأیید نشد";}
        }catch(Exception ex){status.Text="خطا: "+ex.Message;}
    }

    void ImportIdentityZip(global::Android.Net.Uri uri)
    {
        status.Text="وضعیت: بررسی هویت...";
        using var input=ContentResolver?.OpenInputStream(uri)??throw new Exception("فایل قابل خواندن نیست.");
        var dir=Path.Combine(FilesDir!.AbsolutePath,"identity");Directory.CreateDirectory(dir);
        using var zip=new ZipArchive(input,ZipArchiveMode.Read);
        var ce=zip.Entries.FirstOrDefault(x=>Path.GetFileName(x.FullName).Equals("client.json",StringComparison.OrdinalIgnoreCase));
        var pe=zip.Entries.FirstOrDefault(x=>Path.GetFileName(x.FullName).Equals("client.pfx",StringComparison.OrdinalIgnoreCase));
        if(ce==null||pe==null)throw new Exception("client.json یا client.pfx در ZIP پیدا نشد.");
        configPath=Path.Combine(dir,"client.json");pfxPath=Path.Combine(dir,"client.pfx");
        ce.ExtractToFile(configPath,true);pe.ExtractToFile(pfxPath,true);
        config=JsonSerializer.Deserialize<VpnConfiguration>(File.ReadAllText(configPath),new JsonSerializerOptions{PropertyNameCaseInsensitive=true});
        if(!ConfigurationValidator.IsValid(config))throw new Exception("NPV-CFG-001: تنظیمات اتصال معتبر نیست.");
        server.Text=$"Server: {config!.Host}:{config.Port}";
        connect.Enabled=true;status.Text="وضعیت: هویت آماده است";
    }

    void StartVpn()
    {
        if(config==null||string.IsNullOrWhiteSpace(configPath)||string.IsNullOrWhiteSpace(pfxPath)){status.Text="ابتدا فایل هویت را انتخاب کنید";return;}
        if(string.IsNullOrWhiteSpace(password.Text)){status.Text="رمز PFX را وارد کنید";return;}
        var p=VpnService.Prepare(this);
        if(p!=null){status.Text="در انتظار مجوز VPN...";StartActivityForResult(p,VpnReq);}else LaunchService();
    }

    void LaunchService()
    {
        var i=new Intent(this,typeof(TunnelService));i.SetAction("START");
        i.PutExtra("config",configPath);i.PutExtra("pfx",pfxPath);i.PutExtra("password",password.Text??"");
        if(Build.VERSION.SdkInt>=BuildVersionCodes.O)StartForegroundService(i);else StartService(i);
        status.Text="وضعیت: در حال اتصال...";
    }
}
