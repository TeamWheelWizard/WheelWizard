using System.Runtime.InteropServices;
using System.Threading;
using Avalonia;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WheelWizard.MiiImages;
using WheelWizard.MiiImages.Domain;
using WheelWizard.MiiRendering.Services;
using WheelWizard.Shared.DependencyInjection;
using WheelWizard.WiiManagement.MiiManagement.Domain.Mii;

namespace WheelWizard.Views.Patterns;

public partial class MiiCarousel : BaseMiiImage
{
    private const float YawDragSensitivity = 0.8f;
    private const float PitchDragSensitivity = 0.8f;
    private const float ZoomStep = 0.1f;
    private const float MinZoom = 0.35f;
    private const float MaxZoom = 3f;

    [Inject]
    private IMiiNativeRenderer NativeRenderer { get; set; } = null!;

    private readonly object _pendingRenderLock = new();
    private PendingRender? _pendingRender;
    private bool _renderWorkerRunning;
    private int _latestQueuedGeneration;
    private int _lastPresentedGeneration;

    private bool _isDragging;
    private Point _lastPointerPosition;
    private WriteableBitmap? _surfaceBitmap;
    private Mii? _currentMii;
    private string? _studioData;

    private MiiImageSpecifications _baseVariant = MiiImageVariants.FullBodyCarousel.Clone();
    private float _currentYaw;
    private float _currentPitch;
    private float _currentZoom = 1f;

    public static readonly StyledProperty<MiiImageSpecifications> ImageVariantProperty = AvaloniaProperty.Register<
        MiiCarousel,
        MiiImageSpecifications
    >(nameof(ImageVariant), MiiImageVariants.OnlinePlayerSmall, coerce: CoerceVariant);

    public MiiImageSpecifications ImageVariant
    {
        get => GetValue(ImageVariantProperty);
        set => SetValue(ImageVariantProperty, value);
    }

    public MiiCarousel()
    {
        InitializeComponent();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        InvalidatePendingWork();
        _surfaceBitmap?.Dispose();
        _surfaceBitmap = null;
        RenderImage.Source = null;
    }

    private static MiiImageSpecifications CoerceVariant(AvaloniaObject o, MiiImageSpecifications value)
    {
        ((MiiCarousel)o).OnVariantChanged(value);
        return value;
    }

    protected void OnVariantChanged(MiiImageSpecifications newSpecifications)
    {
        _baseVariant = newSpecifications.Clone();
        _currentYaw = _baseVariant.CharacterRotate.Y;
        _currentPitch = _baseVariant.CameraRotate.X;
        _currentZoom = Math.Clamp(_baseVariant.CameraZoom, MinZoom, MaxZoom);
        QueueRenderCurrentView();
    }

    protected override void OnMiiChanged(Mii? newMii)
    {
        _currentMii = newMii;
        _currentYaw = _baseVariant.CharacterRotate.Y;
        _currentPitch = _baseVariant.CameraRotate.X;
        _currentZoom = Math.Clamp(_baseVariant.CameraZoom, MinZoom, MaxZoom);

        if (newMii == null)
        {
            _studioData = null;
            ClearSurface();
            return;
        }

        var serialized = MiiStudioDataSerializer.Serialize(newMii);
        if (serialized.IsFailure)
        {
            _studioData = null;
            ClearSurface();
            return;
        }

        _studioData = serialized.Value;
        QueueRenderCurrentView();
    }

    private void QueueRenderCurrentView()
    {
        var mii = _currentMii;
        if (mii == null || string.IsNullOrWhiteSpace(_studioData))
        {
            ClearSurface();
            return;
        }

        var variant = _baseVariant.Clone();
        variant.InstanceCount = 1;
        variant.CharacterRotate = new(_baseVariant.CharacterRotate.X, NormalizeDegrees(_currentYaw), _baseVariant.CharacterRotate.Z);
        variant.CameraRotate = new(NormalizeDegrees(_currentPitch), _baseVariant.CameraRotate.Y, _baseVariant.CameraRotate.Z);
        variant.CameraZoom = Math.Clamp(_currentZoom, MinZoom, MaxZoom);

        var generation = Interlocked.Increment(ref _latestQueuedGeneration);
        MiiLoaded = false;

        var shouldStartWorker = false;
        lock (_pendingRenderLock)
        {
            _pendingRender = new PendingRender(generation, mii, _studioData!, variant);
            if (!_renderWorkerRunning)
            {
                _renderWorkerRunning = true;
                shouldStartWorker = true;
            }
        }

        if (shouldStartWorker)
            _ = Task.Run(RenderWorkerLoopAsync);
    }

    private async Task RenderWorkerLoopAsync()
    {
        while (true)
        {
            PendingRender render;
            lock (_pendingRenderLock)
            {
                if (_pendingRender is not { } pending)
                {
                    _renderWorkerRunning = false;
                    return;
                }

                render = pending;
                _pendingRender = null;
            }

            var result = await NativeRenderer.RenderBufferAsync(render.Mii, render.StudioData, render.Specifications);

            if (result.IsFailure)
            {
                continue;
            }

            await Dispatcher.UIThread.InvokeAsync(() => PresentBuffer(render, result.Value));
        }
    }

    private void PresentBuffer(PendingRender render, NativeMiiPixelBuffer buffer)
    {
        // If the active Mii changed while this frame was rendering, skip stale frame presentation.
        if (!string.Equals(render.StudioData, _studioData, StringComparison.Ordinal))
            return;

        if (render.Generation < _lastPresentedGeneration)
            return;

        _lastPresentedGeneration = render.Generation;
        EnsureSurfaceBitmap(buffer.Width, buffer.Height);
        using var locked = _surfaceBitmap!.Lock();
        CopyBufferToSurface(locked, buffer.BgraPixels, buffer.Width, buffer.Height);
        RenderImage.Source = _surfaceBitmap;
        ImageBorder.IsVisible = true;
        MiiLoaded = true;
    }

    private static void CopyBufferToSurface(ILockedFramebuffer locked, byte[] pixels, int width, int height)
    {
        var rowBytes = width * 4;
        for (var y = 0; y < height; y++)
        {
            var sourceOffset = y * rowBytes;
            var destinationRow = IntPtr.Add(locked.Address, y * locked.RowBytes);
            Marshal.Copy(pixels, sourceOffset, destinationRow, rowBytes);
        }
    }

    private void EnsureSurfaceBitmap(int width, int height)
    {
        if (_surfaceBitmap is { } existing && existing.PixelSize.Width == width && existing.PixelSize.Height == height)
            return;

        _surfaceBitmap?.Dispose();
        _surfaceBitmap = new WriteableBitmap(new PixelSize(width, height), new(96, 96), PixelFormat.Bgra8888, AlphaFormat.Unpremul);
    }

    private void ClearSurface(int? generation = null)
    {
        var expectedGeneration = generation ?? InvalidatePendingWork();
        if (expectedGeneration != Volatile.Read(ref _latestQueuedGeneration))
            return;

        RenderImage.Source = null;
        ImageBorder.IsVisible = false;
        MiiLoaded = true;
    }

    private int InvalidatePendingWork()
    {
        var generation = Interlocked.Increment(ref _latestQueuedGeneration);
        _lastPresentedGeneration = generation;
        lock (_pendingRenderLock)
            _pendingRender = null;
        return generation;
    }

    private static float NormalizeDegrees(float degrees)
    {
        var normalized = degrees % 360f;
        if (normalized < 0f)
            normalized += 360f;
        return normalized;
    }

    private void ImageBorder_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        _isDragging = true;
        _lastPointerPosition = e.GetPosition(this);
        e.Pointer.Capture(ImageBorder);
    }

    private void ImageBorder_OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isDragging)
            return;

        var currentPosition = e.GetPosition(this);
        var deltaX = currentPosition.X - _lastPointerPosition.X;
        var deltaY = currentPosition.Y - _lastPointerPosition.Y;
        _lastPointerPosition = currentPosition;

        if (Math.Abs(deltaX) < 0.5 && Math.Abs(deltaY) < 0.5)
            return;

        _currentYaw += (float)(deltaX * YawDragSensitivity);
        _currentPitch += (float)(deltaY * PitchDragSensitivity);
        QueueRenderCurrentView();
    }

    private void ImageBorder_OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_isDragging)
            return;

        _isDragging = false;
        e.Pointer.Capture(null);
    }

    private void ImageBorder_OnPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        _isDragging = false;
    }

    private void ImageBorder_OnPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (Math.Abs(e.Delta.Y) < float.Epsilon)
            return;

        _currentZoom -= (float)e.Delta.Y * ZoomStep;
        _currentZoom = Math.Clamp(_currentZoom, MinZoom, MaxZoom);
        QueueRenderCurrentView();
    }

    private readonly record struct PendingRender(int Generation, Mii Mii, string StudioData, MiiImageSpecifications Specifications);
}
