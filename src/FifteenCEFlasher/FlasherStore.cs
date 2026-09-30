using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Threading;
using FifteenCEFlasherCore;
using Microsoft.Win32;

namespace FifteenCEFlasher;

public enum AppMode
{
    Demo,
    Flash,
    Batch,
    Probe,
}

public enum BatchPhase
{
    Setup,
    Waiting,
    BackingUp,
    Flashing,
    Done,
    Error,
}

public enum BatchBackupChoice
{
    Skip,
    AutoSave,
}

public sealed class FlasherStore : INotifyPropertyChanged, IDisposable
{
    private const string BatchFirmwarePathKey = "BatchFirmwarePath";
    private readonly DispatcherTimer _pollTimer;
    private CancellationTokenSource? _workCts;
    private SambaClient? _connectedClient;
    private SimulatedCalculatorTransport? _demoTransport;

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool ShowWelcome { get; private set; } = true;
    public AppMode SelectedMode { get; private set; } = AppMode.Flash;
    public WizardState Wizard { get; } = new();
    public ConnectionProbeResult? ProbeResult { get; private set; }
    public bool ShowAllPorts { get; set; }

    public string StatusMessage { get; private set; } = "Welcome";
    public string DetailMessage { get; private set; } = "";
    public double Progress { get; private set; }
    public string? FirmwarePath { get; private set; }
    public string? BackupPath { get; private set; }
    public string? PagePreviewHeader { get; private set; }
    public IEnumerable<string> PagePreviewLines { get; private set; } = [];
    /// <summary>Firmware read from the calculator on step 3, before the user decides whether to save it.</summary>
    public byte[]? CurrentFirmware { get; private set; }
    public bool BackupSkipped { get; private set; }
    public BackupChecksumAssessment? BackupAssessment { get; private set; }
    public FirmwareFileAssessment? FirmwareAssessment { get; private set; }

    /// <summary>
    /// Window title and heading while the app is open. FLASH and DEMO follow the calculator found
    /// on step 3; BATCH follows the firmware every unit receives. Welcome and Connection Probe stay
    /// 15CE Flasher, and so does the app's own name.
    /// </summary>
    public string AppTitle
    {
        get
        {
            string? model = null;
            if (!ShowWelcome && SelectedMode != AppMode.Probe)
                model = SelectedMode == AppMode.Batch
                    ? FirmwareAssessment?.Known?.Model
                    : BackupAssessment?.Known?.Model;
            return model switch
            {
                "HP 16c Collector’s Edition" => "16CE Flasher",
                "HP 12c" => "12c Flasher",
                _ => "15CE Flasher",
            };
        }
    }

    /// <summary>The calculator named by the flashed file, or else by the firmware found on step 3.</summary>
    public string? FlashedModelName =>
        FirmwareAssessment?.Known?.Model ?? BackupAssessment?.Known?.Model;

    /// <summary>True while a file dialog is open. The connection poll pauses so it cannot block the dialog.</summary>
    private bool _dialogOpen;

    public BatchPhase BatchPhase { get; private set; } = BatchPhase.Setup;
    public int BatchUnitNumber { get; private set; }
    public BatchBackupChoice BatchBackupChoice { get; set; } = BatchBackupChoice.Skip;
    public string? BatchBackupFolder { get; private set; }
    public string? BatchSessionStamp { get; private set; }

    public bool IsBatchActive => !ShowWelcome && SelectedMode == AppMode.Batch;
    public bool CanStartBatch => BatchStartBlockedReason is null;

    public string? BatchStartBlockedReason
    {
        get
        {
            if (!Wizard.FirmwareOk)
                return "Choose firmware first.";
            if (BatchBackupChoice == BatchBackupChoice.AutoSave && BatchBackupFolder is null)
                return "Choose a backup folder.";
            return null;
        }
    }

    public Flasher Flasher { get; private set; }

    public FlasherStore()
    {
        Flasher = HardwareFlasher();
        _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _pollTimer.Tick += (_, _) => RefreshConnection();
    }

    private static Flasher HardwareFlasher() => new()
    {
        FlashCommandSettleSeconds = TimeSpan.FromMilliseconds(10),
        Ports = new WindowsComPortListing(),
        OpenTransport = port => new WindowsSerialLink(port.PortName),
    };

    public void Start()
    {
        _pollTimer.Start();
        RefreshConnection();
    }

    public void Dispose()
    {
        _pollTimer.Stop();
        _workCts?.Cancel();
        Disconnect();
    }

    public void SelectMode(AppMode mode)
    {
        SelectedMode = mode;
        ShowWelcome = false;
        Wizard.Step = WizardStep.Cable;
        ResetWizardFlags();

        if (mode == AppMode.Demo)
        {
            var transport = new SimulatedCalculatorTransport(
                operationDelay: TimeSpan.FromMilliseconds(80),
                preloadApplication: true);
            _demoTransport = transport;
            Flasher = new Flasher
            {
                FlashCommandSettleSeconds = TimeSpan.FromMilliseconds(10),
                Ports = new SimulatedPortListing(),
                OpenTransport = _ => transport,
            };
            Wizard.IdentitySupported = true;
            StatusMessage = "DEMO mode — simulated calculator";
            DetailMessage = "No hardware required.";
        }
        else
        {
            _demoTransport = null;
            Flasher = HardwareFlasher();
            if (mode == AppMode.Batch)
                BeginBatchSession();
        }

        NotifyAll();
    }

    public void ReturnToWelcome()
    {
        _workCts?.Cancel();
        Disconnect();
        ShowWelcome = true;
        BatchPhase = BatchPhase.Setup;
        BatchUnitNumber = 0;
        FirmwarePath = null;
        BackupPath = null;
        ResetWizardFlags();
        StatusMessage = "Welcome";
        DetailMessage = "";
        NotifyAll();
    }

    public void WizardAdvance()
    {
        Wizard.Advance();
        Notify(nameof(Wizard));
        if (Wizard.Step == WizardStep.Backup && CurrentFirmware is null && !Wizard.BackupResolved)
            _ = CheckCurrentFirmwareAsync();
    }

    public void WizardBack()
    {
        if (Wizard.Step == WizardStep.ProgrammingMode)
            ClearCurrentFirmware();
        Wizard.GoBack();
        NotifyAll();
    }

    private bool? RunDialog(CommonDialog dialog)
    {
        _dialogOpen = true;
        try
        {
            return dialog.ShowDialog();
        }
        finally
        {
            _dialogOpen = false;
        }
    }

    public void PickFirmware()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Firmware (*.bin)|*.bin|All files|*.*",
            Title = "Choose firmware file",
        };
        if (RunDialog(dialog) != true)
            return;

        try
        {
            FirmwareImage.Validate(File.ReadAllBytes(dialog.FileName));
            FirmwarePath = dialog.FileName;
            Wizard.FirmwareOk = true;
            // The step 3 check knows the calculator's firmware even when the backup was skipped.
            FirmwareAssessment = VoyagerFirmwareChecksum.FirmwareFileAssessment(
                File.ReadAllBytes(dialog.FileName),
                BackupAssessment,
                backupSkipped: false);

            if (SelectedMode == AppMode.Batch)
                SaveBatchFirmwarePath(dialog.FileName);

            NotifyAll();
        }
        catch (FlasherError ex)
        {
            MessageBox.Show(ex.Message, "Invalid firmware", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>Saves the firmware already read on step 3. Nothing is read from the calculator again.</summary>
    public void SaveBackup()
    {
        if (CurrentFirmware is null || BackupAssessment is null)
            return;

        var dialog = new SaveFileDialog
        {
            Filter = "Firmware backup (*.bin)|*.bin",
            FileName = BackupAssessment.DefaultBackupFileName(DateTime.Now),
            Title = "Save backup",
        };
        if (RunDialog(dialog) != true)
            return;

        try
        {
            File.WriteAllBytes(dialog.FileName, CurrentFirmware);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Backup failed", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        BackupPath = dialog.FileName;
        BackupSkipped = false;
        Wizard.BackupResolved = true;
        StatusMessage = "Backup saved";
        NotifyAll();
    }

    public void SkipBackup()
    {
        BackupPath = null;
        BackupSkipped = true;
        Wizard.BackupResolved = true;
        NotifyAll();
    }

    public void SetBatchBackupChoice(BatchBackupChoice choice)
    {
        if (BatchBackupChoice == choice)
            return;
        BatchBackupChoice = choice;
        Notify(nameof(BatchBackupChoice));
        Notify(nameof(CanStartBatch));
        Notify(nameof(BatchStartBlockedReason));
        Notify(nameof(BatchBackupFolder));
    }

    public void PickBatchBackupFolder()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Choose backup folder",
            Multiselect = false,
        };
        if (!string.IsNullOrEmpty(BatchBackupFolder))
            dialog.InitialDirectory = BatchBackupFolder;
        if (RunDialog(dialog) != true)
            return;

        BatchBackupFolder = dialog.FolderName;
        Notify(nameof(BatchBackupFolder));
        Notify(nameof(CanStartBatch));
        Notify(nameof(BatchStartBlockedReason));
    }

    public void BeginBatchSession()
    {
        BatchPhase = BatchPhase.Setup;
        BatchUnitNumber = 0;
        BatchSessionStamp = null;
        BatchBackupChoice = BatchBackupChoice.Skip;
        BatchBackupFolder = null;
        RestoreBatchFirmwarePath();
        NotifyAll();
    }

    public void StartBatchRun()
    {
        if (!CanStartBatch)
            return;

        BatchSessionStamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        BatchUnitNumber = 1;
        BatchPhase = BatchPhase.Waiting;
        StatusMessage = "Waiting…";
        DetailMessage = "Hold ERASE, press RESET, then release ERASE.";
        Disconnect();
        NotifyAll();
    }

    public void PrepareNextBatchUnit()
    {
        BatchUnitNumber++;
        BatchPhase = BatchPhase.Waiting;
        StatusMessage = "Waiting…";
        DetailMessage = "Hold ERASE, press RESET, then release ERASE.";
        PagePreviewHeader = null;
        PagePreviewLines = [];
        Disconnect();
        NotifyAll();
    }

    public void RetryBatchUnit()
    {
        BatchPhase = BatchPhase.Waiting;
        StatusMessage = "Waiting…";
        DetailMessage = "Hold ERASE, press RESET, then release ERASE.";
        Disconnect();
        NotifyAll();
    }

    public void StopBatch()
    {
        _workCts?.Cancel();
        Application.Current.Shutdown();
    }

    /// <summary>Step 3: read the firmware on the calculator and name it from the known list.</summary>
    public async Task CheckCurrentFirmwareAsync()
    {
        if (Wizard.IsBusy)
            return;

        ClearCurrentFirmware();
        Wizard.IsBusy = true;
        StatusMessage = "Checking current firmware…";
        Progress = 0;
        NotifyAll();
        try
        {
            var data = await Task.Run(() =>
            {
                var client = EnsureConnected();
                return Flasher.ReadApplication(client, (f, _) => ReportProgress(f, "Checking current firmware…"));
            });

            CurrentFirmware = data;
            BackupAssessment = VoyagerFirmwareChecksum.BackupAssessment(data);
            RefreshSelectedFirmwareAssessment();
            StatusMessage = "Firmware checked";
        }
        catch (Exception ex)
        {
            Disconnect();
            MessageBox.Show(ex.Message, "Firmware check failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            Wizard.IsBusy = false;
            Progress = 0;
            NotifyAll();
        }
    }

    private void ClearCurrentFirmware()
    {
        CurrentFirmware = null;
        BackupAssessment = null;
        BackupPath = null;
        BackupSkipped = false;
        Wizard.BackupResolved = false;
        RefreshSelectedFirmwareAssessment();
    }

    private void RefreshSelectedFirmwareAssessment()
    {
        if (FirmwarePath is null || !File.Exists(FirmwarePath))
            return;
        FirmwareAssessment = VoyagerFirmwareChecksum.FirmwareFileAssessment(
            File.ReadAllBytes(FirmwarePath),
            BackupAssessment,
            backupSkipped: false);
    }

    public async Task RunFlashAsync()
    {
        if (Wizard.IsBusy || FirmwarePath is null)
            return;

        Wizard.IsBusy = true;
        StatusMessage = "Writing firmware…";
        Progress = 0;
        PagePreviewHeader = null;
        PagePreviewLines = [];
        NotifyAll();
        var flashRunSucceeded = false;
        try
        {
            FlashPageWrite? latestPage = null;
            await Task.Run(() =>
            {
                var client = EnsureConnected();
                Flasher.Write(
                    FirmwarePath,
                    client: client,
                    progress: (f, phase) =>
                    {
                        var status = phase switch
                        {
                            FlashProgressPhase.Writing => "Writing firmware…",
                            FlashProgressPhase.Verifying => "Verifying…",
                            _ => StatusMessage,
                        };
                        if (phase == FlashProgressPhase.Verifying)
                            ReportProgress(f, status);
                        else
                            ReportProgress(f, status, latestPage);
                    },
                    pageProgress: page => latestPage = page);
            });

            flashRunSucceeded = true;
            Wizard.FlashSucceeded = true;
            if (latestPage is not null)
            {
                PagePreviewHeader = latestPage.Header;
                PagePreviewLines = latestPage.FormattedWordLines().ToList();
            }
            StatusMessage = "Flashed and verified.";
            DetailMessage = "Press RESET on the calculator to restart.";
            Progress = 1;
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Flash failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            Wizard.IsBusy = false;
            if (!flashRunSucceeded)
                Progress = 0;
            NotifyAll();
        }
    }

    private void RefreshConnection()
    {
        if (ShowWelcome || _dialogOpen)
            return;

        if ((SelectedMode is AppMode.Flash or AppMode.Demo) && Wizard.Step == WizardStep.Cable)
            return;

        if (SelectedMode == AppMode.Probe)
        {
            var probe = new ConnectionProbeService
            {
                PortListing = Flasher.Ports,
                OpenTransport = Flasher.OpenTransport,
            }.Poll();
            ProbeResult = probe;
            StatusMessage = probe.StatusText;
            DetailMessage = probe.DetailText ?? "";
            NotifyAll();
            return;
        }

        if (SelectedMode == AppMode.Demo)
        {
            Wizard.IdentitySupported = true;
            StatusMessage = "Connected: ATSAM4LC2C (DEMO)";
            DetailMessage = "Simulated calculator";
            NotifyAll();
            return;
        }

        if (Wizard.IsBusy || BatchPhase is BatchPhase.BackingUp or BatchPhase.Flashing or BatchPhase.Done)
            return;

        if (Wizard.FlashSucceeded && Wizard.Step == WizardStep.Flash)
            return;

        try
        {
            Disconnect();
            var connected = Flasher.Connect(TimeSpan.FromSeconds(2));
            _connectedClient = connected.Client;
            Wizard.IdentitySupported = connected.Identity.IsSupported15C;
            StatusMessage = connected.Identity.IsSupported15C
                ? $"Connected: {connected.Identity.Name}"
                : "Unsupported chip";
            DetailMessage = $"{connected.Port.PortName} · {connected.Client.Version.Trim()}";

            if (IsBatchActive && BatchPhase == BatchPhase.Waiting)
                _ = RunBatchUnitAsync();
        }
        catch
        {
            Disconnect();
            Wizard.IdentitySupported = false;
            StatusMessage = "Waiting…";
            DetailMessage = "Hold ERASE, press RESET, then release ERASE.";
        }

        NotifyAll();
    }

    private async Task RunBatchUnitAsync()
    {
        if (Wizard.IsBusy || BatchPhase != BatchPhase.Waiting || FirmwarePath is null)
            return;

        _workCts?.Cancel();
        _workCts = new CancellationTokenSource();
        var token = _workCts.Token;
        Wizard.IsBusy = true;
        Progress = 0;
        PagePreviewHeader = null;
        PagePreviewLines = [];
        NotifyAll();

        try
        {
            if (BatchBackupChoice == BatchBackupChoice.AutoSave && BatchBackupFolder is not null && BatchSessionStamp is not null)
            {
                BatchPhase = BatchPhase.BackingUp;
                StatusMessage = "Saving backup…";
                NotifyAll();
                var backupPath = BatchBackupNaming.FilePath(BatchBackupFolder, BatchSessionStamp, BatchUnitNumber);
                await Task.Run(() =>
                {
                    token.ThrowIfCancellationRequested();
                    var client = EnsureConnected();
                    Flasher.Read(backupPath, client, (f, _) => ReportProgress(f, "Saving backup…"));
                }, token);
            }

            BatchPhase = BatchPhase.Flashing;
            StatusMessage = "Writing firmware…";
            NotifyAll();
            FlashPageWrite? latestPage = null;
            await Task.Run(() =>
            {
                token.ThrowIfCancellationRequested();
                var client = EnsureConnected();
                Flasher.Write(
                    FirmwarePath,
                    client: client,
                    progress: (f, phase) =>
                    {
                        var status = phase switch
                        {
                            FlashProgressPhase.Writing => "Writing firmware…",
                            FlashProgressPhase.Verifying => "Verifying…",
                            _ => StatusMessage,
                        };
                        if (phase == FlashProgressPhase.Verifying)
                            ReportProgress(f, status, clearPagePreview: true);
                        else
                            ReportProgress(f, status, latestPage);
                    },
                    pageProgress: page => latestPage = page);
            }, token);

            BatchPhase = BatchPhase.Done;
            PagePreviewHeader = null;
            PagePreviewLines = [];
            StatusMessage = $"Unit {BatchUnitNumber} complete";
            DetailMessage = "Press RESET, then turn the calculator ON.";
            Disconnect();
        }
        catch (OperationCanceledException)
        {
            // Ignored.
        }
        catch (Exception ex)
        {
            BatchPhase = BatchPhase.Error;
            StatusMessage = "Batch error";
            DetailMessage = ex.Message;
            Disconnect();
        }
        finally
        {
            Wizard.IsBusy = false;
            Progress = 0;
            NotifyAll();
        }
    }

    private SambaClient EnsureConnected()
    {
        if (_connectedClient is not null)
            return _connectedClient;

        if (SelectedMode == AppMode.Demo && _demoTransport is not null)
        {
            var client = new SambaClient(_demoTransport);
            client.Connect();
            _connectedClient = client;
            return client;
        }

        var connected = Flasher.Connect();
        _connectedClient = connected.Client;
        return connected.Client;
    }

    private void Disconnect()
    {
        _connectedClient?.Close();
        _connectedClient = null;
    }

    private void ResetWizardFlags()
    {
        Wizard.IdentitySupported = false;
        Wizard.BackupResolved = false;
        Wizard.FirmwareOk = false;
        Wizard.FlashSucceeded = false;
        Wizard.IsBusy = false;
        CurrentFirmware = null;
        BackupSkipped = false;
        BackupAssessment = null;
        FirmwareAssessment = null;
        PagePreviewHeader = null;
        PagePreviewLines = [];
    }

    private static void SaveBatchFirmwarePath(string path) =>
        Registry.CurrentUser.CreateSubKey(@"Software\MachII\15CEFlasher")?.SetValue(BatchFirmwarePathKey, path);

    private void RestoreBatchFirmwarePath()
    {
        var path = Registry.CurrentUser.OpenSubKey(@"Software\MachII\15CEFlasher")?.GetValue(BatchFirmwarePathKey) as string;
        if (path is null || !File.Exists(path))
            return;

        try
        {
            FirmwareImage.Validate(File.ReadAllBytes(path));
            FirmwarePath = path;
            Wizard.FirmwareOk = true;
            FirmwareAssessment = VoyagerFirmwareChecksum.FirmwareFileAssessment(
                File.ReadAllBytes(path),
                BackupAssessment,
                backupSkipped: true);
        }
        catch
        {
            Registry.CurrentUser.CreateSubKey(@"Software\MachII\15CEFlasher")?.DeleteValue(BatchFirmwarePathKey, false);
        }
    }

    private void ReportProgress(double fraction, string status, FlashPageWrite? page = null, bool clearPagePreview = false)
    {
        RunOnUi(() =>
        {
            Progress = fraction;
            StatusMessage = status;
            if (clearPagePreview)
            {
                PagePreviewHeader = null;
                PagePreviewLines = [];
            }
            else if (page is not null)
            {
                PagePreviewHeader = page.Header;
                PagePreviewLines = page.FormattedWordLines().ToList();
            }

            Notify(nameof(Progress));
        });
    }

    private static void RunOnUi(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
            return;
        }

        dispatcher.Invoke(action);
    }

    private void NotifyAll()
    {
        Notify(nameof(ShowWelcome));
        Notify(nameof(SelectedMode));
        Notify(nameof(Wizard));
        Notify(nameof(ProbeResult));
        Notify(nameof(StatusMessage));
        Notify(nameof(DetailMessage));
        Notify(nameof(Progress));
        Notify(nameof(FirmwarePath));
        Notify(nameof(BackupPath));
        Notify(nameof(PagePreviewHeader));
        Notify(nameof(PagePreviewLines));
        Notify(nameof(CurrentFirmware));
        Notify(nameof(BackupSkipped));
        Notify(nameof(BackupAssessment));
        Notify(nameof(FirmwareAssessment));
        Notify(nameof(AppTitle));
        Notify(nameof(BatchPhase));
        Notify(nameof(BatchUnitNumber));
        Notify(nameof(BatchBackupFolder));
        Notify(nameof(IsBatchActive));
        Notify(nameof(CanStartBatch));
    }

    private void Notify([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
