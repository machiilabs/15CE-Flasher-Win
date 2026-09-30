using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using FifteenCEFlasherCore;
using SharpVectors.Converters;
using SharpVectors.Renderers.Wpf;

namespace FifteenCEFlasher;

public partial class MainWindow : Window
{
    private static readonly SolidColorBrush SidebarGreen = new(Color.FromRgb(51, 199, 102));

    private readonly FlasherStore _store = new();

    public MainWindow()
    {
        InitializeComponent();
        SubtitleText.Text = AppVersionLabel();
        _store.PropertyChanged += (_, _) =>
        {
            if (Dispatcher.CheckAccess())
                UpdateUi();
            else
                Dispatcher.Invoke(UpdateUi);
        };
        _store.Start();
        UpdateUi();
    }

    protected override void OnClosed(EventArgs e)
    {
        _store.Dispose();
        base.OnClosed(e);
    }

    private static string AppVersionLabel()
    {
        var path = Environment.ProcessPath;
        if (path is null)
            return "Mach II Labs · Windows";

        var info = FileVersionInfo.GetVersionInfo(path);
        return $"Mach II Labs · Windows · {info.FileMajorPart}.{info.FileMinorPart}.{info.FileBuildPart} ({info.FilePrivatePart})";
    }

    private void UpdateUi()
    {
        // Follows the detected model while the app is open; the app's own name stays 15CE Flasher.
        Title = _store.AppTitle;
        HeadingText.Text = _store.AppTitle;
        StatusText.Text = _store.StatusMessage;
        DetailText.Text = _store.DetailMessage;
        ProgressBar.Value = _store.Progress;
        ProgressBar.IsIndeterminate = _store.Wizard.IsBusy && _store.Progress <= 0;
        ProgressBar.Visibility = _store.Wizard.IsBusy || _store.Progress > 0
            ? Visibility.Visible
            : Visibility.Collapsed;
        StatusBar.Visibility = ShowStatusBar() ? Visibility.Visible : Visibility.Collapsed;

        if (_store.ShowWelcome)
            MainContent.Content = Scroll(BuildWelcome());
        else if (_store.SelectedMode is AppMode.Probe || _store.IsBatchActive)
            MainContent.Content = Scroll(BuildActiveView());
        else
            MainContent.Content = BuildWizardView();
    }

    private UIElement BuildWelcome()
    {
        var panel = new StackPanel { MaxWidth = 520 };

        panel.Children.Add(new TextBlock
        {
            Text = "Native SAM-BA programmer for the post-2015 Voyager series.",
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("InkBrush"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 16),
        });

        panel.Children.Add(MakeModeButton("FLASH", "Guided 7-step wizard", AppMode.Flash));
        panel.Children.Add(MakeModeButton("BATCH", "Flash many calculators in a row", AppMode.Batch));
        panel.Children.Add(MakeModeButton("DEMO", "Try the wizard without hardware", AppMode.Demo));
        panel.Children.Add(MakeModeButton("Connection Probe", "Test cable detection only", AppMode.Probe));

        return panel;
    }

    private Button MakeModeButton(string title, string subtitle, AppMode mode)
    {
        var btn = new Button
        {
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(16, 12, 16, 12),
            Margin = new Thickness(0, 0, 0, 8),
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(229, 231, 235)),
            BorderThickness = new Thickness(1),
        };

        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.Bold, FontSize = 16 });
        stack.Children.Add(new TextBlock { Text = subtitle, Foreground = (Brush)FindResource("MutedBrush"), Margin = new Thickness(0, 4, 0, 0) });
        btn.Content = stack;
        btn.Click += (_, _) => _store.SelectMode(mode);
        return btn;
    }

    private UIElement BuildActiveView()
    {
        if (_store.SelectedMode == AppMode.Probe)
            return BuildProbeView();

        if (_store.IsBatchActive)
            return BuildBatchView();

        return BuildWizardView();
    }

    private UIElement BuildProbeView()
    {
        var panel = new StackPanel();

        panel.Children.Add(new TextBlock
        {
            Text = "Connection Probe",
            FontSize = 20,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 12),
        });

        panel.Children.Add(new TextBlock
        {
            Text = "Hold ERASE, press RESET, then release ERASE.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 16),
        });

        panel.Children.Add(WizardDiagram("wizard-programming-mode.png"));

        var atmelPort = _store.ProbeResult?.Port
            ?? (_store.ProbeResult?.AllPorts ?? []).FirstOrDefault(p => p.Kind == UsbPortKind.AtmelSamBa);
        if (atmelPort is not null)
        {
            panel.Children.Add(new TextBlock { Text = "Atmel port:", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 4) });
            panel.Children.Add(new TextBlock
            {
                Text = $"{atmelPort.PortName} · Atmel SAM-BA (03EB:6124)",
                Foreground = (Brush)FindResource("MutedBrush"),
            });
        }

        panel.Children.Add(MakeFooterButtons(showBack: true, doneLabel: "Quit"));
        return panel;
    }

    private UIElement BuildBatchView()
    {
        var panel = new StackPanel();

        panel.Children.Add(new TextBlock
        {
            Text = "BATCH mode",
            FontSize = 20,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 8),
        });

        if (_store.BatchPhase == BatchPhase.Setup)
        {
            panel.Children.Add(new TextBlock
            {
                Text = "Pick firmware once, then flash each calculator as it connects.",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 12),
            });

            panel.Children.Add(MakeActionButton("Choose firmware…", _store.PickFirmware));
            if (_store.FirmwarePath is not null)
            {
                panel.Children.Add(new TextBlock
                {
                    Text = _store.FirmwarePath,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 8, 0, 4),
                    Foreground = (Brush)FindResource("MutedBrush"),
                });
                if (_store.FirmwareAssessment is not null)
                    panel.Children.Add(new TextBlock
                    {
                        Text = $"Expected checksum:  {VoyagerFirmwareChecksum.Formatted(_store.FirmwareAssessment.Displayed)}",
                        Margin = new Thickness(0, 0, 0, 8),
                        Foreground = (Brush)FindResource("MutedBrush"),
                    });
            }

            var backupPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
            var skip = new RadioButton { Content = "Skip backups", IsChecked = _store.BatchBackupChoice == BatchBackupChoice.Skip, Margin = new Thickness(0, 0, 16, 0) };
            var auto = new RadioButton { Content = "Auto-save backups", IsChecked = _store.BatchBackupChoice == BatchBackupChoice.AutoSave };
            skip.Checked += (_, _) => _store.SetBatchBackupChoice(BatchBackupChoice.Skip);
            auto.Checked += (_, _) => _store.SetBatchBackupChoice(BatchBackupChoice.AutoSave);
            backupPanel.Children.Add(skip);
            backupPanel.Children.Add(auto);
            panel.Children.Add(backupPanel);

            if (_store.BatchBackupChoice == BatchBackupChoice.AutoSave)
            {
                panel.Children.Add(MakeActionButton("Choose backup folder…", _store.PickBatchBackupFolder));
                if (_store.BatchBackupFolder is not null)
                    panel.Children.Add(new TextBlock
                    {
                        Text = _store.BatchBackupFolder,
                        TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(0, 4, 0, 0),
                        Foreground = (Brush)FindResource("MutedBrush"),
                    });
            }

            var start = MakeActionButton("Start batch", () =>
            {
                if (_store.BatchStartBlockedReason is { } reason)
                {
                    MessageBox.Show(reason, "BATCH", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                _store.StartBatchRun();
            }, primary: true, enabled: _store.CanStartBatch);
            start.Margin = new Thickness(0, 16, 8, 8);
            panel.Children.Add(start);
        }
        else
        {
            panel.Children.Add(new TextBlock
            {
                Text = BatchRunTitle(),
                FontSize = 16,
                Margin = new Thickness(0, 0, 0, 8),
            });

            switch (_store.BatchPhase)
            {
                case BatchPhase.Waiting:
                    panel.Children.Add(BodyText("Hold ERASE, press RESET, then release ERASE. The app connects and flashes automatically."));
                    AddBatchExpectedChecksum(panel);
                    break;
                case BatchPhase.BackingUp:
                    panel.Children.Add(BodyText("Saving backup…"));
                    break;
                case BatchPhase.Flashing:
                    panel.Children.Add(BodyText("Writing firmware…"));
                    if (_store.PagePreviewHeader is not null)
                    {
                        panel.Children.Add(new TextBlock
                        {
                            Text = _store.PagePreviewHeader,
                            FontWeight = FontWeights.SemiBold,
                            Foreground = (Brush)FindResource("InkBrush"),
                            Margin = new Thickness(0, 12, 0, 4),
                        });
                        foreach (var line in _store.PagePreviewLines.Take(8))
                            panel.Children.Add(new TextBlock
                            {
                                Text = line,
                                FontFamily = new FontFamily("Consolas"),
                                FontSize = 11,
                                Foreground = (Brush)FindResource("InkBrush"),
                            });
                    }
                    break;
                case BatchPhase.Done:
                    panel.Children.Add(new TextBlock
                    {
                        Text = "Flashed and verified.",
                        FontWeight = FontWeights.SemiBold,
                        Foreground = Brushes.DarkGreen,
                        Margin = new Thickness(0, 0, 0, 8),
                    });
                    AddBatchExpectedChecksum(panel);
                    panel.Children.Add(BodyText("Press RESET on the cable, then turn the calculator ON. “Pr Error” is expected."));
                    panel.Children.Add(MakeActionButton("Next unit", _store.PrepareNextBatchUnit, primary: true));
                    break;
                case BatchPhase.Error:
                    panel.Children.Add(new TextBlock
                    {
                        Text = _store.DetailMessage,
                        TextWrapping = TextWrapping.Wrap,
                        Foreground = Brushes.DarkRed,
                        Margin = new Thickness(0, 0, 0, 8),
                    });
                    panel.Children.Add(MakeActionButton("Retry", _store.RetryBatchUnit, primary: true));
                    break;
            }
        }

        panel.Children.Add(MakeFooterButtons(showBack: true, doneLabel: "Stop batch", onDone: _store.StopBatch));
        return panel;
    }

    private string BatchRunTitle() => _store.BatchPhase switch
    {
        BatchPhase.Waiting => $"Unit {_store.BatchUnitNumber}",
        BatchPhase.BackingUp => $"Unit {_store.BatchUnitNumber} · Saving backup",
        BatchPhase.Flashing => $"Unit {_store.BatchUnitNumber} · Writing firmware",
        BatchPhase.Done => $"Unit {_store.BatchUnitNumber} complete",
        BatchPhase.Error => $"Unit {_store.BatchUnitNumber} · Error",
        _ => "BATCH mode",
    };

    private void AddBatchExpectedChecksum(StackPanel panel)
    {
        if (_store.FirmwareAssessment is null)
            return;
        var checksum = VoyagerFirmwareChecksum.Formatted(_store.FirmwareAssessment.Displayed);
        panel.Children.Add(new TextBlock
        {
            Text = $"Expected checksum on calculator: {checksum} (test menu 2.C).",
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)FindResource("MutedBrush"),
            Margin = new Thickness(0, 0, 0, 8),
        });
    }

    private UIElement BuildWizardView()
    {
        var outer = new Grid();
        outer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        outer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        outer.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        outer.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var sidebar = BuildWizardStepSidebar();
        Grid.SetColumn(sidebar, 0);
        Grid.SetRow(sidebar, 0);
        Grid.SetRowSpan(sidebar, 2);
        outer.Children.Add(sidebar);

        var contentGrid = new Grid();
        contentGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        contentGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetColumn(contentGrid, 1);
        Grid.SetRow(contentGrid, 0);
        Grid.SetRowSpan(contentGrid, 2);
        outer.Children.Add(contentGrid);

        var panel = new StackPanel();
        var step = _store.Wizard.Step;

        switch (step)
        {
            case WizardStep.Cable:
                panel.Children.Add(WizardSvgDiagram("step1-cable-diagram-pc.svg", maxHeight: 220));
                break;
            case WizardStep.ProgrammingMode:
                panel.Children.Add(WizardDiagram("wizard-programming-mode.png"));
                break;
            case WizardStep.Finish:
                panel.Children.Add(WizardDiagram("wizard-finish.png"));
                break;
            case WizardStep.Checksum:
                panel.Children.Add(WizardDiagram("wizard-checksum.png", maxHeight: 200));
                break;
        }

        if (step == WizardStep.Firmware && _store.BackupAssessment is { } current)
            panel.Children.Add(FirmwareMessageBox(
                [("Your current firmware:", current.Summary)],
                caution: !current.IsRecognized,
                tone: MessageTone.CarriedOver));

        if (step == WizardStep.Flash)
            AddFlashFirmwareSummary(panel);

        foreach (var line in StepBodyLines(step))
            panel.Children.Add(BodyText(line));

        if (step == WizardStep.Cable)
            panel.Children.Add(WarningBox("Use the POGO cable only on post-2015 Voyager calculators (15c CE, 16c CE, 12c). Do not use the cable on an HP 15c Limited Edition, a pre-2015 12c, an HP 20b, or an HP 30b, as it could permanently damage your calculator."));

        if (_store.SelectedMode == AppMode.Demo && step == WizardStep.Cable)
            panel.Children.Add(MutedText("DEMO uses a simulated calculator. You do not need a cable. Follow the same steps so FLASH is familiar later."));

        if (_store.SelectedMode == AppMode.Demo && step == WizardStep.ProgrammingMode)
            panel.Children.Add(MutedText("DEMO connects the simulated calculator automatically. Continue when it appears below."));

        if (step == WizardStep.Checksum && _store.FirmwareAssessment is not null)
        {
            var expected = VoyagerFirmwareChecksum.TestMenuDisplay(_store.FirmwareAssessment.Displayed);
            var shortForm = VoyagerFirmwareChecksum.Formatted(_store.FirmwareAssessment.Displayed);
            panel.Children.Add(BodyText($"You should see {expected}."));
            panel.Children.Add(BodyText($"If you received the expected checksum of {shortForm}, then congratulations — you have successfully updated the firmware on your {_store.FlashedModelName ?? "calculator"}."));
        }

        switch (step)
        {
            case WizardStep.Backup:
                if (_store.CurrentFirmware is null || _store.BackupAssessment is null)
                {
                    if (_store.Wizard.IsBusy)
                    {
                        panel.Children.Add(BodyText("Checking the firmware on the calculator…"));
                    }
                    else
                    {
                        panel.Children.Add(BodyText("Read the firmware on the calculator to see which version it has."));
                        panel.Children.Add(MakeActionButton("Check firmware", async () => await _store.CheckCurrentFirmwareAsync(), primary: true));
                    }
                    break;
                }

                var assessment = _store.BackupAssessment;
                panel.Children.Add(FirmwareMessageBox(
                    [("Your current firmware:", assessment.Message)],
                    caution: !assessment.IsRecognized));
                panel.Children.Add(BodyText("Do you want to save a backup of this firmware? You can use it to restore the calculator later."));
                panel.Children.Add(MakeActionButton(
                    "Save backup…",
                    _store.SaveBackup,
                    primary: !_store.Wizard.BackupResolved || _store.BackupPath is null,
                    enabled: !_store.Wizard.IsBusy));
                panel.Children.Add(MakeActionButton("Skip backup", _store.SkipBackup, enabled: !_store.Wizard.IsBusy));
                if (_store.BackupPath is not null)
                    panel.Children.Add(FirmwareMessageBox(
                        [("", $"Backup saved as {System.IO.Path.GetFileName(_store.BackupPath)}.")],
                        caution: false));
                if (_store.BackupSkipped)
                    panel.Children.Add(FirmwareMessageBox(
                        [("", "Backup skipped. You may not have a copy of this firmware to restore later.")],
                        caution: false,
                        tone: MessageTone.Amber));
                break;
            case WizardStep.Firmware:
                panel.Children.Add(MakeActionButton("Choose firmware…", _store.PickFirmware));
                if (_store.FirmwarePath is not null)
                    panel.Children.Add(MutedText(System.IO.Path.GetFileName(_store.FirmwarePath)));
                if (_store.FirmwareAssessment is { } selected)
                    panel.Children.Add(FirmwareMessageBox(
                        [("Selected firmware:", selected.Message)],
                        caution: selected.IsCaution));
                break;
            case WizardStep.Flash:
                var flashLabel = _store.SelectedMode == AppMode.Demo
                    ? "Flash simulated calculator"
                    : "Flash calculator";
                if (_store.Wizard.FlashSucceeded)
                    panel.Children.Add(FirmwareMessageBox([("", "Flashed and verified.")], caution: false));
                if (!_store.Wizard.IsBusy)
                    panel.Children.Add(MakeActionButton(
                        flashLabel,
                        async () =>
                        {
                            if (!ConfirmFlash())
                                return;
                            await _store.RunFlashAsync();
                        },
                        primary: !_store.Wizard.FlashSucceeded,
                        enabled: _store.FirmwarePath is not null));
                if (_store.PagePreviewHeader is not null)
                {
                    panel.Children.Add(new TextBlock { Text = _store.PagePreviewHeader, FontWeight = FontWeights.SemiBold, Foreground = (Brush)FindResource("InkBrush"), Margin = new Thickness(0, 12, 0, 4) });
                    foreach (var line in _store.PagePreviewLines.Take(8))
                        panel.Children.Add(new TextBlock { Text = line, FontFamily = new FontFamily("Consolas"), FontSize = 11, Foreground = (Brush)FindResource("InkBrush") });
                }
                break;
            case WizardStep.ProgrammingMode:
                if (_store.Wizard.IdentitySupported)
                    panel.Children.Add(new TextBlock { Text = "Calculator detected in programming mode.", Foreground = Brushes.DarkGreen });
                break;
        }

        var scroll = new ScrollViewer
        {
            Content = panel,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        Grid.SetRow(scroll, 0);
        contentGrid.Children.Add(scroll);

        var buttons = BuildWizardButtons(step);
        Grid.SetRow(buttons, 1);
        contentGrid.Children.Add(buttons);
        return outer;
    }

    private UIElement BuildWizardStepSidebar()
    {
        var steps = Enum.GetValues<WizardStep>();
        var panel = new StackPanel { Width = 196, Margin = new Thickness(0, 0, 20, 0) };

        for (var i = 0; i < steps.Length; i++)
        {
            panel.Children.Add(MakeSidebarRow(steps[i]));
            if (i < steps.Length - 1)
                panel.Children.Add(MakeSidebarConnector(steps[i]));
        }

        return panel;
    }

    private UIElement MakeSidebarRow(WizardStep step)
    {
        var wizard = _store.Wizard;
        var complete = wizard.IsComplete(step);
        var upcoming = wizard.IsUpcoming(step);
        var current = wizard.Step == step;

        var row = new Grid { Margin = new Thickness(0, 0, 0, 0) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        row.Children.Add(MakeSidebarMarker(step, complete, current, upcoming));

        var title = new TextBlock
        {
            Text = $"{step.Number()}. {step.Title()}",
            FontSize = 13,
            FontWeight = current ? FontWeights.SemiBold : FontWeights.Medium,
            Foreground = upcoming
                ? (Brush)FindResource("MutedBrush")
                : (Brush)FindResource("InkBrush"),
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 0, 0),
        };
        Grid.SetColumn(title, 1);
        row.Children.Add(title);

        var container = new Border
        {
            Padding = new Thickness(8, 7, 8, 7),
            Child = row,
            CornerRadius = new CornerRadius(14),
        };

        if (current)
        {
            container.Background = new SolidColorBrush(Color.FromArgb(26, 37, 99, 235));
            container.BorderBrush = new SolidColorBrush(Color.FromArgb(217, 37, 99, 235));
            container.BorderThickness = new Thickness(1.5);
        }

        return container;
    }

    private UIElement MakeSidebarMarker(WizardStep step, bool complete, bool current, bool upcoming)
    {
        var marker = new Grid { Width = 22, Height = 22 };

        if (complete)
        {
            marker.Children.Add(new Ellipse
            {
                Fill = SidebarGreen,
                Width = 22,
                Height = 22,
            });
            marker.Children.Add(new TextBlock
            {
                Text = "✓",
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            });
        }
        else if (current)
        {
            marker.Children.Add(new Ellipse
            {
                Fill = (Brush)FindResource("AccentBrush"),
                Width = 22,
                Height = 22,
            });
            marker.Children.Add(new TextBlock
            {
                Text = step.Number().ToString(),
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            });
        }
        else
        {
            marker.Children.Add(new Ellipse
            {
                Fill = new SolidColorBrush(Color.FromArgb(46, 107, 114, 128)),
                Width = 22,
                Height = 22,
            });
            marker.Children.Add(new TextBlock
            {
                Text = step.Number().ToString(),
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)FindResource("MutedBrush"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Opacity = upcoming ? 0.85 : 1,
            });
        }

        return marker;
    }

    private UIElement MakeSidebarConnector(WizardStep step)
    {
        var next = (WizardStep)((int)step + 1);
        var wizard = _store.Wizard;
        var toCurrent = wizard.Step == next;
        var toComplete = wizard.IsComplete(next);

        var accent = (Brush)FindResource("AccentBrush");
        var muted = new SolidColorBrush(Color.FromArgb(89, 107, 114, 128));

        if (toComplete || toCurrent)
        {
            return new Border
            {
                Width = 2,
                Height = 16,
                Background = toComplete ? SidebarGreen : accent,
                Margin = new Thickness(18, 0, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Left,
            };
        }

        var connector = new Grid
        {
            Height = 16,
            Width = 2,
            Margin = new Thickness(18, 0, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        connector.Children.Add(new Line
        {
            X1 = 1,
            Y1 = 0,
            X2 = 1,
            Y2 = 16,
            Stroke = muted,
            StrokeThickness = 1.5,
            StrokeDashArray = new DoubleCollection { 3, 3 },
        });
        return connector;
    }

    private UIElement BuildWizardButtons(WizardStep step)
    {
        var nav = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
        nav.Children.Add(MakeActionButton("← Welcome", _store.ReturnToWelcome));

        if (step == WizardStep.Checksum)
        {
            nav.Children.Add(MakeActionButton("Done", () => Application.Current.Shutdown(), primary: true));
            return nav;
        }

        if (_store.Wizard.CanGoBack)
            nav.Children.Add(MakeActionButton("Back", _store.WizardBack));
        if (_store.Wizard.CanAdvance)
            nav.Children.Add(MakeActionButton("Next", _store.WizardAdvance, primary: true));
        return nav;
    }

    private bool ShowStatusBar()
    {
        if (_store.ShowWelcome)
            return false;
        if (_store.SelectedMode is AppMode.Probe or AppMode.Batch)
            return true;
        return _store.Wizard.Step != WizardStep.Cable;
    }

    private bool ConfirmFlash()
    {
        var demo = _store.SelectedMode == AppMode.Demo;
        var title = demo ? "Flash the simulated calculator?" : "Flash the calculator?";
        var message = demo
            ? "DEMO writes only the simulated calculator on this PC. A real calculator is not changed.\n\nWrite starts at address 0x04000."
            : "FLASH writes a real calculator. User memory will be wiped. The bootloader at 0x0000–0x3FFF is not overwritten.\n\nWrite starts at address 0x04000.";

        return MessageBox.Show(
            message,
            title,
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning,
            MessageBoxResult.Cancel) == MessageBoxResult.OK;
    }

    private static string[] StepBodyLines(WizardStep step) => step switch
    {
        WizardStep.Cable =>
        [
            "Open the calculator's battery door and insert the POGO cable. The connector is keyed; the POGO can only be inserted one way. Make sure the plug snaps securely into place.",
            "Plug the other end of the cable (USB-A or USB-C) into this PC.",
        ],
        WizardStep.ProgrammingMode =>
        [
            "On the cable's switch box, hold ERASE, press RESET, then release ERASE. The display stays off. The calculator's ON button is ignored in this state.",
            "Once the calculator is recognized (“Connected: ATSAM4LC2C” is shown), continue with the next step.",
        ],
        // Step 3 text depends on the firmware check, so the step builds it.
        WizardStep.Backup => [],
        WizardStep.Firmware =>
        [
            "Choose a 114,688 (0x1C000) byte file with .bin extension. This app does not download HP firmware.",
        ],
        WizardStep.Flash =>
        [
            "Write starts at address 0x04000. The SAM-BA bootloader below that address is left intact.",
        ],
        WizardStep.Finish =>
        [
            "Press RESET on the cable switch-box, then turn the calculator ON. “Pr Error” in the display is expected. Press any key to see 0.0000.",
        ],
        WizardStep.Checksum =>
        [
            "Turn the calculator OFF (press ON). Hold g and ENTER, then press ON. Release ON, then release g and ENTER.",
            "The display shows the test menu: “1.L 2.C 3.H”. Press 2.",
        ],
        _ => [],
    };

    private static ScrollViewer Scroll(UIElement child) =>
        new()
        {
            Content = child,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };

    private static Image WizardDiagram(string fileName, double maxHeight = 160)
    {
        return new Image
        {
            Source = new BitmapImage(new Uri($"pack://application:,,,/Assets/{fileName}")),
            Stretch = Stretch.Uniform,
            MaxHeight = maxHeight,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 0, 0, 12),
            SnapsToDevicePixels = true,
        };
    }

    private static Image WizardSvgDiagram(string fileName, double maxHeight)
    {
        var uri = new Uri($"pack://application:,,,/Assets/{fileName}");
        using var stream = Application.GetResourceStream(uri)?.Stream
            ?? throw new InvalidOperationException($"Missing diagram {fileName}");
        using var reader = new FileSvgReader(new WpfDrawingSettings
        {
            IncludeRuntime = false,
            TextAsGeometry = true,
        }, isEmbedded: true);
        var drawing = reader.Read(stream)
            ?? throw new InvalidOperationException($"Could not render {fileName}");
        return new Image
        {
            Source = new DrawingImage(drawing),
            Stretch = Stretch.Uniform,
            MaxHeight = maxHeight,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 0, 0, 12),
            SnapsToDevicePixels = true,
        };
    }

    private TextBlock BodyText(string text) =>
        new()
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)FindResource("InkBrush"),
            Margin = new Thickness(0, 0, 0, 10),
        };

    private TextBlock MutedText(string text) =>
        new()
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)FindResource("MutedBrush"),
            Margin = new Thickness(0, 0, 0, 10),
        };

    /// <summary>
    /// Message colors: green and amber for a message appearing for the first time, blue (the
    /// current sidebar step's colors) for a message carried over from an earlier step.
    /// </summary>
    private enum MessageTone
    {
        Green,
        /// <summary>A caution, or a choice worth noting. Not an error.</summary>
        Amber,
        CarriedOver,
    }

    /// <summary>Step 5 carries both firmware messages from step 4 into one box.</summary>
    private void AddFlashFirmwareSummary(StackPanel panel)
    {
        var current = _store.BackupAssessment;
        var selected = _store.FirmwareAssessment;
        var lines = new List<(string Label, string Message)>();
        if (current is not null)
            lines.Add(("Your current firmware:", current.Summary));
        if (selected is not null)
            lines.Add(("Selected firmware:", selected.Summary));
        if (lines.Count == 0)
            return;
        panel.Children.Add(FirmwareMessageBox(
            lines,
            caution: (current is not null && !current.IsRecognized) || (selected?.IsCaution ?? false),
            tone: MessageTone.CarriedOver));
    }

    /// <summary>
    /// Step 3–5 messages. Without a tone, a new message is green, or amber when it is a caution.
    /// A caution also adds a warning icon.
    /// </summary>
    private UIElement FirmwareMessageBox(IReadOnlyList<(string Label, string Message)> lines, bool caution, MessageTone? tone = null)
    {
        Color ink, accent;
        byte fillAlpha = 36;
        switch (tone ?? (caution ? MessageTone.Amber : MessageTone.Green))
        {
            case MessageTone.Amber:
                ink = Color.FromRgb(115, 64, 0);
                accent = Color.FromRgb(245, 158, 10);
                break;
            case MessageTone.CarriedOver:
                ink = Color.FromRgb(26, 26, 26);
                accent = Color.FromRgb(37, 99, 235);
                fillAlpha = 26;
                break;
            default:
                ink = Color.FromRgb(13, 84, 36);
                accent = SidebarGreen.Color;
                break;
        }

        var text = new StackPanel();
        foreach (var (label, message) in lines)
        {
            var block = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(ink),
                Margin = new Thickness(0, text.Children.Count == 0 ? 0 : 6, 0, 0),
            };
            if (label.Length > 0)
                block.Inlines.Add(new System.Windows.Documents.Run(label + " ") { FontWeight = FontWeights.Bold });
            block.Inlines.Add(new System.Windows.Documents.Run(message));
            text.Children.Add(block);
        }

        var row = new DockPanel();
        if (caution)
        {
            var icon = new TextBlock
            {
                Text = "⚠",
                Foreground = new SolidColorBrush(Color.FromRgb(234, 88, 12)),
                FontSize = 14,
                Margin = new Thickness(0, 0, 8, 0),
            };
            DockPanel.SetDock(icon, Dock.Left);
            row.Children.Add(icon);
        }
        row.Children.Add(text);

        return new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(fillAlpha, accent.R, accent.G, accent.B)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(217, accent.R, accent.G, accent.B)),
            BorderThickness = new Thickness(1.5),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10),
            Margin = new Thickness(0, 0, 0, 12),
            Child = row,
        };
    }

    private UIElement WarningBox(string text)
    {
        return new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(255, 173, 51)),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10),
            Margin = new Thickness(0, 0, 0, 16),
            Child = new TextBlock
            {
                Text = text,
                TextWrapping = TextWrapping.Wrap,
                Foreground = Brushes.Black,
            },
        };
    }

    private UIElement MakeFooterButtons(bool showBack, string? doneLabel, Action? onDone = null)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 24, 0, 0) };
        if (showBack)
            row.Children.Add(MakeActionButton("← Welcome", _store.ReturnToWelcome));
        if (doneLabel is not null)
            row.Children.Add(MakeActionButton(doneLabel, onDone ?? (() => Application.Current.Shutdown())));
        return row;
    }

    private Button MakeActionButton(string label, Action onClick, bool primary = false, bool enabled = true)
    {
        var btn = new Button
        {
            Content = label,
            Style = (Style)FindResource(primary ? "PrimaryActionButtonStyle" : "ActionButtonStyle"),
            Margin = new Thickness(0, 0, 8, 8),
            HorizontalAlignment = HorizontalAlignment.Left,
            IsEnabled = enabled,
        };
        btn.Click += (_, _) => onClick();
        return btn;
    }
}
