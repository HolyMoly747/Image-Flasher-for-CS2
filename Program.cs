using CounterStrike2GSI;
using CounterStrike2GSI.EventMessages;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Windows.Forms;

Logger.Initialize();

ApplicationConfiguration.Initialize();
Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
Application.ThreadException +=
    (_, e) => Logger.Error("APP", "Unhandled UI thread exception", e.Exception);

AppDomain.CurrentDomain.UnhandledException +=
    (_, e) =>
    {
        if (e.ExceptionObject is Exception exception)
        {
            Logger.Error(
                "APP",
                $"Unhandled application exception | IsTerminating={e.IsTerminating}",
                exception);
        }
        else
        {
            Logger.Error(
                "APP",
                $"Unhandled non-Exception object | IsTerminating={e.IsTerminating}");
        }
    };

try
{
    Logger.Info(
        "APP",
        $"Application started | .NET={Environment.Version} | " +
        $"64BitProcess={Environment.Is64BitProcess} | " +
        $"OS={Environment.OSVersion} | " +
        $"Assembly={Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "unknown"}");

    var settings = AppSettings.Load();

    Logger.Info(
        "SETTINGS",
        $"Settings loaded | FlashEnabled={settings.FlashEnabled} | " +
        $"SoundEnabled={settings.SoundEnabled} | Duration={settings.FlashDurationMs}ms | " +
        $"Hold={settings.FlashHoldMs}ms | Opacity={settings.FlashOpacity}% | " +
        $"PreserveAspect={settings.PreserveImageAspectRatio} | Language={settings.Language}");

    using var appContext =
        new TrayApplicationContext(settings);

    Application.Run(appContext);
}
catch (Exception ex)
{
    Logger.Error("APP", "Fatal exception escaped application startup/run", ex);
    throw;
}
finally
{
    Logger.Info("APP", "Application stopped");
    Logger.Shutdown();
}


// ============================================================
// LOGGER
// ============================================================
sealed class Logger
{
    private static readonly object Sync = new();

    private static StreamWriter? _writer;

    private static string _currentFile = string.Empty;

    private static readonly string SessionId =
        Guid.NewGuid().ToString("N")[..8];

    private static readonly string LogDirectory =
        Path.Combine(
            AppContext.BaseDirectory,
            "Logs");

    private const int RetentionDays = 14;

    public static void Initialize()
    {
        lock (Sync)
        {
            try
            {
                Directory.CreateDirectory(LogDirectory);
                CleanupOldLogs();
                EnsureWriter();
            }
            catch
            {
                // Logging must never prevent the application from starting.
            }
        }

        Info("APP", "Logger initialized");
    }

    public static void Shutdown()
    {
        lock (Sync)
        {
            try
            {
                _writer?.Flush();
                _writer?.Dispose();
            }
            catch
            {
            }
            finally
            {
                _writer = null;
                _currentFile = string.Empty;
            }
        }
    }

    public static void Debug(string source, string message)
    {
        Write("DEBUG", source, message, null);
    }

    public static void Info(string source, string message)
    {
        Write("INFO", source, message, null);
    }

    public static void Warning(string source, string message)
    {
        Write("WARN", source, message, null);
    }

    public static void Error(string source, string message)
    {
        Write("ERROR", source, message, null);
    }

    public static void Error(string source, string message, Exception exception)
    {
        Write("ERROR", source, message, exception);
    }

    private static void Write(
        string level,
        string source,
        string message,
        Exception? exception)
    {
        lock (Sync)
        {
            try
            {
                EnsureWriter();

                string timestamp =
                    DateTimeOffset.Now.ToString(
                        "yyyy-MM-dd HH:mm:ss.fff zzz");

                string safeMessage =
                    message.Replace("\r", " ").Replace("\n", " ");

                string line =
                    $"{timestamp} | {level,-5} | {source,-8} | Session={SessionId} | {safeMessage}";

                _writer!.WriteLine(line);

                if (exception != null)
                {
                    _writer.WriteLine("Exception:");
                    _writer.WriteLine(exception.ToString());
                }

                _writer.Flush();
            }
            catch
            {
                // Never allow logging failures to affect the application.
            }
        }
    }

    private static void EnsureWriter()
    {
        string file =
            Path.Combine(
                LogDirectory,
                $"ImageFlasher_{DateTime.Now:yyyy-MM-dd}.log");

        if (_writer != null &&
            string.Equals(
                _currentFile,
                file,
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _writer?.Flush();
        _writer?.Dispose();

        _writer =
            new StreamWriter(
                new FileStream(
                    file,
                    FileMode.Append,
                    FileAccess.Write,
                    FileShare.ReadWrite),
                new UTF8Encoding(false))
            {
                AutoFlush = true
            };

        _currentFile = file;
    }

    private static void CleanupOldLogs()
    {
        try
        {
            DateTime cutoff =
                DateTime.Now.Date.AddDays(
                    -RetentionDays);

            foreach (string file in Directory.EnumerateFiles(
                         LogDirectory,
                         "ImageFlasher_*.log",
                         SearchOption.TopDirectoryOnly))
            {
                try
                {
                    if (File.GetLastWriteTime(file) < cutoff)
                    {
                        File.Delete(file);
                    }
                }
                catch
                {
                }
            }
        }
        catch
        {
        }
    }
}


// ============================================================
// APPLICATION SETTINGS
// ============================================================
sealed class AppSettings
{
    public bool FlashEnabled { get; set; } = true;

    public int FlashDurationMs { get; set; } = 450;

    public int FlashHoldMs { get; set; } = 70;

    public int FlashOpacity { get; set; } = 100;

    public bool SoundEnabled { get; set; } = true;

    public bool ShowDebug { get; set; } = true;

    public bool PreserveImageAspectRatio { get; set; } = false;

    public string AccentColor { get; set; } = "#0078D7";

    public string Language { get; set; } = "ru";


    private static string FilePath =>
        Path.Combine(
            AppContext.BaseDirectory,
            "settings.json");


    public static AppSettings Load()
    {
        if (!File.Exists(FilePath))
        {
            var settings =
                new AppSettings();

            settings.Save();

            Logger.Info(
                "SETTINGS",
                "Settings file not found; created default settings");

            return settings;
        }


        try
        {
            string json =
                File.ReadAllText(FilePath);


            var settings =
                JsonSerializer.Deserialize<AppSettings>(
                    json);


            if (settings == null)
            {
                settings =
                    new AppSettings();

                settings.Save();

                return settings;
            }


            if (string.IsNullOrWhiteSpace(
                    settings.AccentColor))
            {
                settings.AccentColor =
                    "#0078D7";
            }


            if (settings.Language != "ru" &&
                settings.Language != "en")
            {
                settings.Language =
                    "ru";
            }


            settings.FlashOpacity =
                Math.Clamp(
                    settings.FlashOpacity,
                    0,
                    100);


            settings.Save();


            return settings;
        }
        catch (Exception ex)
        {
            Logger.Error(
                "SETTINGS",
                "Failed to load settings; using defaults",
                ex);

            var settings =
                new AppSettings();

            settings.Save();

            return settings;
        }
    }


    public void Save()
    {
        try
        {
            var options =
                new JsonSerializerOptions
                {
                    WriteIndented = true
                };


            string json =
                JsonSerializer.Serialize(
                    this,
                    options);


            File.WriteAllText(
                FilePath,
                json);
        }
        catch (Exception ex)
        {
            Logger.Error(
                "SETTINGS",
                "Failed to save settings",
                ex);
        }
    }
}


// ============================================================
// TRAY APPLICATION CONTEXT
// ============================================================
sealed class TrayApplicationContext : ApplicationContext
{
    private readonly AppSettings _settings;

    private readonly NotifyIcon _trayIcon;

    private readonly ContextMenuStrip _trayMenu;

    private readonly ToolStripMenuItem _openSettingsItem;

    private readonly ToolStripMenuItem _testImageItem;

    private readonly ToolStripMenuItem _exitItem;

    private readonly FlashOverlay _overlay;

    private readonly GameStateListener _listener;

    private SettingsForm? _settingsForm;

    private bool _isExiting;

    private bool _startupSettingsOpened;


    public TrayApplicationContext(
        AppSettings settings)
    {
        _settings = settings;


        // ----------------------------------------------------
        // OVERLAY
        // ----------------------------------------------------

        _overlay =
            new FlashOverlay(
                _settings);

        _overlay.Show();

        _overlay.SyncNow();


        // ----------------------------------------------------
        // TRAY MENU
        // ----------------------------------------------------

        _trayMenu =
            new ContextMenuStrip();


        _openSettingsItem =
            new ToolStripMenuItem();


        _testImageItem =
            new ToolStripMenuItem();


        _exitItem =
            new ToolStripMenuItem();


        _trayMenu.Items.Add(
            _openSettingsItem);


        _trayMenu.Items.Add(
            _testImageItem);


        _trayMenu.Items.Add(
            new ToolStripSeparator());


        _trayMenu.Items.Add(
            _exitItem);


        _openSettingsItem.Click +=
            (_, _) =>
            {
                OpenSettings();
            };


        _testImageItem.Click +=
            (_, _) =>
            {
                _overlay.ShowFlash(true);
            };


        _exitItem.Click +=
            (_, _) =>
            {
                RequestExit();
            };


        UpdateTrayLanguage();


        // ----------------------------------------------------
        // TRAY ICON
        // ----------------------------------------------------

        _trayIcon =
            new NotifyIcon
            {
                Icon =
                    SystemIcons.Application,

                Visible = true,

                Text =
                    "CS2 Kill Overlay",

                ContextMenuStrip =
                    _trayMenu
            };


        _trayIcon.DoubleClick +=
            (_, _) =>
            {
                OpenSettings();
            };


        // ----------------------------------------------------
        // GSI
        // ----------------------------------------------------

        _listener =
            new GameStateListener(3000);


        _listener.PlayerGotKill +=
            OnPlayerGotKill;


        try
        {
            bool configCreated =
                _listener.GenerateGSIConfigFile(
                    "CS2KillOverlay");


            if (configCreated)
            {
                Logger.Info("GSI", "GSI configuration file generated");
            }
            else
            {
                Logger.Warning("GSI", "Failed to generate GSI configuration file");
            }
        }
        catch (Exception ex)
        {
            Logger.Error(
                "GSI",
                "Exception while generating GSI configuration file",
                ex);
        }


        try
        {
            bool started =
                _listener.Start();


            if (started)
            {
                Logger.Info("GSI", "GSI listener started | Port=3000");
            }
            else
            {
                Logger.Warning("GSI", "GSI listener failed to start | Port=3000");
            }
        }
        catch (Exception ex)
        {
            Logger.Error(
                "GSI",
                "Exception while starting GSI listener",
                ex);
        }


        // ----------------------------------------------------
        // OPEN SETTINGS AFTER THE MESSAGE LOOP
        // ----------------------------------------------------

        Application.Idle +=
            OnApplicationIdle;
    }


    // --------------------------------------------------------
    // TRAY LANGUAGE
    // --------------------------------------------------------
public void UpdateTrayLanguage()
    {
        bool ru =
            _settings.Language == "ru";


        _openSettingsItem.Text =
            ru
                ? "Открыть настройки"
                : "Open settings";


        _testImageItem.Text =
            ru
                ? "Тест картинки"
                : "Test image";


        _exitItem.Text =
            ru
                ? "Выход"
                : "Exit";
    }


    // --------------------------------------------------------
    // OPEN SETTINGS AFTER STARTUP
    // --------------------------------------------------------
private void OnApplicationIdle(
        object? sender,
        EventArgs e)
    {
        Application.Idle -=
            OnApplicationIdle;


        if (_startupSettingsOpened)
            return;


        _startupSettingsOpened = true;


        OpenSettings();
    }


    // --------------------------------------------------------
    // KILL EVENT
    // --------------------------------------------------------
private void OnPlayerGotKill(
        PlayerGotKill gameEvent)
    {
        try
        {
            string weapon =
                gameEvent.Weapon?.Name ??
                "unknown";


            Logger.Info(
                "KILL",
                $"Kill received | Weapon={weapon} | Headshot={gameEvent.IsHeadshot} | Ace={gameEvent.IsAce}");


            _overlay.ShowFlash();
        }
        catch (Exception ex)
        {
            Logger.Error(
                "KILL",
                "Exception while processing kill event",
                ex);
        }
    }


    // --------------------------------------------------------
    // OPEN SETTINGS
    // --------------------------------------------------------
private void OpenSettings()
    {
        if (_isExiting)
            return;


        if (_settingsForm != null &&
            !_settingsForm.IsDisposed)
        {
            if (!_settingsForm.Visible)
            {
                _settingsForm.Show();
            }


            _settingsForm.BringToFront();

            _settingsForm.Activate();

            return;
        }


        try
        {
            _overlay.Hide();
        }
        catch
        {
        }


        _settingsForm =
            new SettingsForm(
                _settings,
                _overlay,
                UpdateTrayLanguage,
                RequestExit);


        _settingsForm.FormClosed +=
            (_, _) =>
            {
                _settingsForm = null;


                if (!_isExiting)
                {
                    try
                    {
                        _overlay.Show();

                        _overlay.SyncNow();
                    }
                    catch
                    {
                    }
                }
            };


        _settingsForm.Show();

        _settingsForm.BringToFront();

        _settingsForm.Activate();
    }


    // --------------------------------------------------------
    // EXIT
    // --------------------------------------------------------
private void RequestExit()
    {
        if (_isExiting)
            return;


        _isExiting = true;


        _settings.Save();


        try
        {
            _listener.Stop();
        }
        catch
        {
        }


        try
        {
            _overlay.CloseOverlay();
        }
        catch
        {
        }


        try
        {
            if (_settingsForm != null &&
                !_settingsForm.IsDisposed)
            {
                _settingsForm.AllowImmediateClose();

                _settingsForm.Close();
            }
        }
        catch
        {
        }


        try
        {
            _trayIcon.Visible = false;

            _trayIcon.Dispose();
        }
        catch
        {
        }


        _trayMenu.Dispose();


        ExitThread();
    }


    // --------------------------------------------------------
    // DISPOSE
    // --------------------------------------------------------
protected override void Dispose(
        bool disposing)
    {
        if (disposing)
        {
            try
            {
                _settings.Save();
            }
            catch
            {
            }


            try
            {
                _listener.Stop();
            }
            catch
            {
            }


            try
            {
                _overlay.CloseOverlay();
            }
            catch
            {
            }


            try
            {
                _trayIcon.Visible = false;

                _trayIcon.Dispose();
            }
            catch
            {
            }


            try
            {
                _trayMenu.Dispose();
            }
            catch
            {
            }
        }


        base.Dispose(disposing);
    }
}


// ============================================================
// SETTINGS FORM
// ============================================================
sealed class SettingsForm : Form
{
    // --------------------------------------------------------
    // THEME
    // --------------------------------------------------------

    private static readonly Color BackgroundColor =
        Color.FromArgb(
            32,
            32,
            32);


    private static readonly Color SurfaceColor =
        Color.FromArgb(
            42,
            42,
            42);


    private static readonly Color SurfaceHoverColor =
        Color.FromArgb(
            52,
            52,
            52);


    private static readonly Color BorderColor =
        Color.FromArgb(
            64,
            64,
            64);


    private static Color PrimaryColor =
        Color.FromArgb(
            0,
            120,
            215);


    private static Color PrimaryHoverColor =
        Color.FromArgb(
            20,
            135,
            225);


    private static Color PrimaryPressedColor =
        Color.FromArgb(
            0,
            100,
            185);


    private static readonly Color TextColor =
        Color.FromArgb(
            240,
            240,
            240);


    private static readonly Color SecondaryTextColor =
        Color.FromArgb(
            165,
            165,
            165);


    private static readonly Color ConnectedColor =
        Color.FromArgb(
            55,
            190,
            90);


    private static readonly Color DisconnectedColor =
        Color.FromArgb(
            220,
            60,
            60);


    // --------------------------------------------------------
    // FIELDS
    // --------------------------------------------------------

    private readonly AppSettings _settings;

    private readonly FlashOverlay _overlay;

    private readonly Action _updateTrayLanguage;

    private readonly Action _requestExit;


    private readonly LanguageToggle _languageToggle;

    private readonly AccentColorPicker _accentColorPicker;

    private readonly Label _accentLabel;


    private readonly Label _flashSectionLabel;

    private readonly Label _durationLabel;

    private readonly Label _holdLabel;

    private readonly Label _opacityLabel;

    private readonly Label _imagesSectionLabel;

    private readonly Label _soundsSectionLabel;

    private readonly CheckBox
        _soundEnabledCheckBox;


    private readonly CheckBox
        _flashEnabledCheckBox;


    private readonly CheckBox
        _showDebugCheckBox;


    private readonly CheckBox
        _preserveAspectRatioCheckBox;


    private readonly TrackBar
        _durationTrackBar;


    private readonly TrackBar
        _holdTrackBar;


    private readonly TrackBar
        _opacityTrackBar;


    private readonly Label
        _durationValueLabel;


    private readonly Label
        _holdValueLabel;


    private readonly Label
        _opacityValueLabel;


    private readonly RoundedButton
        _openImagesButton;


    private readonly RoundedButton
        _refreshImagesButton;


    private readonly Label
        _imageCountLabel;


    private readonly RoundedButton
        _openSoundsButton;


    private readonly RoundedButton
        _refreshSoundsButton;


    private readonly Label
        _soundCountLabel;


    private readonly RoundedButton
        _testFlashButton;


    private readonly RoundedButton
        _hideButton;


    private readonly Panel
        _connectionDot;


    private readonly Label
        _connectionLabel;


    private readonly System.Windows.Forms.Timer
        _connectionTimer;


    private bool _closingForExit;


    public bool IsRussian =>
        _settings.Language == "ru";


    // --------------------------------------------------------
    // CONSTRUCTOR
    // --------------------------------------------------------

    public SettingsForm(
        AppSettings settings,
        FlashOverlay overlay,
        Action updateTrayLanguage,
        Action requestExit)
    {
        _settings = settings;

        _overlay = overlay;

        _updateTrayLanguage =
            updateTrayLanguage;

        _requestExit =
            requestExit;


        // ----------------------------------------------------
        // ACCENT
        // ----------------------------------------------------
Color savedAccent =
            ParseAccentColor(
                _settings.AccentColor);


        SetAccentColors(
            savedAccent);


        // ----------------------------------------------------
        // FORM
        // ----------------------------------------------------
Text =
            "Image Flasher";


        StartPosition =
            FormStartPosition.CenterScreen;


        FormBorderStyle =
            FormBorderStyle.FixedDialog;


        MaximizeBox =
            false;


        MinimizeBox =
            false;


        ShowInTaskbar =
            true;


        ClientSize =
            new Size(
                540,
                705);


        BackColor =
            BackgroundColor;


        ForeColor =
            TextColor;


        Font =
            new Font(
                "Segoe UI",
                9F);


        // ----------------------------------------------------
        // LANGUAGE TOGGLE
        // ----------------------------------------------------
_languageToggle =
            new LanguageToggle(
                _settings.Language)
            {
                Location =
                    new Point(
                        24,
                        18),

                Size =
                    new Size(
                        66,
                        30)
            };


        _languageToggle.LanguageChanged +=
            language =>
            {
                _settings.Language =
                    language;


                _settings.Save();


                ApplyLanguage();

                _updateTrayLanguage();
            };


        Controls.Add(
            _languageToggle);


        // ----------------------------------------------------
        // FLASH SECTION
        // ----------------------------------------------------
_flashSectionLabel =
            CreateSectionTitle(
                "ВСПЫШКА",
                new Point(
                    24,
                    68));


        Controls.Add(
            _flashSectionLabel);


        // ----------------------------------------------------
        // FLASH ENABLED
        // ----------------------------------------------------

        _flashEnabledCheckBox =
            CreateDarkCheckBox(
                "Включить вспышку",
                new Point(
                    28,
                    102));


        _flashEnabledCheckBox.Checked =
            _settings.FlashEnabled;


        _flashEnabledCheckBox.CheckedChanged +=
            (_, _) =>
            {
                _settings.FlashEnabled =
                    _flashEnabledCheckBox.Checked;


                SaveAndRefresh();
            };


        Controls.Add(
            _flashEnabledCheckBox);


        // ----------------------------------------------------
        // DEBUG OVERLAY
        // ----------------------------------------------------

        _showDebugCheckBox =
            CreateDarkCheckBox(
                "Показывать Debug",
                new Point(
                    28,
                    134));


        _showDebugCheckBox.Checked =
            _settings.ShowDebug;


        _showDebugCheckBox.CheckedChanged +=
            (_, _) =>
            {
                _settings.ShowDebug =
                    _showDebugCheckBox.Checked;


                SaveAndRefresh();
            };


        Controls.Add(
            _showDebugCheckBox);


        // ----------------------------------------------------
        // PRESERVE IMAGE ASPECT RATIO
        // ----------------------------------------------------

        _preserveAspectRatioCheckBox =
            CreateDarkCheckBox(
                "Сохранять пропорции",
                new Point(
                    220,
                    134));


        _preserveAspectRatioCheckBox.Checked =
            _settings.PreserveImageAspectRatio;


        _preserveAspectRatioCheckBox.CheckedChanged +=
            (_, _) =>
            {
                _settings.PreserveImageAspectRatio =
                    _preserveAspectRatioCheckBox.Checked;

                SaveAndRefresh();
            };


        Controls.Add(
            _preserveAspectRatioCheckBox);


        // ----------------------------------------------------
        // DURATION
        // ----------------------------------------------------
_durationLabel =
            CreateFieldLabel(
                "Длительность",
                new Point(
                    28,
                    181));


        Controls.Add(
            _durationLabel);


        _durationTrackBar =
            CreateTrackBar(
                new Point(
                    24,
                    204),
                370);


        _durationTrackBar.Minimum =
            100;


        _durationTrackBar.Maximum =
            2000;


        _durationTrackBar.TickFrequency =
            100;


        _durationTrackBar.SmallChange =
            50;


        _durationTrackBar.LargeChange =
            100;


        _durationTrackBar.Value =
            Clamp(
                _settings.FlashDurationMs,
                100,
                2000);


        _durationTrackBar.ValueChanged +=
            (_, _) =>
            {
                _settings.FlashDurationMs =
                    _durationTrackBar.Value;


                UpdateDurationLabel();


                SaveAndRefresh();
            };


        Controls.Add(
            _durationTrackBar);


        _durationValueLabel =
            CreateValueLabel(
                new Point(
                    410,
                    208));


        Controls.Add(
            _durationValueLabel);


        UpdateDurationLabel();


        // ----------------------------------------------------
        // HOLD
        // ----------------------------------------------------
_holdLabel =
            CreateFieldLabel(
                "Удержание",
                new Point(
                    28,
                    254));


        Controls.Add(
            _holdLabel);


        _holdTrackBar =
            CreateTrackBar(
                new Point(
                    24,
                    277),
                370);


        _holdTrackBar.Minimum =
            0;


        _holdTrackBar.Maximum =
            500;


        _holdTrackBar.TickFrequency =
            50;


        _holdTrackBar.SmallChange =
            10;


        _holdTrackBar.LargeChange =
            50;


        _holdTrackBar.Value =
            Clamp(
                _settings.FlashHoldMs,
                0,
                500);


        _holdTrackBar.ValueChanged +=
            (_, _) =>
            {
                _settings.FlashHoldMs =
                    _holdTrackBar.Value;


                UpdateHoldLabel();


                SaveAndRefresh();
            };


        Controls.Add(
            _holdTrackBar);


        _holdValueLabel =
            CreateValueLabel(
                new Point(
                    410,
                    281));


        Controls.Add(
            _holdValueLabel);


        UpdateHoldLabel();


        // ----------------------------------------------------
        // OPACITY
        // ----------------------------------------------------
        _opacityLabel =
            CreateFieldLabel(
                "Прозрачность",
                new Point(
                    28,
                    304));


        Controls.Add(
            _opacityLabel);


        _opacityTrackBar =
            CreateTrackBar(
                new Point(
                    24,
                    327),
                370);


        _opacityTrackBar.Minimum =
            0;


        _opacityTrackBar.Maximum =
            100;


        _opacityTrackBar.TickFrequency =
            10;


        _opacityTrackBar.SmallChange =
            5;


        _opacityTrackBar.LargeChange =
            10;


        _opacityTrackBar.Value =
            Clamp(
                _settings.FlashOpacity,
                0,
                100);


        _opacityTrackBar.ValueChanged +=
            (_, _) =>
            {
                _settings.FlashOpacity =
                    _opacityTrackBar.Value;


                UpdateOpacityLabel();


                SaveAndRefresh();
            };


        Controls.Add(
            _opacityTrackBar);


        _opacityValueLabel =
            CreateValueLabel(
                new Point(
                    410,
                    331));


        Controls.Add(
            _opacityValueLabel);


        UpdateOpacityLabel();


        // ----------------------------------------------------
        // SEPARATOR
        // ----------------------------------------------------
Controls.Add(
            CreateSeparator(
                new Point(
                    24,
                    380),
                492));


        // ----------------------------------------------------
        // IMAGES SECTION
        // ----------------------------------------------------
_imagesSectionLabel =
            CreateSectionTitle(
                "ИЗОБРАЖЕНИЯ",
                new Point(
                    24,
                    398));


        Controls.Add(
            _imagesSectionLabel);


        // ----------------------------------------------------
        // OPEN FOLDER
        // ----------------------------------------------------
_openImagesButton =
            CreateSecondaryButton(
                "Открыть папку",
                new Point(
                    24,
                    433),
                130,
                34);


        _openImagesButton.Click +=
            (_, _) =>
            {
                OpenImagesFolder();
            };


        Controls.Add(
            _openImagesButton);


        // ----------------------------------------------------
        // REFRESH
        // ----------------------------------------------------
_refreshImagesButton =
            CreateSecondaryButton(
                "Обновить",
                new Point(
                    164,
                    433),
                100,
                34);


        _refreshImagesButton.Click +=
            (_, _) =>
            {
                RefreshImages();
            };


        Controls.Add(
            _refreshImagesButton);


        // ----------------------------------------------------
        // IMAGE COUNT
        // ----------------------------------------------------
_imageCountLabel =
            new Label
            {
                AutoSize =
                    true,

                ForeColor =
                    SecondaryTextColor,

                Location =
                    new Point(
                        282,
                        442),

                Font =
                    new Font(
                        "Segoe UI",
                        9F)
            };


        Controls.Add(
            _imageCountLabel);


        UpdateImageCountLabel();


        // ----------------------------------------------------
        // SOUNDS SECTION
        // ----------------------------------------------------
_soundsSectionLabel =
            CreateSectionTitle(
                "ЗВУК",
                new Point(
                    24,
                    476));


        Controls.Add(
            _soundsSectionLabel);


        // ----------------------------------------------------
        // OPEN SOUNDS FOLDER
        // ----------------------------------------------------
_openSoundsButton =
            CreateSecondaryButton(
                "Открыть папку",
                new Point(
                    24,
                    511),
                130,
                34);


        _openSoundsButton.Click +=
            (_, _) =>
            {
                OpenSoundsFolder();
            };


        Controls.Add(
            _openSoundsButton);


        // ----------------------------------------------------
        // REFRESH SOUNDS
        // ----------------------------------------------------
_refreshSoundsButton =
            CreateSecondaryButton(
                "Обновить",
                new Point(
                    164,
                    511),
                100,
                34);


        _refreshSoundsButton.Click +=
            (_, _) =>
            {
                RefreshSounds();
            };


        Controls.Add(
            _refreshSoundsButton);


        // ----------------------------------------------------
        // SOUND COUNT
        // ----------------------------------------------------
_soundCountLabel =
            new Label
            {
                AutoSize =
                    true,

                ForeColor =
                    SecondaryTextColor,

                Location =
                    new Point(
                        282,
                        520),

                Font =
                    new Font(
                        "Segoe UI",
                        9F)
            };


        Controls.Add(
            _soundCountLabel);


        UpdateSoundCountLabel();


        // ----------------------------------------------------
        // SOUND ENABLED
        // ----------------------------------------------------
        _soundEnabledCheckBox =
            CreateDarkCheckBox(
                "Включить звук",
                new Point(
                    282,
                    548));


        _soundEnabledCheckBox.Checked =
            _settings.SoundEnabled;


        _soundEnabledCheckBox.CheckedChanged +=
            (_, _) =>
            {
                _settings.SoundEnabled =
                    _soundEnabledCheckBox.Checked;


                SaveAndRefresh();
            };


        Controls.Add(
            _soundEnabledCheckBox);


        // ----------------------------------------------------
        // BOTTOM SEPARATOR
        // ----------------------------------------------------
Controls.Add(
            CreateSeparator(
                new Point(
                    24,
                    580),
                492));


        // ----------------------------------------------------
        // TEST
        // ----------------------------------------------------
_testFlashButton =
            CreatePrimaryButton(
                "Тест картинки",
                new Point(
                    24,
                    598),
                130,
                34);


        _testFlashButton.Click +=
            (_, _) =>
            {
                _overlay.ShowFlash(true);
            };


        Controls.Add(
            _testFlashButton);


        // ----------------------------------------------------
        // MINIMIZE TO TRAY
        // ----------------------------------------------------
_hideButton =
            CreateSecondaryButton(
                "Свернуть в трей",
                new Point(
                    164,
                    598),
                130,
                34);


        _hideButton.Click +=
            (_, _) =>
            {
                _settings.Save();

                Hide();


                try
                {
                    _overlay.Show();

                    _overlay.SyncNow();
                }
                catch
                {
                }
            };


        Controls.Add(
            _hideButton);


        // ----------------------------------------------------
        // ACCENT
        // ----------------------------------------------------
_accentLabel =
            new Label
            {
                Text =
                    "Акцент",

                AutoSize =
                    true,

                ForeColor =
                    SecondaryTextColor,

                Location =
                    new Point(
                        432,
                        595),

                Font =
                    new Font(
                        "Segoe UI",
                        8F)
            };


        Controls.Add(
            _accentLabel);


        _accentColorPicker =
            new AccentColorPicker(
                savedAccent)
            {
                Location =
                    new Point(
                        500,
                        590),

                Size =
                    new Size(
                        24,
                        24)
            };


        _accentColorPicker.ColorSelected +=
            color =>
            {
                ApplyAccentColor(
                    color);


                _settings.AccentColor =
                    ColorToHex(
                        color);


                _settings.Save();
            };


        Controls.Add(
            _accentColorPicker);


        // ----------------------------------------------------
        // CONNECTION STATUS
        // ----------------------------------------------------
_connectionDot =
            new Panel
            {
                Location =
                    new Point(
                        24,
                        669),

                Size =
                    new Size(
                        11,
                        11),

                BackColor =
                    DisconnectedColor
            };


        _connectionDot.Paint +=
            (_, e) =>
            {
                e.Graphics.SmoothingMode =
                    SmoothingMode.AntiAlias;


                using var brush =
                    new SolidBrush(
                        _connectionDot.BackColor);


                e.Graphics.FillEllipse(
                    brush,
                    0,
                    0,
                    _connectionDot.Width - 1,
                    _connectionDot.Height - 1);
            };


        Controls.Add(
            _connectionDot);


        _connectionLabel =
            new Label
            {
                AutoSize =
                    true,

                Location =
                    new Point(
                        42,
                        664),

                ForeColor =
                    DisconnectedColor,

                Font =
                    new Font(
                        "Segoe UI",
                        8.5F)
            };


        Controls.Add(
            _connectionLabel);


        // ----------------------------------------------------
        // CONNECTION TIMER
        // ----------------------------------------------------
_connectionTimer =
            new System.Windows.Forms.Timer
            {
                Interval = 500
            };


        _connectionTimer.Tick +=
            (_, _) =>
            {
                UpdateConnectionStatus();
            };


        _connectionTimer.Start();


        UpdateConnectionStatus();


        // ----------------------------------------------------
        // TAB ORDER
        // ----------------------------------------------------
_flashEnabledCheckBox.TabIndex =
            0;


        _showDebugCheckBox.TabIndex =
            1;


        _preserveAspectRatioCheckBox.TabIndex =
            2;


        _durationTrackBar.TabIndex =
            3;


        _holdTrackBar.TabIndex =
            4;


        _opacityTrackBar.TabIndex =
            5;


        _openImagesButton.TabIndex =
            6;


        _refreshImagesButton.TabIndex =
            7;


        _openSoundsButton.TabIndex =
            8;


        _refreshSoundsButton.TabIndex =
            9;


        _soundEnabledCheckBox.TabIndex =
            10;


        _testFlashButton.TabIndex =
            11;


        _hideButton.TabIndex =
            12;


        // INITIAL LANGUAGE
        // ----------------------------------------------------
ApplyLanguage();
    }


    // --------------------------------------------------------
    // CONNECTION STATUS
    // --------------------------------------------------------
private void UpdateConnectionStatus()
    {
        bool connected =
            _overlay.Cs2Connected;


        bool ru =
            _settings.Language == "ru";


        if (connected)
        {
            _connectionDot.BackColor =
                ConnectedColor;


            _connectionLabel.ForeColor =
                ConnectedColor;


            _connectionLabel.Text =
                ru
                    ? "CS2 подключена"
                    : "CS2 connected";
        }
        else
        {
            _connectionDot.BackColor =
                DisconnectedColor;


            _connectionLabel.ForeColor =
                DisconnectedColor;


            _connectionLabel.Text =
                ru
                    ? "CS2 не подключена"
                    : "CS2 disconnected";
        }


        _connectionDot.Invalidate();

        _connectionLabel.Invalidate();
    }


    // --------------------------------------------------------
    // APPLY LANGUAGE
    // --------------------------------------------------------
private void ApplyLanguage()
    {
        bool ru =
            _settings.Language == "ru";


        _flashSectionLabel.Text =
            ru
                ? "ВСПЫШКА"
                : "FLASH";


        _flashEnabledCheckBox.Text =
            ru
                ? "Включить вспышку"
                : "Enable flash";


        _showDebugCheckBox.Text =
            ru
                ? "Показывать Debug"
                : "Show Debug";


        _preserveAspectRatioCheckBox.Text =
            ru
                ? "Сохранять пропорции"
                : "Preserve proportions";


        _durationLabel.Text =
            ru
                ? "Длительность"
                : "Duration";


        _holdLabel.Text =
            ru
                ? "Удержание"
                : "Hold";


        _opacityLabel.Text =
            ru
                ? "Прозрачность"
                : "Opacity";


        _imagesSectionLabel.Text =
            ru
                ? "ИЗОБРАЖЕНИЯ"
                : "IMAGES";


        _soundsSectionLabel.Text =
            ru
                ? "ЗВУК"
                : "SOUND";


        _soundEnabledCheckBox.Text =
            ru
                ? "Включить звук"
                : "Enable sound";


        _openImagesButton.Text =
            ru
                ? "Открыть папку"
                : "Open folder";


        _refreshImagesButton.Text =
            ru
                ? "Обновить"
                : "Refresh";


        _openSoundsButton.Text =
            ru
                ? "Открыть папку"
                : "Open folder";


        _refreshSoundsButton.Text =
            ru
                ? "Обновить"
                : "Refresh";


        _testFlashButton.Text =
            ru
                ? "Тест картинки"
                : "Test image";


        _hideButton.Text =
            ru
                ? "Свернуть в трей"
                : "Minimize to tray";


        _accentLabel.Text =
            ru
                ? "Акцент"
                : "Accent";


        UpdateImageCountLabel();
        UpdateSoundCountLabel();

        UpdateConnectionStatus();


        Invalidate(true);
    }


    // --------------------------------------------------------
    // SET ACCENT COLORS
    // --------------------------------------------------------
private static void SetAccentColors(
        Color color)
    {
        PrimaryColor =
            color;


        PrimaryHoverColor =
            LightenColor(
                color,
                20);


        PrimaryPressedColor =
            DarkenColor(
                color,
                20);
    }


    // --------------------------------------------------------
    // APPLY ACCENT COLOR
    // --------------------------------------------------------
private void ApplyAccentColor(
        Color color)
    {
        SetAccentColors(
            color);


        _testFlashButton.NormalColor =
            PrimaryColor;


        _testFlashButton.HoverColor =
            PrimaryHoverColor;


        _testFlashButton.PressedColor =
            PrimaryPressedColor;


        _testFlashButton.BorderColor =
            PrimaryColor;


        _flashSectionLabel.ForeColor =
            color;


        _imagesSectionLabel.ForeColor =
            color;


        _soundsSectionLabel.ForeColor =
            color;


        _accentColorPicker.SetColor(
            color);


        _testFlashButton.Invalidate();

        _flashSectionLabel.Invalidate();

        _imagesSectionLabel.Invalidate();

        _soundsSectionLabel.Invalidate();
    }


    // --------------------------------------------------------
    // SECTION TITLE
    // --------------------------------------------------------
private static Label CreateSectionTitle(
        string text,
        Point location)
    {
        return new Label
        {
            Text =
                text,

            AutoSize =
                true,

            Location =
                location,

            Font =
                new Font(
                    "Segoe UI",
                    8.5F,
                    FontStyle.Bold),

            ForeColor =
                PrimaryColor
        };
    }


    // --------------------------------------------------------
    // FIELD LABEL
    // --------------------------------------------------------
private static Label CreateFieldLabel(
        string text,
        Point location)
    {
        return new Label
        {
            Text =
                text,

            AutoSize =
                true,

            Location =
                location,

            ForeColor =
                SecondaryTextColor,

            Font =
                new Font(
                    "Segoe UI",
                    9.5F)
        };
    }


    // --------------------------------------------------------
    // VALUE LABEL
    // --------------------------------------------------------
private static Label CreateValueLabel(
        Point location)
    {
        return new Label
        {
            AutoSize =
                true,

            Location =
                location,

            ForeColor =
                TextColor,

            Font =
                new Font(
                    "Segoe UI",
                    9F)
        };
    }


    // --------------------------------------------------------
    // CHECKBOX
    // --------------------------------------------------------
private static CheckBox CreateDarkCheckBox(
        string text,
        Point location)
    {
        return new CheckBox
        {
            Text =
                text,

            AutoSize =
                true,

            Location =
                location,

            ForeColor =
                TextColor,

            BackColor =
                BackgroundColor,

            Font =
                new Font(
                    "Segoe UI",
                    10F),

            Cursor =
                Cursors.Hand
        };
    }


    // --------------------------------------------------------
    // TRACKBAR
    // --------------------------------------------------------
private static TrackBar CreateTrackBar(
        Point location,
        int width)
    {
        return new TrackBar
        {
            Location =
                location,

            Width =
                width,

            Height =
                42,

            BackColor =
                BackgroundColor,

            Cursor =
                Cursors.Hand
        };
    }


    // --------------------------------------------------------
    // SEPARATOR
    // --------------------------------------------------------
private static Panel CreateSeparator(
        Point location,
        int width)
    {
        return new Panel
        {
            Location =
                location,

            Size =
                new Size(
                    width,
                    1),

            BackColor =
                BorderColor
        };
    }


    // --------------------------------------------------------
    // PRIMARY BUTTON
    // --------------------------------------------------------
private static RoundedButton CreatePrimaryButton(
        string text,
        Point location,
        int width,
        int height)
    {
        return new RoundedButton
        {
            Text =
                text,

            Location =
                location,

            Width =
                width,

            Height =
                height,

            CornerRadius =
                7,

            NormalColor =
                PrimaryColor,

            HoverColor =
                PrimaryHoverColor,

            PressedColor =
                PrimaryPressedColor,

            BorderColor =
                PrimaryColor,

            BorderSize =
                0,

            TextColor =
                Color.White
        };
    }


    // --------------------------------------------------------
    // SECONDARY BUTTON
    // --------------------------------------------------------
private static RoundedButton CreateSecondaryButton(
        string text,
        Point location,
        int width,
        int height)
    {
        return new RoundedButton
        {
            Text =
                text,

            Location =
                location,

            Width =
                width,

            Height =
                height,

            CornerRadius =
                7,

            NormalColor =
                SurfaceColor,

            HoverColor =
                SurfaceHoverColor,

            PressedColor =
                Color.FromArgb(
                    58,
                    58,
                    58),

            BorderColor =
                BorderColor,

            BorderSize =
                1,

            TextColor =
                TextColor
        };
    }


    // --------------------------------------------------------
    // OPEN IMAGES FOLDER
    // --------------------------------------------------------
private void OpenImagesFolder()
    {
        try
        {
            string imagesPath =
                Path.Combine(
                    AppContext.BaseDirectory,
                    "Images");


            Directory.CreateDirectory(
                imagesPath);


            Process.Start(
                new ProcessStartInfo
                {
                    FileName =
                        "explorer.exe",

                    Arguments =
                        $"\"{imagesPath}\"",

                    UseShellExecute =
                        true
                });
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,

                _settings.Language == "ru"
                    ? $"Не удалось открыть папку:\n\n{ex.Message}"
                    : $"Could not open folder:\n\n{ex.Message}",

                "CS2 Kill Overlay",

                MessageBoxButtons.OK,

                MessageBoxIcon.Error);
        }
    }


    // --------------------------------------------------------
    // REFRESH IMAGES
    // --------------------------------------------------------
private void RefreshImages()
    {
        int count =
            _overlay.RefreshImages();


        UpdateImageCountLabel(
            count);
    }


    // --------------------------------------------------------
    // SOUND COUNT
    // --------------------------------------------------------
private void UpdateSoundCountLabel()
    {
        int count =
            _overlay.SoundCount;


        _soundCountLabel.Text =
            _settings.Language == "ru"
                ? $"Найдено: {Math.Min(count, 1)}"
                : $"Found: {Math.Min(count, 1)}";
    }


    // --------------------------------------------------------
    // OPEN SOUNDS FOLDER
    // --------------------------------------------------------
private void OpenSoundsFolder()
    {
        try
        {
            string soundsPath =
                Path.Combine(
                    AppContext.BaseDirectory,
                    "Sounds");


            Directory.CreateDirectory(
                soundsPath);


            Process.Start(
                new ProcessStartInfo
                {
                    FileName =
                        "explorer.exe",

                    Arguments =
                        $"\"{soundsPath}\"",

                    UseShellExecute =
                        true
                });
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,

                _settings.Language == "ru"
                    ? $"Не удалось открыть папку:\n\n{ex.Message}"
                    : $"Could not open folder:\n\n{ex.Message}",

                "Image Flasher",

                MessageBoxButtons.OK,

                MessageBoxIcon.Error);
        }
    }


    // --------------------------------------------------------
    // REFRESH SOUNDS
    // --------------------------------------------------------
private void RefreshSounds()
    {
        int count =
            _overlay.RefreshSounds();


        UpdateSoundCountLabel();

        _ = count;
    }


    // --------------------------------------------------------
    // IMAGE COUNT
    // --------------------------------------------------------
private void UpdateImageCountLabel()
    {
        UpdateImageCountLabel(
            _overlay.ImageCount);
    }


    private void UpdateImageCountLabel(
        int count)
    {
        _imageCountLabel.Text =
            _settings.Language == "ru"
                ? $"Найдено: {count}"
                : $"Found: {count}";
    }


    // --------------------------------------------------------
    // DURATION
    // --------------------------------------------------------
private void UpdateDurationLabel()
    {
        _durationValueLabel.Text =
            $"{_durationTrackBar.Value} ms";
    }


    // --------------------------------------------------------
    // HOLD
    // --------------------------------------------------------
private void UpdateHoldLabel()
    {
        _holdValueLabel.Text =
            $"{_holdTrackBar.Value} ms";
    }


    // --------------------------------------------------------
    // OPACITY
    // --------------------------------------------------------
private void UpdateOpacityLabel()
    {
        _opacityValueLabel.Text =
            $"{_opacityTrackBar.Value}%";
    }


    // --------------------------------------------------------
    // SAVE
    // --------------------------------------------------------
private void SaveAndRefresh()
    {
        _settings.Save();

        _overlay.RenderSettingsChanged();
    }


    // --------------------------------------------------------
    // ALLOW IMMEDIATE CLOSE
    // --------------------------------------------------------
public void AllowImmediateClose()
    {
        _closingForExit = true;
    }


    // --------------------------------------------------------
    // CLOSE
    // --------------------------------------------------------
protected override void OnFormClosing(
        FormClosingEventArgs e)
    {
        if (_closingForExit)
        {
            base.OnFormClosing(e);

            return;
        }


        e.Cancel = true;


        bool ru =
            _settings.Language == "ru";


        var result =
            MessageBox.Show(
                ru
                    ? "Закрыть программу полностью?\n\n" +
                      "Да — закрыть программу.\n" +
                      "Нет — оставить её работать в трее."
                    : "Close the program completely?\n\n" +
                      "Yes — close the program.\n" +
                      "No — keep it running in the tray.",

                "CS2 Kill Overlay",

                MessageBoxButtons.YesNo,

                MessageBoxIcon.Question);


        if (result ==
            DialogResult.Yes)
        {
            _closingForExit = true;

            _settings.Save();

            _requestExit();

            return;
        }


        Hide();


        try
        {
            _overlay.Show();

            _overlay.SyncNow();
        }
        catch
        {
        }
    }


    // --------------------------------------------------------
    // DISPOSE
    // --------------------------------------------------------
protected override void Dispose(
        bool disposing)
    {
        if (disposing)
        {
            try
            {
                _connectionTimer.Stop();

                _connectionTimer.Dispose();
            }
            catch
            {
            }
        }


        base.Dispose(disposing);
    }


    // --------------------------------------------------------
    // COLOR HELPERS
    // --------------------------------------------------------
private static Color ParseAccentColor(
        string value)
    {
        try
        {
            return ColorTranslator.FromHtml(
                value);
        }
        catch
        {
            return Color.FromArgb(
                0,
                120,
                215);
        }
    }


    private static string ColorToHex(
        Color color)
    {
        return
            $"#{color.R:X2}" +
            $"{color.G:X2}" +
            $"{color.B:X2}";
    }


    private static Color LightenColor(
        Color color,
        int amount)
    {
        return Color.FromArgb(
            Math.Min(
                255,
                color.R + amount),

            Math.Min(
                255,
                color.G + amount),

            Math.Min(
                255,
                color.B + amount));
    }


    private static Color DarkenColor(
        Color color,
        int amount)
    {
        return Color.FromArgb(
            Math.Max(
                0,
                color.R - amount),

            Math.Max(
                0,
                color.G - amount),

            Math.Max(
                0,
                color.B - amount));
    }


    // --------------------------------------------------------
    // CLAMP
    // --------------------------------------------------------
private static int Clamp(
        int value,
        int min,
        int max)
    {
        if (value < min)
            return min;


        if (value > max)
            return max;


        return value;
    }
}


// ============================================================
// ROUNDED BUTTON
// ============================================================
sealed class RoundedButton : Control
{
    private bool _isHovered;

    private bool _isPressed;


    public int CornerRadius { get; set; } = 7;


    public Color NormalColor { get; set; } =
        Color.FromArgb(
            45,
            45,
            45);


    public Color HoverColor { get; set; } =
        Color.FromArgb(
            52,
            52,
            52);


    public Color PressedColor { get; set; } =
        Color.FromArgb(
            58,
            58,
            58);


    public Color BorderColor { get; set; } =
        Color.FromArgb(
            64,
            64,
            64);


    public int BorderSize { get; set; } = 1;


    public Color TextColor { get; set; } =
        Color.White;


    public RoundedButton()
    {
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.Selectable,
            true);


        TabStop =
            true;


        Cursor =
            Cursors.Hand;
    }


    protected override void OnMouseEnter(
        EventArgs e)
    {
        base.OnMouseEnter(e);

        _isHovered = true;

        Invalidate();
    }


    protected override void OnMouseLeave(
        EventArgs e)
    {
        base.OnMouseLeave(e);

        _isHovered = false;

        _isPressed = false;

        Invalidate();
    }


    protected override void OnMouseDown(
        MouseEventArgs e)
    {
        base.OnMouseDown(e);


        if (e.Button ==
            MouseButtons.Left)
        {
            _isPressed = true;

            Invalidate();
        }
    }


    protected override void OnMouseUp(
        MouseEventArgs e)
    {
        base.OnMouseUp(e);

        if (e.Button ==
            MouseButtons.Left)
        {
            _isPressed = false;

            Invalidate();
        }
    }


    protected override void OnKeyDown(
        KeyEventArgs e)
    {
        base.OnKeyDown(e);


        if (e.KeyCode ==
                Keys.Space ||
            e.KeyCode ==
                Keys.Enter)
        {
            _isPressed = true;

            Invalidate();
        }
    }


    protected override void OnKeyUp(
        KeyEventArgs e)
    {
        base.OnKeyUp(e);

        if (e.KeyCode ==
            Keys.Space ||
            e.KeyCode ==
            Keys.Enter)
        {
            _isPressed = false;

            Invalidate();

            OnClick(
                EventArgs.Empty);
        }
    }


    protected override void OnPaint(
        PaintEventArgs e)
    {
        Graphics g =
            e.Graphics;


        g.SmoothingMode =
            SmoothingMode.AntiAlias;


        g.PixelOffsetMode =
            PixelOffsetMode.HighQuality;


        g.CompositingMode =
            CompositingMode.SourceOver;


        Color backgroundColor;


        if (_isPressed)
        {
            backgroundColor =
                PressedColor;
        }
        else if (_isHovered)
        {
            backgroundColor =
                HoverColor;
        }
        else
        {
            backgroundColor =
                NormalColor;
        }


        float border =
            BorderSize > 0
                ? BorderSize
                : 0;


        float x =
            border / 2f;


        float y =
            border / 2f;


        float width =
            Width - border;


        float height =
            Height - border;


        if (width <= 0 ||
            height <= 0)
        {
            return;
        }


        float radius =
            Math.Min(
                CornerRadius,
                Math.Min(
                    width,
                    height) / 2f);


        RectangleF rect =
            new RectangleF(
                x,
                y,
                width,
                height);


        using GraphicsPath path =
            CreateRoundedRectangle(
                rect,
                radius);


        using SolidBrush fillBrush =
            new SolidBrush(
                backgroundColor);


        g.FillPath(
            fillBrush,
            path);


        if (BorderSize > 0)
        {
            using Pen borderPen =
                new Pen(
                    BorderColor,
                    BorderSize);


            borderPen.Alignment =
                PenAlignment.Inset;


            g.DrawPath(
                borderPen,
                path);
        }


        TextFormatFlags flags =
            TextFormatFlags.HorizontalCenter |
            TextFormatFlags.VerticalCenter |
            TextFormatFlags.SingleLine |
            TextFormatFlags.NoPrefix;


        TextRenderer.DrawText(
            g,
            Text,
            Font,
            ClientRectangle,
            TextColor,
            flags);
    }


    private static GraphicsPath CreateRoundedRectangle(
        RectangleF rect,
        float radius)
    {
        var path =
            new GraphicsPath();


        if (radius <= 0)
        {
            path.AddRectangle(rect);

            return path;
        }


        float diameter =
            radius * 2f;


        path.AddArc(
            rect.X,
            rect.Y,
            diameter,
            diameter,
            180,
            90);


        path.AddArc(
            rect.Right - diameter,
            rect.Y,
            diameter,
            diameter,
            270,
            90);


        path.AddArc(
            rect.Right - diameter,
            rect.Bottom - diameter,
            diameter,
            diameter,
            0,
            90);


        path.AddArc(
            rect.X,
            rect.Bottom - diameter,
            diameter,
            diameter,
            90,
            90);


        path.CloseFigure();


        return path;
    }
}


// ============================================================
// LANGUAGE TOGGLE
// ============================================================
sealed class LanguageToggle : Control
{
    private bool _hovered;

    private string _language;


    public event Action<string>? LanguageChanged;


    public LanguageToggle(
        string language)
    {
        _language =
            language == "en"
                ? "en"
                : "ru";


        Cursor =
            Cursors.Hand;


        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw,
            true);
    }


    protected override void OnMouseEnter(
        EventArgs e)
    {
        base.OnMouseEnter(e);

        _hovered = true;

        Invalidate();
    }


    protected override void OnMouseLeave(
        EventArgs e)
    {
        base.OnMouseLeave(e);

        _hovered = false;

        Invalidate();
    }


    protected override void OnMouseClick(
        MouseEventArgs e)
    {
        base.OnMouseClick(e);


        if (e.Button !=
            MouseButtons.Left)
        {
            return;
        }


        _language =
            _language == "en"
                ? "ru"
                : "en";


        LanguageChanged?.Invoke(
            _language);


        Invalidate();
    }


    protected override void OnPaint(
        PaintEventArgs e)
    {
        Graphics g =
            e.Graphics;


        g.SmoothingMode =
            SmoothingMode.AntiAlias;


        Color background =
            _hovered
                ? Color.FromArgb(
                    50,
                    50,
                    50)
                : Color.FromArgb(
                    42,
                    42,
                    42);


        using var backgroundBrush =
            new SolidBrush(
                background);


        using var backgroundPath =
            CreateRoundedPath(
                new RectangleF(
                    0,
                    0,
                    Width - 1,
                    Height - 1),
                7);


        g.FillPath(
            backgroundBrush,
            backgroundPath);


        using var borderPen =
            new Pen(
                Color.FromArgb(
                    70,
                    70,
                    70),
                1);


        g.DrawPath(
            borderPen,
            backgroundPath);


        Rectangle flagRect =
            new Rectangle(
                7,
                7,
                26,
                16);


        using var clipPath =
            CreateRoundedPath(
                flagRect,
                2);


        GraphicsState state =
            g.Save();


        g.SetClip(
            clipPath);


        if (_language == "en")
        {
            DrawUnitedKingdomFlag(
                g,
                flagRect);
        }
        else
        {
            DrawRussianFlag(
                g,
                flagRect);
        }


        g.Restore(state);


        string text =
            _language == "en"
                ? "EN"
                : "RU";


        using var font =
            new Font(
                "Segoe UI",
                8F,
                FontStyle.Bold);


        TextRenderer.DrawText(
            g,
            text,
            font,
            new Rectangle(
                37,
                0,
                Width - 38,
                Height),
            Color.FromArgb(
                225,
                225,
                225),
            TextFormatFlags.HorizontalCenter |
            TextFormatFlags.VerticalCenter |
            TextFormatFlags.SingleLine);
    }


    private static void DrawUnitedKingdomFlag(
        Graphics g,
        Rectangle rect)
    {
        using var blueBrush =
            new SolidBrush(
                Color.FromArgb(
                    30,
                    55,
                    140));


        g.FillRectangle(
            blueBrush,
            rect);


        using var whitePen =
            new Pen(
                Color.White,
                4);


        g.DrawLine(
            whitePen,
            rect.Left,
            rect.Top,
            rect.Right,
            rect.Bottom);


        g.DrawLine(
            whitePen,
            rect.Right,
            rect.Top,
            rect.Left,
            rect.Bottom);


        using var redPen =
            new Pen(
                Color.FromArgb(
                    210,
                    25,
                    45),
                2);


        g.DrawLine(
            redPen,
            rect.Left,
            rect.Top,
            rect.Right,
            rect.Bottom);


        g.DrawLine(
            redPen,
            rect.Right,
            rect.Top,
            rect.Left,
            rect.Bottom);


        using var whiteCross =
            new Pen(
                Color.White,
                4);


        g.DrawLine(
            whiteCross,
            rect.Left +
            rect.Width / 2,
            rect.Top,
            rect.Left +
            rect.Width / 2,
            rect.Bottom);


        g.DrawLine(
            whiteCross,
            rect.Left,
            rect.Top +
            rect.Height / 2,
            rect.Right,
            rect.Top +
            rect.Height / 2);


        using var redCross =
            new Pen(
                Color.FromArgb(
                    210,
                    25,
                    45),
                2);


        g.DrawLine(
            redCross,
            rect.Left +
            rect.Width / 2,
            rect.Top,
            rect.Left +
            rect.Width / 2,
            rect.Bottom);


        g.DrawLine(
            redCross,
            rect.Left,
            rect.Top +
            rect.Height / 2,
            rect.Right,
            rect.Top +
            rect.Height / 2);
    }


    private static void DrawRussianFlag(
        Graphics g,
        Rectangle rect)
    {
        int stripeHeight =
            rect.Height / 3;


        using var whiteBrush =
            new SolidBrush(
                Color.White);


        using var blueBrush =
            new SolidBrush(
                Color.FromArgb(
                    40,
                    90,
                    180));


        using var redBrush =
            new SolidBrush(
                Color.FromArgb(
                    210,
                    35,
                    50));


        g.FillRectangle(
            whiteBrush,
            rect.Left,
            rect.Top,
            rect.Width,
            stripeHeight);


        g.FillRectangle(
            blueBrush,
            rect.Left,
            rect.Top +
            stripeHeight,
            rect.Width,
            stripeHeight);


        g.FillRectangle(
            redBrush,
            rect.Left,
            rect.Top +
            stripeHeight * 2,
            rect.Width,
            rect.Height -
            stripeHeight * 2);
    }


    private static GraphicsPath CreateRoundedPath(
        RectangleF rect,
        float radius)
    {
        var path =
            new GraphicsPath();


        float diameter =
            radius * 2;


        path.AddArc(
            rect.X,
            rect.Y,
            diameter,
            diameter,
            180,
            90);


        path.AddArc(
            rect.Right - diameter,
            rect.Y,
            diameter,
            diameter,
            270,
            90);


        path.AddArc(
            rect.Right - diameter,
            rect.Bottom - diameter,
            diameter,
            diameter,
            0,
            90);


        path.AddArc(
            rect.X,
            rect.Bottom - diameter,
            diameter,
            diameter,
            90,
            90);


        path.CloseFigure();


        return path;
    }
}


// ============================================================
// ACCENT COLOR PICKER
// ============================================================
sealed class AccentColorPicker : Control
{
    private bool _hovered;


    public Color AccentColor
    {
        get;
        private set;
    }


    public event Action<Color>? ColorSelected;


    private static readonly Color[] Palette =
    {
        Color.FromArgb(
            0,
            120,
            215),

        Color.FromArgb(
            0,
            153,
            188),

        Color.FromArgb(
            135,
            100,
            184),

        Color.FromArgb(
            195,
            75,
            130),

        Color.FromArgb(
            220,
            80,
            80),

        Color.FromArgb(
            220,
            140,
            45),

        Color.FromArgb(
            110,
            170,
            70),

        Color.FromArgb(
            45,
            175,
            145)
    };


    public AccentColorPicker(
        Color initialColor)
    {
        AccentColor =
            initialColor;


        Width =
            24;


        Height =
            24;


        Cursor =
            Cursors.Hand;


        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw,
            true);
    }


    protected override void OnMouseEnter(
        EventArgs e)
    {
        base.OnMouseEnter(e);

        _hovered = true;

        Invalidate();
    }


    protected override void OnMouseLeave(
        EventArgs e)
    {
        base.OnMouseLeave(e);

        _hovered = false;

        Invalidate();
    }


    protected override void OnMouseClick(
        MouseEventArgs e)
    {
        base.OnMouseClick(e);


        if (e.Button !=
            MouseButtons.Left)
        {
            return;
        }


        ShowPalette();
    }


    protected override void OnPaint(
        PaintEventArgs e)
    {
        Graphics g =
            e.Graphics;


        g.SmoothingMode =
            SmoothingMode.AntiAlias;


        float padding =
            _hovered
                ? 1f
                : 2f;


        RectangleF rect =
            new RectangleF(
                padding,
                padding,
                Width - padding * 2,
                Height - padding * 2);


        using var brush =
            new SolidBrush(
                AccentColor);


        g.FillEllipse(
            brush,
            rect);


        using var border =
            new Pen(
                Color.FromArgb(
                    180,
                    255,
                    255,
                    255),
                1);


        g.DrawEllipse(
            border,
            rect);
    }


    public void SetColor(
        Color color)
    {
        AccentColor =
            color;


        Invalidate();
    }


    private void ShowPalette()
    {
        string language =
            "ru";


        if (FindForm() is SettingsForm form)
        {
            language =
                form.IsRussian
                    ? "ru"
                    : "en";
        }


        using var popup =
            new AccentPaletteForm(
                AccentColor,
                Palette,
                language);


        Point screenPosition =
            PointToScreen(
                new Point(
                    Width / 2,
                    0));


        popup.StartPosition =
            FormStartPosition.Manual;


        int popupX =
            screenPosition.X -
            popup.Width +
            Width;


        int popupY =
            screenPosition.Y -
            popup.Height -
            8;


        Rectangle workingArea =
            Screen.FromControl(
                this)
            .WorkingArea;


        if (popupX < workingArea.Left)
        {
            popupX =
                workingArea.Left;
        }


        if (popupX +
            popup.Width >
            workingArea.Right)
        {
            popupX =
                workingArea.Right -
                popup.Width;
        }


        if (popupY < workingArea.Top)
        {
            popupY =
                screenPosition.Y +
                Height +
                8;
        }


        popup.Location =
            new Point(
                popupX,
                popupY);


        popup.ColorSelected +=
            color =>
            {
                SetColor(
                    color);


                ColorSelected?.Invoke(
                    color);
            };


        popup.ShowDialog(
            FindForm());
    }
}


// ============================================================
// ACCENT PALETTE
// ============================================================
sealed class AccentPaletteForm : Form
{
    private readonly Color[] _colors;

    private Color _currentColor;

    private readonly string _language;


    public event Action<Color>? ColorSelected;


    private static readonly Color BackgroundColor =
        Color.FromArgb(
            42,
            42,
            42);


    private static readonly Color BorderColor =
        Color.FromArgb(
            70,
            70,
            70);


    private static readonly Color TextColor =
        Color.FromArgb(
            220,
            220,
            220);


    public AccentPaletteForm(
        Color currentColor,
        Color[] colors,
        string language)
    {
        _currentColor =
            currentColor;


        _colors =
            colors;


        _language =
            language;


        FormBorderStyle =
            FormBorderStyle.None;


        ShowInTaskbar =
            false;


        ShowIcon =
            false;


        StartPosition =
            FormStartPosition.Manual;


        TopMost =
            true;


        BackColor =
            BackgroundColor;


        ClientSize =
            new Size(
                210,
                75);


        Deactivate +=
            (_, _) =>
            {
                Close();
            };
    }


    protected override void OnPaint(
        PaintEventArgs e)
    {
        Graphics g =
            e.Graphics;


        g.SmoothingMode =
            SmoothingMode.AntiAlias;


        using var backgroundBrush =
            new SolidBrush(
                BackgroundColor);


        g.FillRectangle(
            backgroundBrush,
            ClientRectangle);


        using var borderPen =
            new Pen(
                BorderColor,
                1);


        g.DrawRectangle(
            borderPen,
            0,
            0,
            Width - 1,
            Height - 1);


        using var titleBrush =
            new SolidBrush(
                TextColor);


        using var titleFont =
            new Font(
                "Segoe UI",
                8.5F);


        string title =
            _language == "ru"
                ? "Акцентный цвет"
                : "Accent color";


        g.DrawString(
            title,
            titleFont,
            titleBrush,
            10,
            7);


        const int size =
            22;


        const int gap =
            4;


        const int columns =
            8;


        for (int i = 0;
             i < _colors.Length;
             i++)
        {
            int column =
                i % columns;


            int x =
                10 +
                column *
                (size + gap);


            int y =
                34;


            Rectangle outer =
                new Rectangle(
                    x,
                    y,
                    size,
                    size);


            bool selected =
                ColorsEqual(
                    _colors[i],
                    _currentColor);


            if (selected)
            {
                using var selectedPen =
                    new Pen(
                        Color.White,
                        2);


                g.DrawEllipse(
                    selectedPen,
                    outer);
            }


            Rectangle inner =
                new Rectangle(
                    x + 2,
                    y + 2,
                    size - 4,
                    size - 4);


            using var colorBrush =
                new SolidBrush(
                    _colors[i]);


            g.FillEllipse(
                colorBrush,
                inner);
        }
    }


    protected override void OnMouseClick(
        MouseEventArgs e)
    {
        base.OnMouseClick(e);


        if (e.Button !=
            MouseButtons.Left)
        {
            return;
        }


        const int size =
            22;


        const int gap =
            4;


        const int columns =
            8;


        for (int i = 0;
             i < _colors.Length;
             i++)
        {
            int column =
                i % columns;


            int x =
                10 +
                column *
                (size + gap);


            int y =
                34;


            Rectangle rect =
                new Rectangle(
                    x,
                    y,
                    size,
                    size);


            if (!rect.Contains(
                    e.Location))
            {
                continue;
            }


            _currentColor =
                _colors[i];


            ColorSelected?.Invoke(
                _currentColor);


            Close();

            return;
        }
    }


    private static bool ColorsEqual(
        Color a,
        Color b)
    {
        return
            a.R == b.R &&
            a.G == b.G &&
            a.B == b.B;
    }
}


// ============================================================
// FLASH OVERLAY
// ============================================================
sealed class FlashOverlay : Form
{
    private readonly AppSettings _settings;


    private readonly System.Windows.Forms.Timer
        _syncTimer;


    private readonly System.Windows.Forms.Timer
        _flashTimer;


    private Rectangle _cs2Rect;

    private IntPtr _cs2WindowHandle;


    private Bitmap? _bitmap;


    private Bitmap? _currentImage;

    private string? _soundFile;

    private static int _soundInstanceId;

    // Adaptive background used when the image keeps its aspect ratio.
    // It is built once per image / overlay size and reused during the flash.
    private Bitmap? _adaptiveBackground;

    private int _adaptiveBackgroundWidth;
    private int _adaptiveBackgroundHeight;

    // Cached GDI resources used by UpdateLayeredWindow.
    private IntPtr _layeredMemDc;
    private IntPtr _layeredHBitmap;
    private IntPtr _layeredOldBitmap;
    private int _layeredBitmapWidth;
    private int _layeredBitmapHeight;


    private bool _flashActive;


    private long _flashStartTimestamp;


    private double _flashAlpha;


    private readonly Random _random =
        new Random();


    private List<string> _imageFiles =
        new List<string>();


    private List<string> _shuffledImages =
        new List<string>();


    private int _shuffledIndex;


    private string? _lastShownImage;


    // --------------------------------------------------------
    // CONNECTION STATUS
    // --------------------------------------------------------

    private bool _cs2Connected;


    public bool Cs2Connected =>
        _cs2Connected;


    // --------------------------------------------------------
    // CONSTANTS
    // --------------------------------------------------------
private const int WS_EX_LAYERED =
        0x00080000;


    private const int WS_EX_TRANSPARENT =
        0x00000020;


    private const int WS_EX_NOACTIVATE =
        0x08000000;


    private const int WS_EX_TOOLWINDOW =
        0x00000080;


    private const int HWND_TOPMOST =
        -1;


    private const uint SWP_NOACTIVATE =
        0x0010;


    private const uint SWP_NOOWNERZORDER =
        0x0200;


    private const uint SWP_SHOWWINDOW =
        0x0040;


    private const int ULW_ALPHA =
        0x00000002;


    private const byte AC_SRC_OVER =
        0x00;


    private const byte AC_SRC_ALPHA =
        0x01;


    private const int WM_NCHITTEST =
        0x0084;


    private const int HTTRANSPARENT =
        -1;


    // --------------------------------------------------------
    // IMAGE FORMATS
    // --------------------------------------------------------
private static readonly string[] SupportedExtensions =
    {
        ".png",
        ".jpg",
        ".jpeg",
        ".bmp",
        ".gif",
        ".tif",
        ".tiff"
    };


    private static readonly string[] SupportedSoundExtensions =
    {
        ".wav",
        ".mp3"
    };


    // --------------------------------------------------------
    // NATIVE STRUCTS
    // --------------------------------------------------------
[StructLayout(
        LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;

        public int Y;
    }


    [StructLayout(
        LayoutKind.Sequential)]
    private struct SIZE
    {
        public int CX;

        public int CY;
    }


    [StructLayout(
        LayoutKind.Sequential,
        Pack = 1)]
    private struct BLENDFUNCTION
    {
        public byte BlendOp;

        public byte BlendFlags;

        public byte SourceConstantAlpha;

        public byte AlphaFormat;
    }


    [StructLayout(
        LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;

        public int Top;

        public int Right;

        public int Bottom;
    }


    // --------------------------------------------------------
    // P/INVOKE
    // --------------------------------------------------------
[DllImport(
        "user32.dll",
        SetLastError = true)]
    private static extern bool GetWindowRect(
        IntPtr hWnd,
        out RECT lpRect);


    [DllImport(
        "user32.dll")]
    private static extern IntPtr GetForegroundWindow();


    [DllImport(
        "winmm.dll",
        CharSet = CharSet.Unicode)]
    private static extern int mciSendString(
        string command,
        StringBuilder? returnValue,
        int returnLength,
        IntPtr callback);


    [DllImport(
        "winmm.dll",
        CharSet = CharSet.Unicode)]
    private static extern bool mciGetErrorString(
        int errorCode,
        StringBuilder errorText,
        int errorTextLength);


    [DllImport(
        "user32.dll",
        SetLastError = true)]
    private static extern bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int X,
        int Y,
        int CX,
        int CY,
        uint uFlags);


    [DllImport(
        "user32.dll",
        SetLastError = true)]
    private static extern IntPtr GetDC(
        IntPtr hWnd);


    [DllImport(
        "user32.dll",
        SetLastError = true)]
    private static extern int ReleaseDC(
        IntPtr hWnd,
        IntPtr hDC);


    [DllImport(
        "gdi32.dll",
        SetLastError = true)]
    private static extern IntPtr CreateCompatibleDC(
        IntPtr hdc);


    [DllImport(
        "gdi32.dll",
        SetLastError = true)]
    private static extern bool DeleteDC(
        IntPtr hdc);


    [DllImport(
        "gdi32.dll",
        SetLastError = true)]
    private static extern IntPtr SelectObject(
        IntPtr hdc,
        IntPtr hgdiobj);


    [DllImport(
        "gdi32.dll",
        SetLastError = true)]
    private static extern bool DeleteObject(
        IntPtr hObject);


    [DllImport(
        "user32.dll",
        SetLastError = true)]
    private static extern bool UpdateLayeredWindow(
        IntPtr hwnd,
        IntPtr hdcDst,
        ref POINT pptDst,
        ref SIZE psize,
        IntPtr hdcSrc,
        ref POINT pptSrc,
        int crKey,
        ref BLENDFUNCTION pblend,
        int dwFlags);


    // --------------------------------------------------------
    // CONSTRUCTOR
    // --------------------------------------------------------
public FlashOverlay(
        AppSettings settings)
    {
        _settings = settings;


        FormBorderStyle =
            FormBorderStyle.None;


        ShowInTaskbar =
            false;


        StartPosition =
            FormStartPosition.Manual;


        TopMost =
            true;


        BackColor =
            Color.Black;


        Text =
            "CS2 Kill Overlay";


        Size =
            new Size(
                1,
                1);


        Location =
            new Point(
                0,
                0);


        // ----------------------------------------------------
        // SYNC TIMER
        // ----------------------------------------------------

        _syncTimer =
            new System.Windows.Forms.Timer
            {
                Interval = 100
            };


        _syncTimer.Tick +=
            (_, _) =>
            {
                UpdateCs2Rect();
            };


        // ----------------------------------------------------
        // FLASH TIMER
        // ----------------------------------------------------

        _flashTimer =
            new System.Windows.Forms.Timer
            {
                Interval = 15
            };


        _flashTimer.Tick +=
            (_, _) =>
            {
                OnFlashTimer();
            };


        CreateControl();


        RefreshImages();
        RefreshSounds();


        _syncTimer.Start();
    }


    // --------------------------------------------------------
    // CREATE PARAMS
    // --------------------------------------------------------
protected override CreateParams CreateParams
    {
        get
        {
            var cp =
                base.CreateParams;


            cp.ExStyle |=
                WS_EX_LAYERED |
                WS_EX_TRANSPARENT |
                WS_EX_NOACTIVATE |
                WS_EX_TOOLWINDOW;


            return cp;
        }
    }


    // --------------------------------------------------------
    // CLICK-THROUGH
    // --------------------------------------------------------
protected override void WndProc(
        ref Message m)
    {
        if (m.Msg ==
            WM_NCHITTEST)
        {
            m.Result =
                new IntPtr(
                    HTTRANSPARENT);

            return;
        }


        base.WndProc(
            ref m);
    }


    // --------------------------------------------------------
    // IMAGE COUNT
    // --------------------------------------------------------
public int ImageCount =>
        _imageFiles.Count;


    // --------------------------------------------------------
    // SOUND COUNT
    // --------------------------------------------------------
public int SoundCount =>
        string.IsNullOrWhiteSpace(_soundFile)
            ? 0
            : 1;


    // --------------------------------------------------------
    // REFRESH SOUNDS
    // --------------------------------------------------------
public int RefreshSounds()
    {
        if (InvokeRequired)
        {
            return (int)Invoke(
                new Func<int>(
                    RefreshSounds));
        }


        try
        {
            string soundsPath =
                Path.Combine(
                    AppContext.BaseDirectory,
                    "Sounds");


            Directory.CreateDirectory(
                soundsPath);


            string? newSound =
                Directory
                    .EnumerateFiles(
                        soundsPath,
                        "*.*",
                        SearchOption.TopDirectoryOnly)
                    .Where(
                        file =>
                            SupportedSoundExtensions.Contains(
                                Path.GetExtension(file),
                                StringComparer.OrdinalIgnoreCase))
                    .OrderBy(
                        file => file,
                        StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault();


            if (string.Equals(
                    newSound,
                    _soundFile,
                    StringComparison.OrdinalIgnoreCase))
            {
                return SoundCount;
            }


            _soundFile = null;


            if (string.IsNullOrWhiteSpace(newSound))
            {
                Logger.Info("SOUND", "No supported sound file found");
                return 0;
            }


            _soundFile = newSound;

            Logger.Info(
                "SOUND",
                $"Sound selected | File={Path.GetFileName(_soundFile)} | Format={Path.GetExtension(_soundFile)}");

            return 1;
        }
        catch (Exception ex)
        {
            Logger.Error(
                "SOUND",
                "Failed to refresh sound",
                ex);

            _soundFile = null;

            return 0;
        }
    }


    // --------------------------------------------------------
    // REFRESH IMAGES
    // --------------------------------------------------------
public int RefreshImages()
    {
        if (InvokeRequired)
        {
            return (int)Invoke(
                new Func<int>(
                    RefreshImages));
        }


        var newFiles =
            new List<string>();


        try
        {
            string imagesPath =
                Path.Combine(
                    AppContext.BaseDirectory,
                    "Images");


            Directory.CreateDirectory(
                imagesPath);


            string[] files =
                Directory.GetFiles(
                    imagesPath);


            foreach (string file in files)
            {
                string extension =
                    Path.GetExtension(file);


                if (SupportedExtensions.Contains(
                        extension,
                        StringComparer.OrdinalIgnoreCase))
                {
                    newFiles.Add(
                        Path.GetFullPath(
                            file));
                }
            }


            newFiles.Sort(
                StringComparer.OrdinalIgnoreCase);


            _imageFiles =
                newFiles;


            CreateNewShuffle();


            Logger.Info(
                "IMAGE",
                $"Images refreshed | Count={_imageFiles.Count}");

            if (_imageFiles.Count == 0)
            {
                Logger.Info("IMAGE", "No supported images found; white flash fallback is active");
            }


            return _imageFiles.Count;
        }
        catch (Exception ex)
        {
            Logger.Error(
                "IMAGE",
                "Failed to refresh images",
                ex);


            _imageFiles =
                newFiles;


            CreateNewShuffle();


            return _imageFiles.Count;
        }
    }


    // --------------------------------------------------------
    // CREATE SHUFFLE
    // --------------------------------------------------------
private void CreateNewShuffle()
    {
        _shuffledImages =
            new List<string>(
                _imageFiles);


        for (int i =
                 _shuffledImages.Count - 1;
             i > 0;
             i--)
        {
            int j =
                _random.Next(
                    i + 1);


            (
                _shuffledImages[i],
                _shuffledImages[j]
            ) =
            (
                _shuffledImages[j],
                _shuffledImages[i]
            );
        }


        // The new shuffle cycle must not start with the image shown last in the previous cycle.


        if (_shuffledImages.Count > 1 &&
            !string.IsNullOrWhiteSpace(
                _lastShownImage) &&
            string.Equals(
                _shuffledImages[0],
                _lastShownImage,
                StringComparison.OrdinalIgnoreCase))
        {
            for (int i = 1;
                 i < _shuffledImages.Count;
                 i++)
            {
                if (!string.Equals(
                        _shuffledImages[i],
                        _lastShownImage,
                        StringComparison.OrdinalIgnoreCase))
                {
                    (
                        _shuffledImages[0],
                        _shuffledImages[i]
                    ) =
                    (
                        _shuffledImages[i],
                        _shuffledImages[0]
                    );

                    break;
                }
            }
        }


        _shuffledIndex =
            0;
    }


    // --------------------------------------------------------
    // SYNC NOW
    // --------------------------------------------------------
public void SyncNow()
    {
        if (InvokeRequired)
        {
            BeginInvoke(
                new Action(
                    SyncNow));

            return;
        }


        UpdateCs2Rect();
    }


    // ========================================================
    // PLAY SOUND
    // ========================================================

    private static string GetMciErrorText(
        int errorCode)
    {
        StringBuilder buffer =
            new StringBuilder(256);

        return mciGetErrorString(
                   errorCode,
                   buffer,
                   buffer.Capacity)
            ? buffer.ToString()
            : $"MCI error code {errorCode}";
    }


    private void PlaySound()
    {
        if (string.IsNullOrWhiteSpace(_soundFile))
        {
            Logger.Debug("SOUND", "Play skipped | No sound file loaded");
            return;
        }

        try
        {
            string alias =
                $"ImageFlasherSound_{Interlocked.Increment(
                    ref _soundInstanceId)}";

            string escapedPath =
                _soundFile.Replace("\"", "\"\"");

            string extension =
                Path.GetExtension(_soundFile);

            string openCommand =
                extension.Equals(
                    ".mp3",
                    StringComparison.OrdinalIgnoreCase)
                    ? $"open \"{escapedPath}\" type mpegvideo alias {alias}"
                    : $"open \"{escapedPath}\" type waveaudio alias {alias}";

            int openResult =
                mciSendString(
                    openCommand,
                    null,
                    0,
                    IntPtr.Zero);

            if (openResult != 0)
            {
                Logger.Warning(
                    "SOUND",
                    $"MCI open failed | File={Path.GetFileName(_soundFile)} | Code={openResult} | Error={GetMciErrorText(openResult)}");
                return;
            }

            StringBuilder lengthBuffer =
                new StringBuilder(32);

            mciSendString(
                $"status {alias} length",
                lengthBuffer,
                lengthBuffer.Capacity,
                IntPtr.Zero);

            int lengthMs =
                int.TryParse(
                    lengthBuffer.ToString(),
                    out int parsedLength)
                    ? parsedLength
                    : 1000;

            int playResult =
                mciSendString(
                    $"play {alias}",
                    null,
                    0,
                    IntPtr.Zero);

            if (playResult != 0)
            {
                Logger.Warning(
                    "SOUND",
                    $"MCI play failed | File={Path.GetFileName(_soundFile)} | Code={playResult} | Error={GetMciErrorText(playResult)}");
                mciSendString(
                    $"close {alias}",
                    null,
                    0,
                    IntPtr.Zero);
                return;
            }

            Logger.Info(
                "SOUND",
                $"Sound started | File={Path.GetFileName(_soundFile)} | Alias={alias}");

            _ = Task.Run(
                async () =>
                {
                    try
                    {
                        await Task.Delay(
                            Math.Max(lengthMs + 100, 500));
                    }
                    finally
                    {
                        mciSendString(
                            $"close {alias}",
                            null,
                            0,
                            IntPtr.Zero);
                    }
                });
        }
        catch (Exception ex)
        {
            Logger.Error(
                "SOUND",
                "Exception while playing sound",
                ex);
        }
    }


    // --------------------------------------------------------
    // SHOW FLASH
    // --------------------------------------------------------
public void ShowFlash(bool force = false)
    {
        if (!_settings.FlashEnabled)
        {
            Logger.Debug("FLASH", "Flash skipped | FlashEnabled=false");
            return;
        }


        if (IsDisposed)
            return;


        if (InvokeRequired)
        {
            BeginInvoke(
                new Action(
                    () => ShowFlash(force)));

            return;
        }


        UpdateCs2Rect();


        if (!force &&
            (_cs2WindowHandle == IntPtr.Zero ||
             GetForegroundWindow() != _cs2WindowHandle))
        {
            Logger.Debug(
                "FLASH",
                "Flash skipped | CS2 is not the foreground window");
            return;
        }


        if (_cs2Rect.Width <= 0 ||
            _cs2Rect.Height <= 0)
        {
            return;
        }


        if (_imageFiles.Count == 0)
        {
            RefreshImages();
        }


        if (_settings.SoundEnabled)
        {
            if (string.IsNullOrWhiteSpace(_soundFile))
            {
                RefreshSounds();
            }


            PlaySound();
        }


        // Use the next queued image when available; otherwise show a white flash.


        if (!LoadNextImage())
        {
            _currentImage?.Dispose();

            _currentImage = null;

            Logger.Info(
                "FLASH",
                "No valid image available; using white flash fallback");
        }


        ApplyBounds();


        _flashActive =
            true;

        Logger.Info(
            "FLASH",
            $"Flash started | Force={force} | Duration={_settings.FlashDurationMs}ms | " +
            $"Hold={_settings.FlashHoldMs}ms | Opacity={_settings.FlashOpacity}% | " +
            $"Sound={_settings.SoundEnabled}");


        _flashAlpha =
            255.0;


        _flashStartTimestamp =
            Stopwatch.GetTimestamp();


        BuildFlashFrame();
        Render();


        _flashTimer.Stop();

        _flashTimer.Start();
    }


    // --------------------------------------------------------
    // LOAD NEXT IMAGE
    // --------------------------------------------------------
private bool LoadNextImage()
    {
        while (true)
        {
            if (_shuffledIndex >=
                _shuffledImages.Count)
            {
                CreateNewShuffle();
            }


            if (_shuffledImages.Count == 0)
            {
                _currentImage?.Dispose();

                _currentImage = null;

                return false;
            }


            string selectedFile =
                _shuffledImages[
                    _shuffledIndex];


            _shuffledIndex++;


            try
            {
                using FileStream stream =
                    new FileStream(
                        selectedFile,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.ReadWrite);


                using Image source =
                    Image.FromStream(
                        stream);


                Bitmap newImage =
                    new Bitmap(
                        source);


                _currentImage?.Dispose();


                _currentImage =
                    newImage;

                InvalidateAdaptiveBackground();


                _lastShownImage =
                    selectedFile;


                Logger.Info(
                    "IMAGE",
                    $"Image loaded | File={Path.GetFileName(selectedFile)}");


                return true;
            }
            catch (Exception ex)
            {
                Logger.Error(
                    "IMAGE",
                    $"Failed to load image | File={Path.GetFileName(selectedFile)}",
                    ex);


                _imageFiles.RemoveAll(
                    x =>
                        string.Equals(
                            x,
                            selectedFile,
                            StringComparison.OrdinalIgnoreCase));


                _shuffledImages.RemoveAll(
                    x =>
                        string.Equals(
                            x,
                            selectedFile,
                            StringComparison.OrdinalIgnoreCase));


                _shuffledIndex =
                    Math.Min(
                        _shuffledIndex,
                        _shuffledImages.Count);
            }
        }
    }


    // --------------------------------------------------------
    // FLASH TIMER
    // --------------------------------------------------------
private void OnFlashTimer()
    {
        if (!_flashActive)
        {
            _flashTimer.Stop();

            return;
        }


        double elapsedMs =
            Stopwatch.GetElapsedTime(
                _flashStartTimestamp)
            .TotalMilliseconds;


        int holdMs =
            Math.Max(
                0,
                _settings.FlashHoldMs);


        int durationMs =
            Math.Max(
                1,
                _settings.FlashDurationMs);


        if (elapsedMs <= holdMs)
        {
            _flashAlpha =
                255.0;


            Render();

            return;
        }


        double fadeElapsed =
            elapsedMs -
            holdMs;


        double fadeDuration =
            Math.Max(
                1,
                durationMs -
                holdMs);


        double t =
            Math.Clamp(
                fadeElapsed /
                fadeDuration,
                0.0,
                1.0);


        double smooth =
            t *
            t *
            (3.0 - 2.0 * t);


        _flashAlpha =
            255.0 *
            (1.0 - smooth);


        if (t >= 1.0)
        {
            _flashAlpha =
                0.0;


            _flashActive =
                false;


            _flashTimer.Stop();


            _currentImage?.Dispose();

            _currentImage = null;

            Logger.Info("FLASH", "Flash finished");
        }


        Render();
    }


    // --------------------------------------------------------
    // CS2 WINDOW AND CONNECTION
    // --------------------------------------------------------
private void UpdateCs2Rect()
    {
        try
        {
            Process[] processes =
                Process.GetProcessesByName(
                    "cs2");


            if (processes.Length == 0)
            {
                _cs2WindowHandle =
                    IntPtr.Zero;

                SetCs2ConnectionStatus(
                    false);

                return;
            }


            try
            {
                Process? cs2Process =
                    processes.FirstOrDefault(
                        p =>
                            p.MainWindowHandle !=
                            IntPtr.Zero);


                if (cs2Process == null)
                {
                    _cs2WindowHandle =
                        IntPtr.Zero;

                    SetCs2ConnectionStatus(
                        false);

                    return;
                }


                IntPtr handle =
                    cs2Process.MainWindowHandle;

                _cs2WindowHandle =
                    handle;


                if (handle == IntPtr.Zero)
                {
                    _cs2WindowHandle =
                        IntPtr.Zero;

                    SetCs2ConnectionStatus(
                        false);

                    return;
                }


                if (!GetWindowRect(
                        handle,
                        out RECT rect))
                {
                    _cs2WindowHandle =
                        IntPtr.Zero;

                    SetCs2ConnectionStatus(
                        false);

                    return;
                }


                int width =
                    rect.Right -
                    rect.Left;


                int height =
                    rect.Bottom -
                    rect.Top;


                if (width <= 0 ||
                    height <= 0)
                {
                    _cs2WindowHandle =
                        IntPtr.Zero;

                    SetCs2ConnectionStatus(
                        false);

                    return;
                }


                SetCs2ConnectionStatus(
                    true);


                Rectangle newRect =
                    new Rectangle(
                        rect.Left,
                        rect.Top,
                        width,
                        height);


                bool changed =
                    newRect != _cs2Rect;


                _cs2Rect =
                    newRect;


                if (changed)
                {
                    ApplyBounds();
                }
            }
            finally
            {
                foreach (Process process in processes)
                {
                    process.Dispose();
                }
            }
        }
        catch
        {
            _cs2WindowHandle =
                IntPtr.Zero;

            SetCs2ConnectionStatus(
                false);
        }
    }


    private void SetCs2ConnectionStatus(
        bool connected)
    {
        if (_cs2Connected != connected)
        {
            Logger.Info(
                "CS2",
                connected
                    ? "CS2 window detected / connected"
                    : "CS2 window lost / disconnected");
        }

        _cs2Connected =
            connected;
    }


    // --------------------------------------------------------
    // APPLY BOUNDS
    // --------------------------------------------------------
private void ApplyBounds()
    {
        if (_cs2Rect.Width <= 0 ||
            _cs2Rect.Height <= 0)
        {
            return;
        }


        if (!IsHandleCreated)
            return;


        SetWindowPos(
            Handle,

            new IntPtr(
                HWND_TOPMOST),

            _cs2Rect.Left,

            _cs2Rect.Top,

            _cs2Rect.Width,

            _cs2Rect.Height,

            SWP_NOACTIVATE |
            SWP_NOOWNERZORDER |
            SWP_SHOWWINDOW);


        EnsureBitmapSize(
            _cs2Rect.Width,
            _cs2Rect.Height);
    }


    // --------------------------------------------------------
    // BITMAP SIZE
    // --------------------------------------------------------
private void EnsureBitmapSize(
        int width,
        int height)
    {
        if (width <= 0 ||
            height <= 0)
        {
            return;
        }


        if (_bitmap != null &&
            _bitmap.Width == width &&
            _bitmap.Height == height)
        {
            return;
        }


        _bitmap?.Dispose();


        _bitmap =
            new Bitmap(
                width,
                height,
                PixelFormat.Format32bppPArgb);

        InvalidateAdaptiveBackground();
        InvalidateLayeredBitmap();
    }


    // --------------------------------------------------------
    // ADAPTIVE BACKGROUND
    // --------------------------------------------------------
private void InvalidateAdaptiveBackground()
    {
        _adaptiveBackground?.Dispose();
        _adaptiveBackground = null;
        _adaptiveBackgroundWidth = 0;
        _adaptiveBackgroundHeight = 0;
    }


    private void InvalidateLayeredBitmap()
    {
        if (_layeredMemDc != IntPtr.Zero &&
            _layeredOldBitmap != IntPtr.Zero)
        {
            SelectObject(
                _layeredMemDc,
                _layeredOldBitmap);
        }

        if (_layeredHBitmap != IntPtr.Zero)
        {
            DeleteObject(
                _layeredHBitmap);
        }

        if (_layeredMemDc != IntPtr.Zero)
        {
            DeleteDC(
                _layeredMemDc);
        }

        _layeredMemDc = IntPtr.Zero;
        _layeredHBitmap = IntPtr.Zero;
        _layeredOldBitmap = IntPtr.Zero;
        _layeredBitmapWidth = 0;
        _layeredBitmapHeight = 0;
    }


    private void EnsureLayeredBitmap()
    {
        if (_bitmap == null ||
            _bitmap.Width <= 0 ||
            _bitmap.Height <= 0)
        {
            return;
        }

        if (_layeredMemDc != IntPtr.Zero &&
            _layeredHBitmap != IntPtr.Zero &&
            _layeredBitmapWidth == _bitmap.Width &&
            _layeredBitmapHeight == _bitmap.Height)
        {
            return;
        }

        InvalidateLayeredBitmap();

        _layeredMemDc =
            CreateCompatibleDC(
                IntPtr.Zero);

        if (_layeredMemDc == IntPtr.Zero)
        {
            return;
        }

        _layeredHBitmap =
            _bitmap.GetHbitmap(
                Color.FromArgb(
                    0,
                    0,
                    0,
                    0));

        if (_layeredHBitmap == IntPtr.Zero)
        {
            InvalidateLayeredBitmap();
            return;
        }

        _layeredOldBitmap =
            SelectObject(
                _layeredMemDc,
                _layeredHBitmap);

        _layeredBitmapWidth =
            _bitmap.Width;

        _layeredBitmapHeight =
            _bitmap.Height;
    }


    private void EnsureAdaptiveBackground(
        int width,
        int height)
    {
        if (_currentImage == null ||
            width <= 0 ||
            height <= 0)
        {
            return;
        }

        if (_adaptiveBackground != null &&
            _adaptiveBackgroundWidth == width &&
            _adaptiveBackgroundHeight == height)
        {
            return;
        }

        InvalidateAdaptiveBackground();

        // Small texture + bicubic upscale gives a soft background
        // without an expensive per-pixel blur.
        const int textureWidth = 96;

        int textureHeight = Math.Clamp(
            (int)Math.Round(
                textureWidth *
                (height / (double)width)),
            54,
            120);

        Color[] edgeColors =
            GetEdgeColors(_currentImage);

        Color topLeft = edgeColors[0];
        Color topRight = edgeColors[1];
        Color bottomLeft = edgeColors[2];
        Color bottomRight = edgeColors[3];

        using Bitmap gradient =
            new Bitmap(
                textureWidth,
                textureHeight,
                PixelFormat.Format32bppArgb);

        for (int y = 0; y < gradient.Height; y++)
        {
            float v =
                gradient.Height <= 1
                    ? 0f
                    : y / (float)(gradient.Height - 1);

            Color left =
                BlendColor(
                    topLeft,
                    bottomLeft,
                    v);

            Color right =
                BlendColor(
                    topRight,
                    bottomRight,
                    v);

            for (int x = 0; x < gradient.Width; x++)
            {
                float u =
                    gradient.Width <= 1
                        ? 0f
                        : x / (float)(gradient.Width - 1);

                gradient.SetPixel(
                    x,
                    y,
                    BlendColor(
                        left,
                        right,
                        u));
            }
        }

        // Tiny source, then smooth upscale = cheap soft image halo.
        const int softWidth = 32;

        int softHeight = Math.Clamp(
            (int)Math.Round(
                softWidth *
                (_currentImage.Height /
                 (double)Math.Max(1, _currentImage.Width))),
            18,
            48);

        using Bitmap softSource =
            new Bitmap(
                softWidth,
                softHeight,
                PixelFormat.Format32bppArgb);

        using (Graphics softGraphics =
               Graphics.FromImage(softSource))
        {
            softGraphics.CompositingMode =
                CompositingMode.SourceCopy;
            softGraphics.InterpolationMode =
                InterpolationMode.HighQualityBicubic;
            softGraphics.SmoothingMode =
                SmoothingMode.HighQuality;
            softGraphics.PixelOffsetMode =
                PixelOffsetMode.HighQuality;

            softGraphics.DrawImage(
                _currentImage,
                new Rectangle(
                    0,
                    0,
                    softWidth,
                    softHeight),
                0,
                0,
                _currentImage.Width,
                _currentImage.Height,
                GraphicsUnit.Pixel);
        }

        using (Graphics bgGraphics =
               Graphics.FromImage(gradient))
        {
            using ImageAttributes sourceAttributes =
                new ImageAttributes();

            ColorMatrix matrix =
                new ColorMatrix();

            // Keep the background fully opaque.
            // We darken the soft image through RGB channels,
            // not through alpha, so its opacity is exactly the
            // same as the main image during the flash.
            matrix.Matrix00 =
                0.24f;

            matrix.Matrix11 =
                0.24f;

            matrix.Matrix22 =
                0.24f;

            matrix.Matrix33 =
                1.0f;

            sourceAttributes.SetColorMatrix(
                matrix,
                ColorMatrixFlag.Default,
                ColorAdjustType.Bitmap);

            bgGraphics.CompositingMode =
                CompositingMode.SourceOver;
            bgGraphics.InterpolationMode =
                InterpolationMode.HighQualityBicubic;
            bgGraphics.SmoothingMode =
                SmoothingMode.HighQuality;
            bgGraphics.PixelOffsetMode =
                PixelOffsetMode.HighQuality;

            bgGraphics.DrawImage(
                softSource,
                new Rectangle(
                    0,
                    0,
                    gradient.Width,
                    gradient.Height),
                0,
                0,
                softSource.Width,
                softSource.Height,
                GraphicsUnit.Pixel,
                sourceAttributes);

            using var dimBrush =
                new SolidBrush(
                    Color.FromArgb(
                        38,
                        0,
                        0,
                        0));

            bgGraphics.FillRectangle(
                dimBrush,
                0,
                0,
                gradient.Width,
                gradient.Height);
        }

        Bitmap finalBackground =
            new Bitmap(
                width,
                height,
                PixelFormat.Format32bppPArgb);

        using (Graphics finalGraphics =
               Graphics.FromImage(finalBackground))
        {
            finalGraphics.CompositingMode =
                CompositingMode.SourceCopy;
            finalGraphics.InterpolationMode =
                InterpolationMode.HighQualityBicubic;
            finalGraphics.SmoothingMode =
                SmoothingMode.HighQuality;
            finalGraphics.PixelOffsetMode =
                PixelOffsetMode.HighQuality;

            finalGraphics.DrawImage(
                gradient,
                new Rectangle(
                    0,
                    0,
                    width,
                    height),
                0,
                0,
                gradient.Width,
                gradient.Height,
                GraphicsUnit.Pixel);
        }

        _adaptiveBackground = finalBackground;
        _adaptiveBackgroundWidth = width;
        _adaptiveBackgroundHeight = height;
    }


    private static Color[] GetEdgeColors(
        Bitmap source)
    {
        const int sampleSize = 96;

        int sampleWidth = Math.Max(
            1,
            Math.Min(sampleSize, source.Width));

        int sampleHeight = Math.Max(
            1,
            Math.Min(sampleSize, source.Height));

        using Bitmap sample =
            new Bitmap(
                sampleWidth,
                sampleHeight,
                PixelFormat.Format24bppRgb);

        using (Graphics g = Graphics.FromImage(sample))
        {
            g.InterpolationMode =
                InterpolationMode.HighQualityBicubic;

            g.DrawImage(
                source,
                new Rectangle(
                    0,
                    0,
                    sample.Width,
                    sample.Height),
                0,
                0,
                source.Width,
                source.Height,
                GraphicsUnit.Pixel);
        }

        int edgeX = Math.Max(
            2,
            sample.Width / 12);

        int edgeY = Math.Max(
            2,
            sample.Height / 12);

        Color top = GetTrimmedEdgeColor(
            sample,
            new Rectangle(
                0,
                0,
                sample.Width,
                edgeY));

        Color right = GetTrimmedEdgeColor(
            sample,
            new Rectangle(
                sample.Width - edgeX,
                0,
                edgeX,
                sample.Height));

        Color bottom = GetTrimmedEdgeColor(
            sample,
            new Rectangle(
                0,
                sample.Height - edgeY,
                sample.Width,
                edgeY));

        Color left = GetTrimmedEdgeColor(
            sample,
            new Rectangle(
                0,
                0,
                edgeX,
                sample.Height));

        return new[]
        {
            TameBackgroundColor(top),
            TameBackgroundColor(right),
            TameBackgroundColor(bottom),
            TameBackgroundColor(left)
        };
    }


    private static Color GetTrimmedEdgeColor(
        Bitmap bitmap,
        Rectangle area)
    {
        List<int> red = new List<int>();
        List<int> green = new List<int>();
        List<int> blue = new List<int>();

        int stepX = Math.Max(1, area.Width / 16);
        int stepY = Math.Max(1, area.Height / 8);

        for (int y = area.Top; y < area.Bottom; y += stepY)
        {
            for (int x = area.Left; x < area.Right; x += stepX)
            {
                Color color = bitmap.GetPixel(x, y);

                red.Add(color.R);
                green.Add(color.G);
                blue.Add(color.B);
            }
        }

        if (red.Count == 0)
        {
            return Color.FromArgb(70, 70, 70);
        }

        red.Sort();
        green.Sort();
        blue.Sort();

        int trim =
            (int)(red.Count * 0.15f);

        int start = Math.Min(
            trim,
            red.Count - 1);

        int end = Math.Max(
            start + 1,
            red.Count - trim);

        long sumR = 0;
        long sumG = 0;
        long sumB = 0;

        for (int i = start; i < end; i++)
        {
            sumR += red[i];
            sumG += green[i];
            sumB += blue[i];
        }

        int count = end - start;

        return Color.FromArgb(
            (int)(sumR / count),
            (int)(sumG / count),
            (int)(sumB / count));
    }


    private static Color TameBackgroundColor(
        Color color)
    {
        const float factor = 0.62f;

        int r = (int)Math.Clamp(
            color.R * factor + 6,
            12,
            150);

        int g = (int)Math.Clamp(
            color.G * factor + 6,
            12,
            150);

        int b = (int)Math.Clamp(
            color.B * factor + 6,
            12,
            150);

        return Color.FromArgb(
            r,
            g,
            b);
    }


    private static Color BlendColor(
        Color a,
        Color b,
        float t)
    {
        t = Math.Clamp(t, 0f, 1f);

        return Color.FromArgb(
            (int)Math.Round(a.R + (b.R - a.R) * t),
            (int)Math.Round(a.G + (b.G - a.G) * t),
            (int)Math.Round(a.B + (b.B - a.B) * t));
    }


    // --------------------------------------------------------
    // SETTINGS CHANGED
    // --------------------------------------------------------
public void RenderSettingsChanged()
    {
        if (IsDisposed)
            return;

        if (InvokeRequired)
        {
            BeginInvoke(
                new Action(
                    RenderSettingsChanged));

            return;
        }

        if (_flashActive)
        {
            BuildFlashFrame();
        }

        Render();
    }


    // --------------------------------------------------------
    // RENDER
    // --------------------------------------------------------
private void BuildFlashFrame()
    {
        if (_bitmap == null ||
            _bitmap.Width <= 0 ||
            _bitmap.Height <= 0)
        {
            return;
        }

        using Graphics g =
            Graphics.FromImage(
                _bitmap);

        g.CompositingMode =
            CompositingMode.SourceCopy;

        g.Clear(
            Color.FromArgb(
                0,
                0,
                0,
                0));

        g.CompositingMode =
            CompositingMode.SourceOver;

        if (_settings.PreserveImageAspectRatio &&
            _currentImage != null)
        {
            EnsureAdaptiveBackground(
                _bitmap.Width,
                _bitmap.Height);

            if (_adaptiveBackground != null)
            {
                g.DrawImageUnscaled(
                    _adaptiveBackground,
                    0,
                    0);
            }
        }
        else
        {
            using var whiteBrush =
                new SolidBrush(
                    Color.White);

            if (_currentImage == null)
            {
                g.FillRectangle(
                    whiteBrush,
                    0,
                    0,
                    _bitmap.Width,
                    _bitmap.Height);
            }
        }

        if (_currentImage != null)
        {
            g.InterpolationMode =
                InterpolationMode.HighQualityBicubic;
            g.SmoothingMode =
                SmoothingMode.HighQuality;
            g.PixelOffsetMode =
                PixelOffsetMode.HighQuality;

            if (_settings.PreserveImageAspectRatio)
            {
                float scaleX =
                    (float)_bitmap.Width /
                    _currentImage.Width;

                float scaleY =
                    (float)_bitmap.Height /
                    _currentImage.Height;

                float scale =
                    Math.Min(
                        scaleX,
                        scaleY);

                int imageWidth =
                    Math.Max(
                        1,
                        (int)Math.Round(
                            _currentImage.Width * scale));

                int imageHeight =
                    Math.Max(
                        1,
                        (int)Math.Round(
                            _currentImage.Height * scale));

                int imageX =
                    (_bitmap.Width - imageWidth) / 2;

                int imageY =
                    (_bitmap.Height - imageHeight) / 2;

                g.DrawImage(
                    _currentImage,
                    new Rectangle(
                        imageX,
                        imageY,
                        imageWidth,
                        imageHeight),
                    0,
                    0,
                    _currentImage.Width,
                    _currentImage.Height,
                    GraphicsUnit.Pixel);
            }
            else
            {
                g.DrawImage(
                    _currentImage,
                    new Rectangle(
                        0,
                        0,
                        _bitmap.Width,
                        _bitmap.Height),
                    0,
                    0,
                    _currentImage.Width,
                    _currentImage.Height,
                    GraphicsUnit.Pixel);
            }
        }

        if (_settings.ShowDebug)
        {
            g.CompositingMode =
                CompositingMode.SourceOver;

            using var font =
                new Font(
                    "Arial",
                    20,
                    FontStyle.Bold);

            using var brush =
                new SolidBrush(
                    Color.Red);

            const string text =
                "Debug";

            SizeF textSize =
                g.MeasureString(
                    text,
                    font);

            float x =
                _bitmap.Width -
                textSize.Width -
                20;

            float y =
                _bitmap.Height -
                textSize.Height -
                15;

            g.DrawString(
                text,
                font,
                brush,
                x,
                y);
        }

        InvalidateLayeredBitmap();
        EnsureLayeredBitmap();
    }


    private void Render()
    {
        if (IsDisposed ||
            !IsHandleCreated)
        {
            return;
        }

        PushBitmap();
    }


    // --------------------------------------------------------
    // PUSH BITMAP
    // --------------------------------------------------------
private void PushBitmap()
    {
        if (_bitmap == null ||
            !IsHandleCreated ||
            _bitmap.Width <= 0 ||
            _bitmap.Height <= 0)
        {
            return;
        }

        EnsureLayeredBitmap();

        if (_layeredMemDc == IntPtr.Zero ||
            _layeredHBitmap == IntPtr.Zero)
        {
            return;
        }

        POINT destination =
            new POINT
            {
                X = _cs2Rect.Left,
                Y = _cs2Rect.Top
            };

        POINT source =
            new POINT
            {
                X = 0,
                Y = 0
            };

        SIZE size =
            new SIZE
            {
                CX = _bitmap.Width,
                CY = _bitmap.Height
            };

        double opacity =
            Math.Clamp(
                _settings.FlashOpacity,
                0,
                100) / 100.0;


        byte alpha =
            _flashActive
                ? (byte)Math.Clamp(
                    (int)Math.Round(
                        _flashAlpha * opacity),
                    0,
                    255)
                : (byte)0;

        BLENDFUNCTION blend =
            new BLENDFUNCTION
            {
                BlendOp = AC_SRC_OVER,
                BlendFlags = 0,
                SourceConstantAlpha = alpha,
                AlphaFormat = AC_SRC_ALPHA
            };

        UpdateLayeredWindow(
            Handle,
            IntPtr.Zero,
            ref destination,
            ref size,
            _layeredMemDc,
            ref source,
            0,
            ref blend,
            ULW_ALPHA);
    }


    // --------------------------------------------------------
    // CLOSE
    // --------------------------------------------------------
public void CloseOverlay()
    {
        if (IsDisposed)
            return;


        if (InvokeRequired)
        {
            BeginInvoke(
                new Action(
                    CloseOverlay));

            return;
        }


        _syncTimer.Stop();

        _flashTimer.Stop();


        _currentImage?.Dispose();

        _currentImage = null;

        _soundFile = null;

        InvalidateAdaptiveBackground();


        InvalidateLayeredBitmap();

        _bitmap?.Dispose();

        _bitmap = null;


        Hide();
    }
}
