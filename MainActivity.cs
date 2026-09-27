using Android.App;
using Android.Content;
using Android.Net;
using Android.OS;
using Android.Views;
using Android.Widget;
using System.IO.Compression;
using System.Text.Json;

namespace PSVVPN.Android;

[Activity(Label = "PSVVPN", MainLauncher = true, Exported = true)]
public class MainActivity : Activity
{
    const int PickZip = 1001;
    const int VpnReq = 1002;

    TextView status = null!;
    EditText password = null!;
    Button connect = null!;

    string? configPath;
    string? pfxPath;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        var root = new LinearLayout(this)
        {
            Orientation = Orientation.Vertical
        };

        root.SetPadding(48, 70, 48, 30);

        var title = new TextView(this)
        {
            Text = "PSVVPN",
            TextSize = 32,
            Gravity = GravityFlags.Center
        };

        var sub = new TextView(this)
        {
            Text = "Pishgaman Sepand Viera\n89.163.206.27 : 443",
            TextSize = 16,
            Gravity = GravityFlags.Center
        };

        var import = new Button(this)
        {
            Text = "وارد کردن ZIP هویت دستگاه"
        };

        password = new EditText(this)
        {
            Hint = "رمز فایل PFX",
            InputType =
                global::Android.Text.InputTypes.ClassText |
                global::Android.Text.InputTypes.TextVariationPassword
        };

        connect = new Button(this)
        {
            Text = "اتصال",
            Enabled = false
        };

        var disconnect = new Button(this)
        {
            Text = "قطع اتصال"
        };

        status = new TextView(this)
        {
            Text = "وضعیت: ابتدا فایل ZIP را انتخاب کنید",
            TextSize = 16
        };

        foreach (var view in new View[]
        {
            title,
            sub,
            import,
            password,
            connect,
            disconnect,
            status
        })
        {
            root.AddView(
                view,
                new LinearLayout.LayoutParams(-1, -2)
                {
                    TopMargin = 18
                });
        }

        SetContentView(root);

        import.Click += (_, __) =>
        {
            var intent = new Intent(Intent.ActionOpenDocument);

            intent.AddCategory(Intent.CategoryOpenable);
            intent.SetType("application/zip");

            StartActivityForResult(intent, PickZip);
        };

        connect.Click += (_, __) => StartVpn();

        disconnect.Click += (_, __) =>
        {
            var intent = new Intent(this, typeof(TunnelService));
            intent.SetAction("STOP");

            StartService(intent);

            status.Text = "وضعیت: قطع شد";
        };
    }

    protected override void OnActivityResult(
        int requestCode,
        Result resultCode,
        Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);

        if (requestCode == PickZip &&
            resultCode == Result.Ok &&
            data?.Data != null)
        {
            try
            {
                using var input =
                    ContentResolver!.OpenInputStream(data.Data)!;

                var dir =
                    Path.Combine(
                        FilesDir!.AbsolutePath,
                        "identity");

                Directory.CreateDirectory(dir);

                using var zip =
                    new ZipArchive(
                        input,
                        ZipArchiveMode.Read);

                var configEntry =
                    zip.GetEntry("client.json")
                    ?? throw new Exception(
                        "client.json پیدا نشد");

                var pfxEntry =
                    zip.GetEntry("client.pfx")
                    ?? throw new Exception(
                        "client.pfx پیدا نشد");

                configPath =
                    Path.Combine(
                        dir,
                        "client.json");

                pfxPath =
                    Path.Combine(
                        dir,
                        "client.pfx");

                configEntry.ExtractToFile(
                    configPath,
                    true);

                pfxEntry.ExtractToFile(
                    pfxPath,
                    true);

                var config =
                    JsonSerializer.Deserialize<ClientConfig>(
                        File.ReadAllText(configPath))
                    ?? new ClientConfig();

                // Endpoint جدید PSVVPN
                config.Host = "89.163.206.27";
                config.Port = 443;

                File.WriteAllText(
                    configPath,
                    JsonSerializer.Serialize(config));

                status.Text =
                    "وضعیت: ZIP با موفقیت وارد شد";

                connect.Enabled = true;
            }
            catch (Exception ex)
            {
                status.Text =
                    "خطای ZIP: " + ex.Message;
            }

            return;
        }

        if (requestCode == VpnReq &&
            resultCode == Result.Ok)
        {
            LaunchService();
        }
    }

    void StartVpn()
    {
        if (string.IsNullOrWhiteSpace(password.Text))
        {
            status.Text =
                "رمز فایل PFX را وارد کنید";

            return;
        }

        var prepareIntent =
            VpnService.Prepare(this);

        if (prepareIntent != null)
        {
            StartActivityForResult(
                prepareIntent,
                VpnReq);
        }
        else
        {
            LaunchService();
        }
    }

    void LaunchService()
    {
        if (string.IsNullOrEmpty(pfxPath))
        {
            status.Text =
                "ابتدا ZIP هویت دستگاه را وارد کنید";

            return;
        }

        var intent =
            new Intent(
                this,
                typeof(TunnelService));

        intent.SetAction("START");

        intent.PutExtra(
            "config",
            configPath);

        intent.PutExtra(
            "pfx",
            pfxPath);

        intent.PutExtra(
            "password",
            password.Text);

        if (Build.VERSION.SdkInt >=
            BuildVersionCodes.O)
        {
            StartForegroundService(intent);
        }
        else
        {
                        StartService(intent);
        }

        status.Text =
            "وضعیت: در حال اتصال...";
    }
}

public class ClientConfig
{
    public string Host { get; set; }
        = "89.163.206.27";

    public int Port { get; set; }
        = 443;

    public string ServerCertificateSha256
    {
        get;
        set;
    } = "";

    public string CertificateFile
    {
        get;
        set;
    } = "client.pfx";

    public string AdapterName
    {
        get;
        set;
    } = "PSVVPN";
}
