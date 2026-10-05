using System.Diagnostics;
using System.IO.Abstractions;
using System.Numerics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using MiiAnim.Core.Animation;
using MiiAnim.Core.Evaluation;
using MiiAnim.Core.Format;
using MiiAnim.Core.Rig;
using WheelWizard.MiiImages;
using WheelWizard.MiiImages.Domain;
using WheelWizard.MiiImages.Views;
using WheelWizard.MiiRendering.Services;
using WheelWizard.Settings;
using WheelWizard.Shared.Desktop.Storage;
using WheelWizard.Views.Dialogs.Base;
using WheelWizard.WiiManagement;
using WheelWizard.WiiManagement.MiiManagement;
using WheelWizard.WiiManagement.MiiManagement.Domain.Mii;
using WheelWizard.WiiManagement.MiiManagement.Views.Dialogs;

namespace WheelWizard.Views.DesignTime;

/// <summary>
/// Dev tool: plays a .miianim (made with the Mii Animator) on any of the user's Miis, looping.
/// Uses the realtime GPU view; falls back to the CPU renderer when OpenGL isn't available.
/// </summary>
public partial class MiiAnimatorDevWindow : PopupContent
{
    private static readonly double[] Speeds = [0.25, 0.5, 1, 2];
    private static readonly FilePickerFileType MiiAnimFiles = new("Mii animation") { Patterns = ["*" + MiiAnimFormat.FileExtension] };
    private const string SwapGenderEvent = "swap_gender";
    private const int EventLogSize = 6;

    private IMiiDbService MiiDb { get; }
    private IMiiNativeRenderer Renderer { get; }
    private ISettingsManager Settings { get; }
    private IFilePickerService FilePicker { get; }
    private IFileSystem FileSystem { get; }

    private readonly MiiRealtimeView _realtime;
    private readonly DispatcherTimer _uiTimer = new() { Interval = TimeSpan.FromMilliseconds(50) };
    private readonly Stopwatch _fpsClock = new();
    private long _fpsFrames;
    private double _fps;

    private Mii? _mii;
    private Mii? _swappedMii;
    private bool _showingSwapped;
    private MiiAnimation? _animation;
    private float _yaw;
    private float _pitch;
    private Point? _dragStart;
    private bool _updatingSlider;
    private readonly List<string> _eventLog = [];

    // CPU fallback.
    private bool _useCpu;
    private readonly Dictionary<bool, MiiRig> _rigs = new();
    private readonly Stopwatch _cpuClock = new();
    private CancellationTokenSource? _cpuLoopCts;
    private WriteableBitmap? _bitmap;
    private double _cpuPlayheadSeconds;
    private double _cpuLastEventFrame = -1;
    private double _speed = 1;
    private bool _playing = true;

    public MiiAnimatorDevWindow(
        IMiiDbService miiDb,
        IMiiNativeRenderer renderer,
        ISettingsManager settings,
        IFilePickerService filePicker,
        IFileSystem fileSystem
    )
        : base(true, true, false, "Mii Animator")
    {
        MiiDb = miiDb;
        Renderer = renderer;
        Settings = settings;
        FilePicker = filePicker;
        FileSystem = fileSystem;
        InitializeComponent();

        // The realtime view is transparent: it sits on the preview's background like any other control.
        _realtime = new MiiRealtimeView(renderer) { IsHitTestVisible = false };
        _realtime.AnimationEvent += LogEvent;
        _realtime.RealtimeUnavailable += SwitchToCpu;
        PreviewArea.Children.Insert(0, _realtime);
        PreviewImage.IsVisible = false;
        UpdateSpecifications();

        SpeedBox.ItemsSource = Speeds.Select(s => $"{s:0.##}x speed").ToList();
        SpeedBox.SelectedIndex = Array.IndexOf(Speeds, 1d);
        SetMii(MiiDb.GetAllMiis().OrderByDescending(m => m.IsFavorite).FirstOrDefault());
        UpdateInfo();
        _uiTimer.Tick += (_, _) => UpdateUi();
    }

    protected override void BeforeOpen()
    {
        base.BeforeOpen();
        _fpsClock.Restart();
        _uiTimer.Start();
    }

    protected override void BeforeClose()
    {
        _uiTimer.Stop();
        _cpuLoopCts?.Cancel();
        base.BeforeClose();
    }

    private void SetMii(Mii? mii)
    {
        _mii = mii;
        _swappedMii = mii is null ? null : WithOtherGender(mii);
        _showingSwapped = false;
        _realtime.Mii = mii;
        if (_swappedMii is not null)
            _realtime.Prewarm(_swappedMii);
        MiiNameText.Text = mii?.Name.ToString() ?? "No Miis found";
        StatusText.Text = mii is null ? "No Miis found. Create one in the Mii Channel first." : "";
    }

    private static Mii? WithOtherGender(Mii mii)
    {
        if (
            MiiSerializer.Serialize(mii) is not { IsSuccess: true } bytes
            || MiiSerializer.Deserialize(bytes.Value) is not { IsSuccess: true } copy
        )
            return null;
        copy.Value.IsGirl = !mii.IsGirl;
        return copy.Value;
    }

    private MiiImageSpecifications Specifications() =>
        new()
        {
            Name = "MiiAnimatorDev",
            Type = MiiImageSpecifications.BodyType.all_body,
            Size = MiiImageSpecifications.ImageSize.medium,
            RenderScale = 0.8f,
            CharacterRotate = new Vector3(0, _yaw, 0),
            CameraRotate = new Vector3(_pitch, 0, 0),
            BackgroundColor = "00000000",
        };

    private void UpdateSpecifications()
    {
        _realtime.Specifications = Specifications();
        _realtime.Invalidate();
    }

    // ---------- Playback state (GPU view or CPU fallback) ----------

    private float CurrentFrame =>
        _animation is null ? 0f
        : _useCpu ? _animation.FrameAtTime(_cpuPlayheadSeconds)
        : _animation.FrameAtTime(_realtime.PlayheadFrames / Math.Max(1, _animation.Fps));

    private bool IsPlaying => _useCpu ? _playing : _realtime.IsPlaying;

    /// <summary>
    /// With "Act on swap_gender" on, the Mii shows with the other gender from the swap_gender event until the
    /// loop restarts, which is how WheelWizard would time a gender change to the animation.
    /// </summary>
    private bool ShouldShowSwapped(float frame) =>
        SwapGenderToggle.IsChecked == true
        && _swappedMii is not null
        && _animation is not null
        && _animation.Events.Any(e => e.Name == SwapGenderEvent && frame >= e.Frame);

    private void UpdateUi()
    {
        var frame = CurrentFrame;
        var swapped = ShouldShowSwapped(frame);
        if (swapped != _showingSwapped && !_useCpu)
        {
            _showingSwapped = swapped;
            _realtime.Mii = swapped ? _swappedMii : _mii;
        }

        if (!_useCpu && _fpsClock.Elapsed.TotalSeconds >= 0.5)
        {
            _fps = (_realtime.RenderedFrames - _fpsFrames) / _fpsClock.Elapsed.TotalSeconds;
            _fpsFrames = _realtime.RenderedFrames;
            _fpsClock.Restart();
        }

        PlayButton.Text = IsPlaying ? "Pause" : "Play";
        var renderer = _useCpu ? "CPU renderer (no OpenGL)" : $"GPU · {_fps:0} fps";
        if (_animation is null)
        {
            FrameText.Text = $"No animation loaded — showing the rest pose.\n{renderer}";
            return;
        }

        _updatingSlider = true;
        FrameSlider.Maximum = _animation.Length;
        FrameSlider.Value = frame;
        _updatingSlider = false;
        FrameText.Text = $"Frame {frame:0} / {_animation.Length}\n{renderer}";
    }

    private void LogEvent(AnimEvent animEvent)
    {
        _eventLog.Insert(0, $"{animEvent.Name}  (frame {animEvent.Frame})");
        if (_eventLog.Count > EventLogSize)
            _eventLog.RemoveAt(_eventLog.Count - 1);
        UpdateEventsText();
    }

    private void UpdateEventsText() =>
        EventsText.Text =
            _animation is null || _animation.Events.Count == 0 ? "This animation has no events."
            : _eventLog.Count == 0 ? "Waiting for the first event…"
            : "Fired (newest first):\n" + string.Join("\n", _eventLog);

    private void UpdateInfo()
    {
        UpdateEventsText();
        if (_animation is null)
        {
            InfoText.Text = "Load an animation made with the Mii Animator.";
            return;
        }

        var keys = _animation.Tracks.Sum(t => t.Value.Count);
        var author =
            _animation.AuthorMii is { } bytes && MiiSerializer.Deserialize(bytes) is { IsSuccess: true } authorMii
                ? authorMii.Value.Name.ToString()
                : "none";
        InfoText.Text =
            $"{_animation.Name}\n{_animation.Length} frames @ {_animation.Fps} fps ({_animation.DurationSeconds:0.##}s)\n"
            + $"{keys} keys on {_animation.Tracks.Count} channels\nMade with Mii: {author}"
            + (_animation.Events.Count == 0 ? "" : "\nEvents: " + string.Join(", ", _animation.Events.Select(e => $"{e.Name}@{e.Frame}")));
    }

    // ---------- CPU fallback ----------

    private void SwitchToCpu(string reason)
    {
        if (_useCpu)
            return;
        _useCpu = true;
        PreviewArea.Children.Remove(_realtime);
        PreviewImage.IsVisible = true;
        _cpuPlayheadSeconds = _realtime.PlayheadFrames / Math.Max(1, _animation?.Fps ?? 60);
        _cpuLastEventFrame = _realtime.PlayheadFrames;
        _playing = _realtime.IsPlaying;
        _speed = _realtime.Speed;
        StatusText.Text = "";
        _cpuClock.Restart();
        _cpuLoopCts = new CancellationTokenSource();
        _ = CpuRenderLoopAsync(_cpuLoopCts.Token);
        InfoText.Text += $"\n(Realtime view unavailable: {reason})";
    }

    private MiiRig RigFor(Mii mii)
    {
        if (!_rigs.TryGetValue(mii.IsGirl, out var rig))
            _rigs[mii.IsGirl] = rig = new MiiRig(MiiBodyModel.Get(mii.IsGirl));
        return rig;
    }

    private async Task CpuRenderLoopAsync(CancellationToken token)
    {
        var lastTick = _cpuClock.Elapsed;
        while (!token.IsCancellationRequested)
        {
            var now = _cpuClock.Elapsed;
            if (_playing)
                _cpuPlayheadSeconds += (now - lastTick).TotalSeconds * _speed;
            lastTick = now;

            var frame = CurrentFrame;
            if (_animation is not null)
            {
                var absolute = _cpuPlayheadSeconds * _animation.Fps;
                foreach (var animEvent in _animation.EventsBetween(_cpuLastEventFrame, absolute).ToList())
                    LogEvent(animEvent);
                _cpuLastEventFrame = absolute;
            }

            var mii = ShouldShowSwapped(frame) ? _swappedMii! : _mii;
            if (mii is null || MiiStudioDataSerializer.Serialize(mii) is not { IsSuccess: true } studio)
            {
                await Task.Delay(100, token).ContinueWith(_ => { });
                continue;
            }

            var rig = RigFor(mii);
            var pose = _animation is null ? rig.RestPose : rig.Evaluate(_animation, frame);
            var result = await Renderer.RenderPosedBufferAsync(mii, studio.Value, Specifications(), pose, token);
            if (token.IsCancellationRequested)
                return;
            if (result.IsSuccess)
                Present(result.Value);
            else
            {
                StatusText.Text = result.Error!.Message;
                await Task.Delay(500, token).ContinueWith(_ => { });
            }

            await Task.Yield();
        }
    }

    private void Present(NativeMiiPixelBuffer buffer)
    {
        if (_bitmap is null || _bitmap.PixelSize.Width != buffer.Width || _bitmap.PixelSize.Height != buffer.Height)
        {
            _bitmap = new WriteableBitmap(
                new PixelSize(buffer.Width, buffer.Height),
                new Avalonia.Vector(96, 96),
                PixelFormat.Bgra8888,
                AlphaFormat.Unpremul
            );
            PreviewImage.Source = _bitmap;
        }

        using (var locked = _bitmap.Lock())
        {
            var rowBytes = buffer.Width * 4;
            for (var y = 0; y < buffer.Height; y++)
                Marshal.Copy(buffer.BgraPixels, y * rowBytes, locked.Address + y * locked.RowBytes, rowBytes);
        }

        PreviewImage.InvalidateVisual();
    }

    // ---------- UI events ----------

    private async void ChooseMii_OnClick(object? sender, RoutedEventArgs e)
    {
        var miis = MiiDb.GetAllMiis();
        if (miis.Count == 0)
            return;
        var selected = await new MiiSelectorWindow().SetMiiOptions(miis, _mii, Settings.Get<string>(Settings.MACADDRESS)).AwaitAnswer();
        if (selected is not null)
            SetMii(selected);
    }

    private async void LoadAnimation_OnClick(object? sender, RoutedEventArgs e)
    {
        var path = await FilePicker.OpenSingleFileAsync("Select a Mii animation", [MiiAnimFiles]);
        if (path is null)
            return;
        try
        {
            _animation = MiiAnimFormat.Read(FileSystem.File.ReadAllBytes(path));
            _eventLog.Clear();
            _realtime.Animation = _animation;
            _realtime.IsPlaying = true;
            _cpuPlayheadSeconds = 0;
            _cpuLastEventFrame = -1;
            _playing = true;
            UpdateInfo();
        }
        catch (Exception exception)
        {
            InfoText.Text = "Couldn't load animation: " + exception.Message;
        }
    }

    private void PlayPause_OnClick(object? sender, RoutedEventArgs e)
    {
        _playing = !IsPlaying;
        _realtime.IsPlaying = _playing;
        _realtime.Invalidate();
    }

    private void Speed_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (SpeedBox.SelectedIndex < 0)
            return;
        _speed = Speeds[SpeedBox.SelectedIndex];
        _realtime.Speed = _speed;
    }

    private void FrameSlider_OnValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (_updatingSlider || _animation is null)
            return;
        // Scrubbing pauses and jumps without firing the events in between.
        _playing = false;
        _realtime.IsPlaying = false;
        _realtime.Seek(e.NewValue);
        _cpuPlayheadSeconds = e.NewValue / Math.Max(1, _animation.Fps);
        _cpuLastEventFrame = e.NewValue;
    }

    private void Preview_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _dragStart = e.GetPosition(PreviewArea);
        e.Pointer.Capture(PreviewArea);
    }

    private void Preview_OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragStart is not { } start)
            return;
        var point = e.GetPosition(PreviewArea);
        _yaw = (_yaw + (float)(point.X - start.X) * 0.6f) % 360f;
        _pitch = Math.Clamp(_pitch + (float)(point.Y - start.Y) * 0.3f, -40f, 40f);
        _dragStart = point;
        UpdateSpecifications();
    }

    private void Preview_OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _dragStart = null;
        e.Pointer.Capture(null);
    }
}
