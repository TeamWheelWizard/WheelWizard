using System.Runtime.InteropServices;
using System.Threading;
using Avalonia;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using WheelWizard.MiiImages;
using WheelWizard.MiiImages.Domain;
using WheelWizard.MiiRendering.Services;
using WheelWizard.Shared.Calendar;
using WheelWizard.WiiManagement.MiiManagement.Domain.Mii;

namespace WheelWizard.MiiImages.Views;

/// <summary>
/// Interactive 3D Mii (drag to rotate, middle-drag to pan, scroll to zoom). Draws with the realtime GPU view and
/// falls back to full-quality CPU renders when OpenGL isn't available.
/// </summary>
public partial class MiiRenderView : BaseMiiImage
{
    private readonly ISeasonalCalendar Calendar;

    private const float YawDragSensitivity = 0.8f;
    private const float PitchDragSensitivity = 0.8f;
    private const float MiddlePanSensitivity = 0.35f;
    private const float ZoomStep = 0.1f;
    private const float MinZoom = 0.35f;
    private const float MaxZoom = 1.5f;
    private const float MinCameraVerticalOffset = -90f;
    private const float MaxCameraVerticalOffset = 90f;

    private readonly IMiiNativeRenderer NativeRenderer;

    private MiiRealtimeView? _realtime;
    private string? _shownStudio;

    private readonly object _renderLock = new();
    private PendingRender? _pendingRender;
    private CancellationTokenSource? _inFlightRenderCts;
    private bool _renderWorkerRunning;
    private int _latestQueuedGeneration;
    private int _lastPresentedGeneration;

    private bool _isDragging;
    private Point _lastPointerPosition;
    private WriteableBitmap? _surfaceBitmap;
    private Mii? _currentMii;
    private string? _studioData;
    private bool _forceNextSurfaceRecreate;

    private MiiImageSpecifications _baseVariant = MiiImageVariants.FullBodyCarousel.Clone();
    private float _currentYaw;
    private float _currentPitch;
    private float _currentCameraVerticalOffset;
    private float _currentZoom = 1f;

    public static readonly StyledProperty<MiiImageSpecifications> ImageVariantProperty = AvaloniaProperty.Register<
        MiiRenderView,
        MiiImageSpecifications
    >(nameof(ImageVariant), MiiImageVariants.OnlinePlayerSmall, coerce: CoerceVariant);

    public MiiImageSpecifications ImageVariant
    {
        get => GetValue(ImageVariantProperty);
        set => SetValue(ImageVariantProperty, value);
    }

    public static readonly StyledProperty<bool> InteractiveProperty = AvaloniaProperty.Register<MiiRenderView, bool>(
        nameof(Interactive),
        true,
        coerce: CoerceInteractive
    );

    public bool Interactive
    {
        get => GetValue(InteractiveProperty);
        set => SetValue(InteractiveProperty, value);
    }

    public MiiRenderView(IMiiImagesSingletonService images, ISeasonalCalendar calendar, IMiiNativeRenderer nativeRenderer)
        : base(images)
    {
        Calendar = calendar;
        NativeRenderer = nativeRenderer;
        InitializeComponent();
        ImageBorder.IsHitTestVisible = Interactive;

        _realtime = new MiiRealtimeView(nativeRenderer) { IsPlaying = false };
        _realtime.MiiShown += OnRealtimeMiiShown;
        _realtime.RealtimeUnavailable += _ => SwitchToCpu();
        RenderHost.Children.Add(_realtime);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        InvalidatePendingWork();
        DisposeSurfaceBitmap();
        _forceNextSurfaceRecreate = false;
        RenderImage.Source = null;
    }

    private static MiiImageSpecifications CoerceVariant(AvaloniaObject o, MiiImageSpecifications value)
    {
        ((MiiRenderView)o).OnVariantChanged(value);
        return value;
    }

    private static bool CoerceInteractive(AvaloniaObject o, bool value)
    {
        ((MiiRenderView)o).OnInteractiveChanged(value);
        return value;
    }

    protected void OnVariantChanged(MiiImageSpecifications newSpecifications)
    {
        _baseVariant = newSpecifications.Clone();
        _currentYaw = _baseVariant.CharacterRotate.Y;
        _currentPitch = _baseVariant.CameraRotate.X;
        _currentCameraVerticalOffset = Math.Clamp(_baseVariant.CameraVerticalOffset, MinCameraVerticalOffset, MaxCameraVerticalOffset);
        _currentZoom = Math.Clamp(_baseVariant.CameraZoom, MinZoom, MaxZoom);
        _forceNextSurfaceRecreate = true;
        QueueRenderCurrentView();
    }

    private void OnInteractiveChanged(bool interactive)
    {
        _isDragging = false;
        if (ImageBorder != null)
            ImageBorder.IsHitTestVisible = interactive;
    }

    protected override void OnMiiChanged(Mii? newMii)
    {
        _currentMii = newMii;
        UpdateStudioDataAndQueue();
    }

    public override void RefreshCurrentMii()
    {
        _currentMii = Mii ?? _currentMii;
        UpdateStudioDataAndQueue();
    }

    private void UpdateStudioDataAndQueue()
    {
        if (_currentMii == null)
        {
            _studioData = null;
            ClearSurface();
            return;
        }

        var serialized = MiiStudioDataSerializer.Serialize(_currentMii, Calendar.IsAprilFirst);
        if (serialized.IsFailure)
        {
            _studioData = null;
            ClearSurface();
            return;
        }

        _studioData = serialized.Value;
        if (_realtime is { } realtime && IsImageAttached)
        {
            realtime.SetMii(_currentMii, _studioData);
            ImageBorder.IsVisible = true;
            MiiLoaded = _shownStudio == _studioData;
        }

        QueueRenderCurrentView();
    }

    private MiiImageSpecifications CurrentVariant()
    {
        var variant = _baseVariant.Clone();
        variant.InstanceCount = 1;
        variant.CharacterRotate = new(_baseVariant.CharacterRotate.X, NormalizeDegrees(_currentYaw), _baseVariant.CharacterRotate.Z);
        variant.CameraRotate = new(NormalizeDegrees(_currentPitch), _baseVariant.CameraRotate.Y, _baseVariant.CameraRotate.Z);
        variant.CameraVerticalOffset = Math.Clamp(_currentCameraVerticalOffset, MinCameraVerticalOffset, MaxCameraVerticalOffset);
        variant.CameraZoom = Math.Clamp(_currentZoom, MinZoom, MaxZoom);
        return variant;
    }

    private void QueueRenderCurrentView()
    {
        if (!IsImageAttached)
            return;
        var mii = _currentMii;
        if (mii == null || string.IsNullOrWhiteSpace(_studioData))
        {
            ClearSurface();
            return;
        }

        if (_realtime is { } realtime)
        {
            realtime.Specifications = CurrentVariant();
            realtime.Invalidate();
            return;
        }

        var generation = Interlocked.Increment(ref _latestQueuedGeneration);
        MiiLoaded = false;

        var shouldStartWorker = false;
        var cancellation = new CancellationTokenSource();
        lock (_renderLock)
        {
            _pendingRender?.Cancellation.Cancel();
            _pendingRender?.Cancellation.Dispose();

            _pendingRender = new PendingRender(generation, mii, _studioData!, CurrentVariant(), cancellation);
            _inFlightRenderCts?.Cancel();
            if (!_renderWorkerRunning)
            {
                _renderWorkerRunning = true;
                shouldStartWorker = true;
            }
        }

        if (shouldStartWorker)
            _ = Task.Run(RenderWorkerLoopAsync);
    }

    private void OnRealtimeMiiShown(string studioData)
    {
        _shownStudio = studioData;
        if (studioData == _studioData)
            MiiLoaded = true;
    }

    private void SwitchToCpu()
    {
        if (_realtime is not { } realtime)
            return;
        _realtime = null;
        realtime.MiiShown -= OnRealtimeMiiShown;
        RenderHost.Children.Remove(realtime);
        RenderImage.IsVisible = true;
        ImageBorder.IsVisible = false;
        _forceNextSurfaceRecreate = true;
        QueueRenderCurrentView();
    }

    private async Task RenderWorkerLoopAsync()
    {
        while (true)
        {
            PendingRender render;
            lock (_renderLock)
            {
                if (_pendingRender is not { } pending)
                {
                    _renderWorkerRunning = false;
                    return;
                }

                render = pending;
                _pendingRender = null;
                _inFlightRenderCts = render.Cancellation;
            }

            OperationResult<NativeMiiPixelBuffer> result;
            try
            {
                result = await NativeRenderer.RenderBufferAsync(
                    render.Mii,
                    render.StudioData,
                    render.Specifications,
                    render.Cancellation.Token
                );
            }
            finally
            {
                lock (_renderLock)
                {
                    if (ReferenceEquals(_inFlightRenderCts, render.Cancellation))
                        _inFlightRenderCts = null;
                }
                render.Cancellation.Dispose();
            }

            if (result.IsFailure)
            {
                await Dispatcher.UIThread.InvokeAsync(() => ClearSurface(render.Generation), DispatcherPriority.Background);
                continue;
            }

            await Dispatcher.UIThread.InvokeAsync(() => PresentBuffer(render, result.Value), DispatcherPriority.Background);
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
        if (_surfaceBitmap == null)
            return;

        using var locked = _surfaceBitmap!.Lock();
        CopyBufferToSurface(locked, buffer.BgraPixels, buffer.Width, buffer.Height);
        if (_forceNextSurfaceRecreate)
            RenderImage.Source = null;
        _forceNextSurfaceRecreate = false;
        RenderImage.Source = _surfaceBitmap;
        ImageBorder.IsVisible = true;
        RenderImage.InvalidateVisual();
        ImageBorder.InvalidateVisual();
        InvalidateVisual();
        MiiLoaded = true;
    }

    private static void CopyBufferToSurface(ILockedFramebuffer locked, byte[] pixels, int width, int height)
    {
        var rowBytes = width * 4;
        if (locked.RowBytes == rowBytes)
        {
            Marshal.Copy(pixels, 0, locked.Address, rowBytes * height);
            return;
        }

        for (var y = 0; y < height; y++)
        {
            var sourceOffset = y * rowBytes;
            var destinationRow = IntPtr.Add(locked.Address, y * locked.RowBytes);
            Marshal.Copy(pixels, sourceOffset, destinationRow, rowBytes);
        }
    }

    private void EnsureSurfaceBitmap(int width, int height, bool forceRecreate = false)
    {
        if (width <= 0 || height <= 0)
            return;

        if (!forceRecreate && _surfaceBitmap is { } existing && existing.PixelSize.Width == width && existing.PixelSize.Height == height)
            return;

        DisposeSurfaceBitmap();
        _surfaceBitmap = new WriteableBitmap(new PixelSize(width, height), new(96, 96), PixelFormat.Bgra8888, AlphaFormat.Unpremul);
    }

    private void ClearSurface(int? generation = null)
    {
        var expectedGeneration = generation ?? InvalidatePendingWork();
        if (expectedGeneration != Volatile.Read(ref _latestQueuedGeneration))
            return;

        _realtime?.SetMii(null, null);
        DisposeSurfaceBitmap();
        RenderImage.Source = null;
        ImageBorder.IsVisible = false;
        RenderImage.InvalidateVisual();
        ImageBorder.InvalidateVisual();
        InvalidateVisual();
        MiiLoaded = true;
    }

    private int InvalidatePendingWork()
    {
        var generation = Interlocked.Increment(ref _latestQueuedGeneration);
        _lastPresentedGeneration = generation;
        lock (_renderLock)
        {
            _pendingRender?.Cancellation.Cancel();
            _pendingRender?.Cancellation.Dispose();
            _pendingRender = null;

            _inFlightRenderCts?.Cancel();
            _inFlightRenderCts = null;
        }
        return generation;
    }

    private void DisposeSurfaceBitmap()
    {
        _surfaceBitmap?.Dispose();
        _surfaceBitmap = null;
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
        if (!Interactive)
            return;

        var properties = e.GetCurrentPoint(this).Properties;
        if (!properties.IsLeftButtonPressed && !properties.IsMiddleButtonPressed)
            return;

        _isDragging = true;
        _lastPointerPosition = e.GetPosition(this);
        e.Pointer.Capture(ImageBorder);
    }

    private void ImageBorder_OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!Interactive)
            return;

        if (!_isDragging)
            return;

        var currentPosition = e.GetPosition(this);
        var deltaX = currentPosition.X - _lastPointerPosition.X;
        var deltaY = currentPosition.Y - _lastPointerPosition.Y;
        _lastPointerPosition = currentPosition;

        if (Math.Abs(deltaX) < 0.5 && Math.Abs(deltaY) < 0.5)
            return;

        var properties = e.GetCurrentPoint(this).Properties;
        if (properties.IsMiddleButtonPressed)
        {
            _currentCameraVerticalOffset += (float)(deltaY * MiddlePanSensitivity);
            _currentCameraVerticalOffset = Math.Clamp(_currentCameraVerticalOffset, MinCameraVerticalOffset, MaxCameraVerticalOffset);
        }
        else
        {
            _currentYaw += (float)(deltaX * YawDragSensitivity);
            _currentPitch += (float)(deltaY * PitchDragSensitivity);
        }

        QueueRenderCurrentView();
    }

    private void ImageBorder_OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_isDragging)
            return;

        _isDragging = false;
        e.Pointer.Capture(null);
    }

    private void ImageBorder_OnPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e) => _isDragging = false;

    private void ImageBorder_OnPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (!Interactive)
            return;

        if (Math.Abs(e.Delta.Y) < float.Epsilon)
            return;

        _currentZoom -= (float)e.Delta.Y * ZoomStep;
        _currentZoom = Math.Clamp(_currentZoom, MinZoom, MaxZoom);
        QueueRenderCurrentView();
    }

    private sealed record PendingRender(
        int Generation,
        Mii Mii,
        string StudioData,
        MiiImageSpecifications Specifications,
        CancellationTokenSource Cancellation
    );
}
