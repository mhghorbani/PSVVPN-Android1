using Android.App;
using Android.Content;
using Android.Net;
using Android.OS;
using Android.Views;
using Android.Widget;
using System.IO.Compression;
using System.Text.Json;

namespace PSVVPN.Android;

[Activity(
    Label = "PSVVPN",
    MainLauncher = true,
    Exported = true
)]
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

        try
        {
            BuildInterface();
        }
        catch (Exception ex)
        {
            ShowStartupError(ex);
        }
    }

    void BuildInterface()
    {
        var scroll = new ScrollView(this);

        var root = new LinearLayout(this)
        {
            Orientation = Orientation.Vertical,
            Gravity = GravityFlags.CenterHorizontal
        };

        root.SetPadding(48, 80, 48, 48);

        // عنوان برنامه
        var title = new TextView(this)
        {
            Text = "PSVVPN",
            TextSize = 34,
            Gravity = GravityFlags.Center
        };

        title.SetPadding(0, 20, 0, 8);

        // نام شرکت
        var company = new TextView(this)
        {
            Text = "Pishgaman Sepand Viera",
            TextSize = 17,
            Gravity = GravityFlags.Center
        };

        // اطلاعات سرور
        var server = new TextView(this)
        {
            Text = "Secure VPN Client\n89.163.206.27 : 443",
            TextSize = 14,
            Gravity = GravityFlags.Center
        };

        server.SetPadding(0, 8, 0, 30);

        // دکمه انتخاب ZIP
        var importButton = new Button(this)
        {
            Text = "انتخاب فایل هویت VPN"
        };

        // پسورد PFX
        password = new EditText(this)
        {
            Hint = "رمز فایل PFX",
            Gravity = GravityFlags.Center
        };

        password.InputType =
            global::Android.Text.InputTypes.ClassText |
            global::Android.Text.InputTypes.TextVariationPassword;

        // دکمه اتصال
        connect = new Button(this)
        {
            Text = "اتصال به VPN",
            Enabled = false
        };

        // دکمه قطع
        var disconnect = new Button(this)
        {
            Text = "قطع اتصال"
        };

        // وضعیت
        status = new TextView(this)
        {
            Text = "وضعیت: فایل هویت VPN را انتخاب کنید",
            TextSize = 15,
            Gravity = GravityFlags.Center
        };

        status.SetPadding(0, 25, 0, 10);

        Add(root, title);
        Add(root, company);
        Add(root, server);
        Add(root, importButton);
        Add(root, password);
        Add(root, connect);
        Add(root, disconnect);
        Add(root, status);

        scroll.AddView(root);

        SetContentView(scroll);

        // انتخاب ZIP
        importButton.Click += (_, __) =>
        {
            try
            {
                var intent =
                    new Intent(Intent.ActionOpenDocument);

                intent.AddCategory(
                    Intent.CategoryOpenable);

                // بعضی File Manager های Xiaomi
                // application/zip را درست برنمی‌گردانند.
                intent.SetType("*/*");

                StartActivityForResult(
                    intent,
                    PickZip);
            }
            catch (Exception ex)
            {
                status.Text =
                    "خطای انتخاب فایل: " +
                    ex.Message;
            }
        };

        // اتصال
        connect.Click += (_, __) =>
        {
            try
            {
                StartVpn();
            }
            catch (Exception ex)
            {
                status.Text =
                    "خطای اتصال: " +
                    ex.Message;
            }
        };

        // قطع اتصال
        disconnect.Click += (_, __) =>
        {
            try
            {
                var intent =
                    new Intent(
                        this,
                        typeof(TunnelService));

                intent.SetAction("STOP");

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
                    "وضعیت: اتصال قطع شد";
            }
            catch (Exception ex)
            {
                status.Text =
                    "خطای قطع اتصال: " +
                    ex.Message;
            }
        };
    }

    static void Add(
        LinearLayout root,
        View view)
    {
        root.AddView(
            view,
            new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent,
                ViewGroup.LayoutParams.WrapContent)
            {
                TopMargin = 14
            });
    }

    protected override void OnActivityResult(
        int requestCode,
        Result resultCode,
        Intent? data)
    {
        base.OnActivityResult(
            requestCode,
            resultCode,
            data);

        try
        {
            if (requestCode == PickZip &&
                resultCode == Result.Ok &&
                data?.Data != null)
            {
                ImportIdentityZip(data.Data);
                return;
            }

            if (requestCode == VpnReq)
            {
                if (resultCode == Result.Ok)
                {
                    LaunchService();
                }
                else
                {
                    status.Text =
                        "مجوز VPN توسط کاربر تأیید نشد";
                }
            }
        }
        catch (Exception ex)
        {
            if (status != null)
            {
                status.Text =
                    "خطا: " +
                    ex.Message;
            }
            else
            {
                ShowStartupError(ex);
            }
        }
    }

    void ImportIdentityZip(
        global::Android.Net.Uri uri)
    {
        status.Text =
            "وضعیت: در حال بررسی فایل...";

        using var input =
            ContentResolver?.OpenInputStream(uri);

        if (input == null)
        {
            throw new Exception(
                "فایل انتخاب‌شده قابل خواندن نیست.");
        }

        var dir =
            Path.Combine(
                FilesDir!.AbsolutePath,
                "identity");

        Directory.CreateDirectory(dir);

        using var zip =
            new ZipArchive(
                input,
                ZipArchiveMode.Read);

        ZipArchiveEntry? configEntry = null;
        ZipArchiveEntry? pfxEntry = null;

        foreach (var entry in zip.Entries)
        {
            var name =
                Path.GetFileName(entry.FullName);

            if (name.Equals(
                "client.json",
                StringComparison.OrdinalIgnoreCase))
            {
                configEntry = entry;
            }

            if (name.Equals(
                "client.pfx",
                StringComparison.OrdinalIgnoreCase))
            {
                pfxEntry = entry;
            }
        }

        if (configEntry == null)
        {
            throw new Exception(
                "فایل client.json داخل ZIP پیدا نشد.");
        }

        if (pfxEntry == null)
        {
            throw new Exception(
                "فایل client.pfx داخل ZIP پیدا نشد.");
        }

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

        // خواندن کانفیگ
        var json =
            File.ReadAllText(configPath);

        var config =
            JsonSerializer.Deserialize<ClientConfig>(
                json,
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                })
            ?? new ClientConfig();

        // سرور جدید PSVVPN
        config.Host =
            "89.163.206.27";

        config.Port =
            443;

        // ذخیره کانفیگ اصلاح‌شده
        File.WriteAllText(
            configPath,
            JsonSerializer.Serialize(
                config,
                new JsonSerializerOptions
                {
                    WriteIndented = true
                }));

        connect.Enabled = true;

        status.Text =
            "وضعیت: فایل هویت با موفقیت وارد شد";
    }

    void StartVpn()
    {
        if (string.IsNullOrWhiteSpace(
            password.Text))
        {
            status.Text =
                "رمز فایل PFX را وارد کنید";

            return;
        }

        if (string.IsNullOrWhiteSpace(
            pfxPath))
        {
            status.Text =
                "ابتدا فایل هویت VPN را انتخاب کنید";

            return;
        }

        var prepareIntent =
            VpnService.Prepare(this);

        if (prepareIntent != null)
        {
            status.Text =
                "در انتظار تأیید مجوز VPN...";

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
        if (string.IsNullOrWhiteSpace(
            pfxPath))
        {
            status.Text =
                "فایل PFX موجود نیست";

            return;
        }

        var intent =
            new Intent(
                this,
                typeof(TunnelService));

        intent.SetAction("START");

        intent.PutExtra(
            "config",
            configPath ?? "");

        intent.PutExtra(
            "pfx",
            pfxPath);

        intent.PutExtra(
            "password",
            password.Text ?? "");

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
            "وضعیت: در حال اتصال به PSVVPN...";
    }

    void ShowStartupError(
        Exception ex)
    {
        try
        {
            var root =
                new LinearLayout(this)
                {
                    Orientation =
                        Orientation.Vertical
                };

            root.SetPadding(
                40,
                80,
                40,
                40);

            var title =
                new TextView(this)
                {
                    Text =
                        "PSVVPN - Startup Error",
                    TextSize = 22
                };

            var error =
                new TextView(this)
                {
                    Text =
                        ex.ToString(),
                    TextSize = 14
                };

            root.AddView(title);
            root.AddView(error);

            SetContentView(root);
        }
        catch
        {
            Toast.MakeText(
                this,
                "PSVVPN startup error: " +
                ex.Message,
                ToastLength.Long
            )?.Show();
        }
    }
}

public class ClientConfig
{
    public string Host
    {
        get;
        set;
    } = "89.163.206.27";

    public int Port
    {
        get;
        set;
    } = 443;

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
