using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Tack.Core.Diagnostics;
using Tack.Core.Prints;
using Tack.Core.Updates;
using Tack.Core.Updates.Feed;
using Tack.Windows.Updates;

namespace Tack.App.Updates;

/// <summary>
/// Looks for a newer Tack and installs it when the person says so, as Tack
/// 0.2 did. Tack checks once by itself, 45 s after it starts, if "Check for
/// updates when Tack starts" is on then and nothing has been checked; a check
/// the person asks for (Settings, the tray) goes ahead whatever the setting
/// says. Installing downloads the installer, checks its signature, writes
/// board.json, starts the installer and quits Tack; the installer starts it again.
/// </summary>
/// <remarks>
/// The state (<see cref="UpdaterState"/>) is kept under a lock; checks and
/// downloads wait on the thread pool, and <see cref="StatusChanged"/> is
/// raised on the UI thread. A development build, and any build run against
/// other folders (TACK_HOME), never checks or installs: it would replace the
/// installed Tack. It says so in the trace and stays Idle.
/// </remarks>
sealed class Updater : IUpdater, IDisposable
{
    static readonly TimeSpan s_startDelay = TimeSpan.FromSeconds(45);

    readonly SettingsChanges _settings;
    readonly Storage _storage;
    readonly DispatcherQueue _ui;
    readonly Action _quit;
    readonly bool _disabled;
    readonly UpdateServer _server = new("Tack", BuildInfo.Version);
    readonly CancellationTokenSource _stop = new();
    readonly Lock _lock = new();
    readonly UpdaterState _state = new();
    DispatcherQueueTimer? _timer;

    /// <param name="quit">The app's own Quit, which ends Tack once the installer has started.</param>
    /// <param name="disabled">A development build, or one run against other folders: never check or install.</param>
    public Updater(SettingsChanges settings, Storage storage, DispatcherQueue ui, Action quit, bool disabled) =>
        (_settings, _storage, _ui, _quit, _disabled) = (settings, storage, ui, quit, disabled);

    public UpdateStatus Status
    {
        get
        {
            lock (_lock)
            {
                return _state.Status;
            }
        }
    }

    public event Action? StatusChanged;

    /// <summary>
    /// Sets the one automatic check going, <see cref="s_startDelay"/> from now,
    /// and clears away installers earlier updates left in %TEMP%. UI thread, once.
    /// </summary>
    public void Start()
    {
        if (!_disabled)
        {
            // A started installer cannot be deleted while it runs, so an update's download stays behind.
            Background.Run("clearing old update downloads", InstallerFolder.ClearOld);
        }
        _timer = _ui.CreateTimer();
        _timer.Interval = s_startDelay;
        _timer.IsRepeating = false;
        // The setting is read when the check is due, so switching it off meanwhile counts.
        _timer.Tick += (_, _) => _ = Check(automatic: true);
        _timer.Start();
    }

    public Task<UpdateStatus?> CheckAsync() => Check(automatic: false);

    public async Task<UpdateStatus?> InstallAsync()
    {
        if (_disabled)
        {
            DevTrace.Write("updates: this build does not install updates (a development build, or TACK_HOME is set)");
            return Status;
        }
        UpdateOffer? offer;
        lock (_lock)
        {
            offer = _state.BeginInstall();
        }
        if (offer is null)
        {
            return Status;
        }
        Raise();

        string? installer = null;
        try
        {
            var file = await _server.DownloadAsync(offer.Url, OnPercent, _stop.Token).ConfigureAwait(false);
            installer = file;
            Verify(file, offer);
            Change(state => state.Installing());
            // The installer ends Tack if it is still running when it gets going, without the save Quit makes: write board.json first.
            await OnUi(() =>
            {
                _storage.Flush();
                InstallerLauncher.Start(file);
            }).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            if (installer is not null)
            {
                InstallerFolder.Discard(installer);
            }
            Fail("install", e);
            return Status;
        }
        DevTrace.Write($"updates: the installer for {offer.Version} started; Tack quits");
        _ui.TryEnqueue(() => _quit());
        return Status;
    }

    /// <summary>Stops the automatic check and anything on the network. Tack is quitting. UI thread.</summary>
    public void Dispose()
    {
        _timer?.Stop();
        _stop.Cancel();
        _server.Dispose();
    }

    async Task<UpdateStatus?> Check(bool automatic)
    {
        if (_disabled)
        {
            DevTrace.Write("updates: this build does not look for updates (a development build, or TACK_HOME is set)");
            return Status;
        }
        var settingOn = _settings.Current.Updates.Check;
        lock (_lock)
        {
            if (!_state.BeginCheck(automatic, settingOn))
            {
                return _state.Status;
            }
        }
        Raise();

        UpdateOffer? offer;
        try
        {
            var text = await _server.FeedAsync(new Uri(UpdateTrust.Feed), _stop.Token).ConfigureAwait(false);
            offer = UpdateFeed.Offer(UpdateFeed.Read(text), BuildInfo.Version, RuntimeInformation.ProcessArchitecture);
        }
        catch (Exception e)
        {
            Fail("check", e);
            return Status;
        }
        DevTrace.Write(offer is null ? "updates: Tack is up to date" : $"updates: version {offer.Version} is available");
        Change(state => state.Checked(offer, History.NowMs()));
        return Status;
    }

    /// <summary>The installer may run only with Tack's signature for the announced version; otherwise it goes.</summary>
    static void Verify(string installer, UpdateOffer offer)
    {
        using var file = File.OpenRead(installer);
        UpdateSignature.Check(offer.Signature, file, offer.Version);
    }

    void OnPercent(int percent)
    {
        bool changed;
        lock (_lock)
        {
            changed = _state.Downloaded(percent);
        }
        if (changed)
        {
            Raise();
        }
    }

    /// <summary>The person is told a kind of failure; what exactly went wrong goes to the trace.</summary>
    void Fail(string step, Exception error)
    {
        var kind = UpdateFailures.Classify(error);
        DevTrace.Write($"updates: the {step} failed ({kind}): {error}");
        Change(state => state.Fail(UpdateFailures.Words(kind)));
    }

    /// <summary>Changes the state under the lock, then tells whoever shows it, off the lock.</summary>
    void Change(Action<UpdaterState> change)
    {
        lock (_lock)
        {
            change(_state);
        }
        Raise();
    }

    void Raise()
    {
        if (_ui.HasThreadAccess)
        {
            StatusChanged?.Invoke();
        }
        else
        {
            _ui.TryEnqueue(() => StatusChanged?.Invoke());
        }
    }

    /// <summary>Runs <paramref name="work"/> on the UI thread and waits for it, failures included.</summary>
    Task OnUi(Action work)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var queued = _ui.TryEnqueue(() =>
        {
            try
            {
                work();
                done.SetResult();
            }
            catch (Exception e)
            {
                done.SetException(e);
            }
        });
        if (!queued)
        {
            done.SetException(new InvalidOperationException("the UI thread is gone"));
        }
        return done.Task;
    }
}
