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
using MiiAnim.Core.Animation;
using MiiAnim.Core.Evaluation;
using MiiAnim.Core.Format;
using MiiAnim.Core.Rig;
using WheelWizard.MiiImages;
using WheelWizard.MiiImages.Domain;
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
/// </summary>
public partial class MiiAnimatorDevWindow : PopupContent
{
    private static readonly double[] Speeds = [0.25, 0.5, 1, 2];
    private static readonly FilePickerFileType MiiAnimFiles = new("Mii animation") { Patterns = ["*" + MiiAnimFormat.FileExtension] };

    private IMiiDbService MiiDb { get; }
    private IMiiNativeRenderer Renderer { get; }
    private ISettingsManager Settings { get; }
    private IFilePickerService FilePicker { get; }
    private IFileSystem FileSystem { get; }

    private readonly Dictionary<bool, MiiRig> _rigs = new();
    private readonly Stopwatch _clock = new();
    private CancellationTokenSource? _loopCts;
    private WriteableBitmap? _bitmap;

    private Mii? _mii;
    private string? _studioData;
    private MiiAnimation? _animation;
    private double _playheadSeconds;
    private double _speed = 1;
    private bool _playing = true;
    private float _yaw;
    private float _pitch;
    private Point? _dragStart;
    private bool _updatingSlider;

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

        SpeedBox.ItemsSource = Speeds.Select(s => $"{s:0.##}x speed").ToList();
        SpeedBox.SelectedIndex = Array.IndexOf(Speeds, 1d);
        SetMii(MiiDb.GetAllMiis().OrderByDescending(m => m.IsFavorite).FirstOrDefault());
        UpdateInfo();
    }

    protected override void BeforeOpen()
    {
        base.BeforeOpen();
        _clock.Restart();
        _loopCts = new CancellationTokenSource();
        _ = RenderLoopAsync(_loopCts.Token);
    }

    protected override void BeforeClose()
    {
        _loopCts?.Cancel();
        base.BeforeClose();
    }

    private void SetMii(Mii? mii)
    {
        _mii = mii;
        _studioData =
            mii is null ? null
            : MiiStudioDataSerializer.Serialize(mii) is { IsSuccess: true } studio ? studio.Value
            : null;
        MiiNameText.Text = mii?.Name.ToString() ?? "No Miis found";
        StatusText.Text = mii is null ? "No Miis found. Create one in the Mii Channel first." : "";
    }

    private MiiRig RigFor(Mii mii)
    {
        if (!_rigs.TryGetValue(mii.IsGirl, out var rig))
            _rigs[mii.IsGirl] = rig = new MiiRig(MiiBodyModel.Get(mii.IsGirl));
        return rig;
    }

    // ---------- Rendering ----------

    private async Task RenderLoopAsync(CancellationToken token)
    {
        var lastTick = _clock.Elapsed;
        while (!token.IsCancellationRequested)
        {
            var now = _clock.Elapsed;
            if (_playing)
                _playheadSeconds += (now - lastTick).TotalSeconds * _speed;
            lastTick = now;

            if (_mii is null || _studioData is null)
            {
                await Task.Delay(100, token).ContinueWith(_ => { });
                continue;
            }

            var rig = RigFor(_mii);
            var frame = _animation?.FrameAtTime(_playheadSeconds) ?? 0f;
            var pose = _animation is null ? rig.RestPose : rig.Evaluate(_animation, frame);
            var specifications = new MiiImageSpecifications
            {
                Name = "MiiAnimatorDev",
                Type = MiiImageSpecifications.BodyType.all_body,
                Size = MiiImageSpecifications.ImageSize.medium,
                RenderScale = 0.8f,
                CharacterRotate = new Vector3(0, _yaw, 0),
                CameraRotate = new Vector3(_pitch, 0, 0),
                BackgroundColor = "00000000",
            };

            var result = await Renderer.RenderPosedBufferAsync(_mii, _studioData, specifications, pose, token);
            if (token.IsCancellationRequested)
                return;
            if (result.IsSuccess)
            {
                Present(result.Value);
                StatusText.Text = "";
            }
            else
            {
                StatusText.Text = result.Error!.Message;
                await Task.Delay(500, token).ContinueWith(_ => { });
            }

            UpdatePlaybackUi(frame);
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

    private void UpdatePlaybackUi(float frame)
    {
        PlayButton.Text = _playing ? "Pause" : "Play";
        if (_animation is null)
        {
            FrameText.Text = "No animation loaded — showing the rest pose.";
            return;
        }

        _updatingSlider = true;
        FrameSlider.Maximum = _animation.Length;
        FrameSlider.Value = frame;
        _updatingSlider = false;
        FrameText.Text = $"Frame {frame:0} / {_animation.Length}";
    }

    private void UpdateInfo()
    {
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
            + $"{keys} keys on {_animation.Tracks.Count} channels\nMade with Mii: {author}";
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
            _playheadSeconds = 0;
            _playing = true;
            UpdateInfo();
        }
        catch (Exception exception)
        {
            InfoText.Text = "Couldn't load animation: " + exception.Message;
        }
    }

    private void PlayPause_OnClick(object? sender, RoutedEventArgs e) => _playing = !_playing;

    private void Speed_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (SpeedBox.SelectedIndex >= 0)
            _speed = Speeds[SpeedBox.SelectedIndex];
    }

    private void FrameSlider_OnValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (_updatingSlider || _animation is null)
            return;
        _playing = false;
        _playheadSeconds = e.NewValue / Math.Max(1, _animation.Fps);
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
    }

    private void Preview_OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _dragStart = null;
        e.Pointer.Capture(null);
    }
}
