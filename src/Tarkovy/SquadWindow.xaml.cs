using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Tarkovy.Services;

namespace Tarkovy;

public partial class SquadWindow : UserControl
{
    private bool _busy;
    private bool _passwordVisible;
    private bool _nameTaken;
    private CancellationTokenSource? _nameCheck;

    public SquadWindow()
    {
        InitializeComponent();
    }

    public event Action? Closed;

    public void LoadFromSettings()
    {
        var s = App.Settings;
        SquadNickBox.Text = string.IsNullOrWhiteSpace(s.SquadNickname) ? "Squad" : s.SquadNickname;
        var inRoom = App.Squad is { IsInRoom: true };
        SquadCodeBox.Text = inRoom
            ? App.Squad.RoomCode
            : string.IsNullOrWhiteSpace(s.SquadRoomCode) ? SquadHub.NewRoomName() : s.SquadRoomCode;
        SetPassword(s.SquadPassword ?? "", visible: false);
        if (App.Squad != null)
        {
            App.Squad.Changed -= OnSquadChanged;
            App.Squad.Changed += OnSquadChanged;
        }
        Refresh();
        if (!inRoom)
            _ = CheckNameAvailabilityAsync();
    }

    public void Detach()
    {
        if (App.Squad != null)
            App.Squad.Changed -= OnSquadChanged;
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        PersistFields();
        Detach();
        Closed?.Invoke();
    }

    private void OnSquadChanged() => Dispatcher.BeginInvoke(Refresh);

    private void SquadFields_LostFocus(object sender, RoutedEventArgs e) => PersistFields();

    private void SquadCode_LostFocus(object sender, RoutedEventArgs e)
    {
        PersistFields();
        _ = CheckNameAvailabilityAsync();
    }

    private void SquadCode_TextChanged(object sender, TextChangedEventArgs e)
    {
        _nameCheck?.Cancel();
        _nameCheck = new CancellationTokenSource();
        var ct = _nameCheck.Token;
        _ = CheckNameSoonAsync(ct);
    }

    private async Task CheckNameSoonAsync(CancellationToken ct)
    {
        try { await Task.Delay(350, ct); }
        catch (OperationCanceledException) { return; }
        if (ct.IsCancellationRequested) return;
        await CheckNameAvailabilityAsync();
    }

    private async Task CheckNameAvailabilityAsync()
    {
        if (App.Squad is { IsInRoom: true } || App.Squad == null) return;
        var code = (SquadCodeBox.Text ?? "").Trim().ToUpperInvariant();
        if (code.Length < 4)
        {
            _nameTaken = false;
            UpdateCreateEnabled();
            return;
        }

        try
        {
            _nameTaken = await App.Squad.IsRoomNameTakenAsync(code);
        }
        catch
        {
            _nameTaken = false;
        }

        UpdateCreateEnabled();
        if (_nameTaken && !_busy)
        {
            SquadStatusText.Text = Loc.T("Squad.Status.NameTaken", code);
            var warn = TryFindResource("BrushAmber") as Brush ?? Brushes.Orange;
            SquadStatusText.Foreground = warn;
            SquadStatusDot.Fill = warn;
        }
        else if (!_busy && App.Squad is not { IsInRoom: true })
        {
            SquadStatusText.Text = App.Squad?.Status ?? Loc.T("Squad.Status.Idle");
            var dim = TryFindResource("BrushTextDim") as Brush ?? Brushes.Silver;
            SquadStatusText.Foreground = dim;
            SquadStatusDot.Fill = dim;
        }
    }

    private void UpdateCreateEnabled()
    {
        var inRoom = App.Squad is { IsInRoom: true };
        SquadCreateBtn.IsEnabled = !inRoom && !_nameTaken && !_busy;
    }

    private void PersistFields()
    {
        var s = App.Settings;
        s.SquadNickname = SquadNickBox.Text.Trim();
        s.SquadRoomCode = (SquadCodeBox.Text ?? "").Trim().ToUpperInvariant();
        s.SquadPassword = ReadPassword();
        SettingsStore.Save(s);
    }

    private string ReadPassword() =>
        _passwordVisible ? SquadPasswordPlainBox.Text ?? "" : SquadPasswordBox.Password ?? "";

    private void SetPassword(string value, bool visible)
    {
        _passwordVisible = visible;
        SquadPasswordBox.Password = value;
        SquadPasswordPlainBox.Text = value;
        SquadPasswordBox.Visibility = visible ? Visibility.Collapsed : Visibility.Visible;
        SquadPasswordPlainBox.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        SquadEyeOpen.Visibility = visible ? Visibility.Collapsed : Visibility.Visible;
        SquadEyeSlash.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        SquadPasswordEyeBtn.ToolTip = Loc.T(visible ? "Squad.Password.Hide" : "Squad.Password.Show");
    }

    private void SquadPasswordEye_Click(object sender, RoutedEventArgs e) =>
        SetPassword(ReadPassword(), !_passwordVisible);

    private async void SquadGenerateName_Click(object sender, RoutedEventArgs e)
    {
        if (App.Squad is { IsInRoom: true }) return;
        await PickFreeNameAsync();
    }

    private async Task PickFreeNameAsync()
    {
        var name = App.Squad == null
            ? SquadHub.NewRoomName()
            : await App.Squad.NextFreeRoomNameAsync();
        SquadCodeBox.Text = name;
        _nameTaken = false;
        PersistFields();
        UpdateCreateEnabled();
        if (!_busy && App.Squad is not { IsInRoom: true })
        {
            SquadStatusText.Text = Loc.T("Squad.Status.Idle");
            var dim = TryFindResource("BrushTextDim") as Brush ?? Brushes.Silver;
            SquadStatusText.Foreground = dim;
            SquadStatusDot.Fill = dim;
        }
    }

    private void Refresh()
    {
        SquadStatusText.Text = App.Squad?.Status ?? Loc.T("Squad.Status.Idle");
        var dim = TryFindResource("BrushTextDim") as Brush ?? Brushes.Silver;
        var ok = TryFindResource("BrushSquadOnline") as Brush ?? Brushes.LimeGreen;
        var inRoom = App.Squad is { IsInRoom: true };
        SquadStatusText.Foreground = inRoom ? ok : dim;
        SquadStatusDot.Fill = inRoom ? ok : dim;
        if (App.Squad is { IsInRoom: true } && !string.IsNullOrWhiteSpace(App.Squad.RoomCode))
            SquadCodeBox.Text = App.Squad.RoomCode;
        UpdateCreateEnabled();
        RebuildMembers();
        SquadMembersTitle.Text = inRoom
            ? Loc.T("Squad.Members.Count", App.Squad!.Mates.Count, SquadHub.MaxPlayers)
            : Loc.T("Squad.Members");
    }

    private void RebuildMembers()
    {
        SquadMembersPanel.Children.Clear();
        var mates = App.Squad is { IsInRoom: true } ? App.Squad.Mates : [];
        if (mates.Count == 0)
        {
            SquadMembersPanel.Children.Add(new TextBlock
            {
                Text = Loc.T("Squad.Members.Empty"),
                FontSize = 11,
                Foreground = TryFindResource("BrushTextDim") as Brush ?? Brushes.Silver,
                TextWrapping = TextWrapping.Wrap
            });
            return;
        }

        var self = (App.Settings.SquadNickname ?? "").Trim();
        var text = TryFindResource("BrushText") as Brush ?? Brushes.White;
        var dim = TryFindResource("BrushTextDim") as Brush ?? Brushes.Silver;
        foreach (var m in mates)
        {
            var map = string.IsNullOrWhiteSpace(m.MapId)
                ? Loc.T("Squad.Members.NoFix")
                : App.Maps.FindById(m.MapId)?.Name ?? m.MapId;
            var you = string.Equals(m.Nick, self, StringComparison.OrdinalIgnoreCase)
                ? "  ·  " + Loc.T("Squad.Members.You")
                : "";
            SquadMembersPanel.Children.Add(new TextBlock
            {
                Text = "●  " + m.Nick + you,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = text,
                Margin = new Thickness(0, 2, 0, 0)
            });
            SquadMembersPanel.Children.Add(new TextBlock
            {
                Text = map.ToUpperInvariant(),
                FontSize = 11,
                Foreground = dim,
                Margin = new Thickness(18, 0, 0, 8)
            });
        }
    }

    private async void SquadCreate_Click(object sender, RoutedEventArgs e) =>
        await RunAsync(async () =>
        {
            if (string.IsNullOrWhiteSpace(SquadCodeBox.Text))
                await PickFreeNameAsync();
            var code = (SquadCodeBox.Text ?? "").Trim().ToUpperInvariant();
            if (App.Squad.IsInRoom && string.Equals(App.Squad.RoomCode, code, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(Loc.T("Squad.Error.AlreadyInRoom"));
            if (await App.Squad.IsRoomNameTakenAsync(code))
            {
                await PickFreeNameAsync();
                throw new InvalidOperationException(Loc.T("Squad.Error.RoomTakenRolled", SquadCodeBox.Text));
            }
            var pass = ReadPassword();
            if (pass.Length < 4)
            {
                pass = SquadHub.NewRoomPassword();
                SetPassword(pass, _passwordVisible);
            }
            PersistFields();
            await App.Squad.CreateAsync(App.Settings.SquadPassword, App.Settings.SquadNickname, App.Settings.SquadRoomCode);
            SquadCodeBox.Text = App.Squad.RoomCode;
            try { Clipboard.SetText(App.Squad.RoomCode); } catch { /* ignore */ }
        });

    private async void SquadJoin_Click(object sender, RoutedEventArgs e) =>
        await RunAsync(async () =>
        {
            PersistFields();
            await App.Squad.JoinAsync(App.Settings.SquadRoomCode, App.Settings.SquadPassword, App.Settings.SquadNickname);
            SquadCodeBox.Text = App.Squad.RoomCode;
        });

    private async void SquadLeave_Click(object sender, RoutedEventArgs e) =>
        await RunAsync(() => App.Squad.LeaveAsync());

    private void SquadCopy_Click(object sender, RoutedEventArgs e)
    {
        var code = (SquadCodeBox.Text ?? "").Trim();
        if (string.IsNullOrWhiteSpace(code)) return;
        try { Clipboard.SetText(code.ToUpperInvariant()); } catch { /* ignore */ }
    }

    private async Task RunAsync(Func<Task> action)
    {
        if (_busy || App.Squad == null) return;
        _busy = true;
        SquadStatusText.Text = Loc.T("Squad.Status.Working");
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            SquadStatusText.Text = Loc.T("Squad.Status.Error", SquadHub.FormatError(ex));
            return;
        }
        finally
        {
            _busy = false;
            UpdateCreateEnabled();
        }

        Refresh();
        if (App.Squad is not { IsInRoom: true })
            _ = CheckNameAvailabilityAsync();
    }
}
