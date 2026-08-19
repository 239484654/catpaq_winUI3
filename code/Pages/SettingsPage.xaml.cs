// Settings 页：语言 / 文件关联 / 更新。
// 关于已挪出为侧栏独立页（AboutPage），管理员状态（运行权限）仍保留在本页。
//
// 崩溃根因（0x802B000A / stowed exception）：
// XAML 中 Slider 的 Value 属性在 InitializeComponent 期间就会触发
// ValueChanged，此时事件 handler 若访问尚未初始化的控件 / App.MainWindow，
// WinUI 会将异常转成原生 XAML 错误直接崩溃（绕过 .NET UnhandledException）。
// 修复：
//   1) XAML 不再内联绑定 ValueChanged / SelectionChanged（事件在代码中绑定）
//   2) 事件在 Loaded 后才订阅，且 handler 全程判空保护
//   3) 初始化期设置控件值时用 _loading 闸门拦截回调
using System.Net.Http;
using System.Security.Principal;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Win32;

namespace Catpaq.Pages;

public sealed partial class SettingsPage : Page
{
    private MainWindow Main => App.MainWindow;
    private bool _loading;
    private bool _subscribed;
    private static readonly string RepoUrl = "https://github.com/239484654/catpaq_winUI3";

    public SettingsPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += (_, _) => UnsubscribeEvents();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Main.SettingsPage = this;
        LoadLanguages();
        ApplyValues();
        UpdateAdminStatus();
        UpdateAssocStatus();
        ApplyLanguage();
        SubscribeEvents();
    }

    // ------------------------------------------------------------------
    private void SubscribeEvents()
    {
        if (_subscribed)
            return;
        _subscribed = true;
        CmbLanguage.SelectionChanged += CmbLanguage_SelectionChanged;
    }

    private void UnsubscribeEvents()
    {
        if (!_subscribed)
            return;
        _subscribed = false;
        CmbLanguage.SelectionChanged -= CmbLanguage_SelectionChanged;
    }

    // 当前语言在注册表中的索引（前缀匹配；找不到回退 en-US 即索引 0）
    private static int MatchLanguageIndex(string langName)
    {
        var tag = langName.Split('-')[0];
        for (var i = 0; i < Core.I18n.Languages.Length; i++)
        {
            if (Core.I18n.Languages[i].Code.Split('-')[0].Equals(tag, StringComparison.OrdinalIgnoreCase))
                return i;
        }
        return 0;
    }

    private void LoadLanguages()
    {
        CmbLanguage.Items.Clear();
        foreach (var (_, display) in Core.I18n.Languages)
            CmbLanguage.Items.Add(display);
        CmbLanguage.SelectedIndex = MatchLanguageIndex(Main.LangName);
    }

    private void ApplyValues()
    {
        _loading = true;
        try
        {
            // 无滑动条设置项：仅同步语言选择
            CmbLanguage.SelectedIndex = MatchLanguageIndex(Main.LangName);
        }
        finally
        {
            _loading = false;
        }
    }

    public void ApplyLanguage()
    {
        LblTitle.Text = Main.T("set.title", "Settings");
        LblLanguage.Text = Main.T("set.language", "Language");
        LblAssoc.Text = Main.T("set.assoc", "File associations");
        BtnAssoc.Content = Main.T("set.assoc_btn", "Associate .zpaq");
        BtnRemoveAssoc.Content = Main.T("set.assoc_remove_btn", "Remove .zpaq association");
        LblUpdate.Text = Main.T("set.update", "Updates");
        BtnCheckUpdate.Content = Main.T("set.check_btn", "Check for updates");
        LinkRepo.Content = RepoUrl;
        UpdateAssocStatus();
        UpdateAdminStatus();
    }

    private void CmbLanguage_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        var idx = CmbLanguage.SelectedIndex;
        if (idx < 0 || idx >= Core.I18n.Languages.Length) return;
        Main.LangName = Core.I18n.Languages[idx].Code;
        // 广播到导航项与所有页面
        Main.ApplyLanguage();
    }

    // ------------------------------------------------------------------
    // 文件关联：在 HKCU 注册 .zpaq -> Catpaq（无需管理员权限）
    private void BtnAssoc_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var exe = Environment.ProcessPath;
            using (var classes = Registry.CurrentUser.CreateSubKey(@"Software\Classes\.zpaq"))
                classes?.SetValue("", "Catpaq.ZpaqArchive");
            using (var cmd = Registry.CurrentUser.CreateSubKey(@"Software\Classes\Catpaq.ZpaqArchive\shell\open\command"))
                cmd?.SetValue("", $"\"{exe}\" \"%1\"");
            LblAssocStatus.Text = Main.T("set.assoc_ok", ".zpaq files are now associated with Catpaq");
            UpdateAssocStatus();
        }
        catch (Exception ex)
        {
            LblAssocStatus.Text = Main.T("set.assoc_fail", "Failed to set file association: ") + ex.Message;
        }
    }

    // 移除关联：仅当 .zpaq 当前指向 Catpaq 时才清除，避免破坏其他程序的关联
    private void BtnRemoveAssoc_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Classes\.zpaq", writable: true))
            {
                if (key?.GetValue("") as string == "Catpaq.ZpaqArchive")
                    key.DeleteValue("", throwOnMissingValue: false);
            }
            Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\Catpaq.ZpaqArchive", throwOnMissingSubKey: false);
            LblAssocStatus.Text = Main.T("set.assoc_remove_ok", ".zpaq association removed");
            UpdateAssocStatus();
        }
        catch (Exception ex)
        {
            LblAssocStatus.Text = Main.T("set.assoc_remove_fail", "Failed to remove file association: ") + ex.Message;
        }
    }

    private void UpdateAssocStatus()
    {
        try
        {
            var def = Registry.CurrentUser.OpenSubKey(@"Software\Classes\.zpaq")?.GetValue("") as string;
            LblAssocStatus.Text = def == "Catpaq.ZpaqArchive"
                ? Main.T("set.assoc_on", ".zpaq is associated with Catpaq")
                : Main.T("set.assoc_off", ".zpaq is not associated yet");
        }
        catch
        {
            LblAssocStatus.Text = "";
        }
    }

    // ------------------------------------------------------------------
    // 更新：尚未发布到商店，不做实际更新；仅检测 GitHub 连通性与最新版本
    private async void BtnCheckUpdate_Click(object sender, RoutedEventArgs e)
    {
        BtnCheckUpdate.IsEnabled = false;
        LblUpdateStatus.Text = Main.T("set.update_checking", "Checking for updates...");
        try
        {
            using var client = new HttpClient();
            client.Timeout = TimeSpan.FromSeconds(10);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Catpaq");
            using var resp = await client.GetAsync("https://api.github.com/repos/239484654/catpaq_winUI3/releases/latest");
            if (resp.IsSuccessStatusCode)
            {
                var json = await resp.Content.ReadAsStringAsync();
                var tag = ExtractJsonString(json, "tag_name");
                LblUpdateStatus.Text = Main.T("set.update_latest", "Connected to GitHub. Latest release: ") + (tag ?? "?");
            }
            else
            {
                LblUpdateStatus.Text = Main.T("set.update_no_net", "Cannot connect to GitHub (HTTP ")
                    + (int)resp.StatusCode + ")";
            }
        }
        catch
        {
            LblUpdateStatus.Text = Main.T("set.update_no_net", "Cannot connect to GitHub");
        }
        finally
        {
            BtnCheckUpdate.IsEnabled = true;
        }
    }

    private static string? ExtractJsonString(string json, string key)
    {
        var marker = "\"" + key + "\":\"";
        var i = json.IndexOf(marker, StringComparison.Ordinal);
        if (i < 0) return null;
        var start = i + marker.Length;
        var end = json.IndexOf('"', start);
        return end < 0 ? null : json[start..end];
    }

    private void UpdateAdminStatus()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            LblAdmin.Text = principal.IsInRole(WindowsBuiltInRole.Administrator)
                ? Main.T("set.admin_yes", "Running as administrator")
                : Main.T("set.admin_no", "Running as standard user");
        }
        catch
        {
            LblAdmin.Text = "";
        }
    }
}
