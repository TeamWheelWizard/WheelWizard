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

public partial class Mii3DRender : BaseMiiImage
{
    private const float YawDragSensitivity = 0.8f;
    private const float PitchDragSensitivity = 0.8f;
    private const float ZoomStep = 0.1f;
    private const float MinZoom = 0.35f;
    private const float MaxZoom = 3f;
    private static readonly TimeSpan RapidModelUpdateThreshold = TimeSpan.FromMilliseconds(120);

    [Inject]
    private IMiiNativeRenderer NativeRenderer { get; set; } = null!;

    private readonly object _renderLock = new();
    private PendingRender? _pendingRender;
    private CancellationTokenSource? _inFlightRenderCts;
    private bool _renderWorkerRunning;
    private int _latestQueuedGeneration;
    private int _lastPresentedGeneration;

    private bool _isDragging;
    private Point _lastPointerPosition;
    private CancellationTokenSource? _interactionSettleCts;
    private WriteableBitmap? _surfaceBitmap;
    private byte[]? _upscaledPreviewBuffer;
    private Mii? _currentMii;
    private string? _studioData;
    private bool _forceNextSurfaceRecreate;
    private int _stableSurfaceWidth;
    private int _stableSurfaceHeight;
    private DateTime _lastModelChangeUtc = DateTime.MinValue;
    private bool _hasPresentedFrame;

    private MiiImageSpecifications _baseVariant = MiiImageVariants.FullBodyCarousel.Clone();
    private float _currentYaw;
    private float _currentPitch;
    private float _currentZoom = 1f;

    public static readonly StyledProperty<MiiImageSpecifications> ImageVariantProperty = AvaloniaProperty.Register<
        Mii3DRender,
        MiiImageSpecifications
    >(nameof(ImageVariant), MiiImageVariants.OnlinePlayerSmall, coerce: CoerceVariant);

    public MiiImageSpecifications ImageVariant
    {
        get => GetValue(ImageVariantProperty);
        set => SetValue(ImageVariantProperty, value);
    }

    public static readonly StyledProperty<bool> InteractiveProperty = AvaloniaProperty.Register<Mii3DRender, bool>(
        nameof(Interactive),
        true,
        coerce: CoerceInteractive
    );

    public bool Interactive
    {
        get => GetValue(InteractiveProperty);
        set => SetValue(InteractiveProperty, value);
    }

    public static readonly StyledProperty<float> PreviewRenderScaleProperty = AvaloniaProperty.Register<Mii3DRender, float>(
        nameof(PreviewRenderScale),
        0.2f
    );

    public float PreviewRenderScale
    {
        get => GetValue(PreviewRenderScaleProperty);
        set => SetValue(PreviewRenderScaleProperty, value);
    }

    public static readonly StyledProperty<int> HighQualitySettleDelayMsProperty = AvaloniaProperty.Register<Mii3DRender, int>(
        nameof(HighQualitySettleDelayMs),
        90
    );

    public int HighQualitySettleDelayMs
    {
        get => GetValue(HighQualitySettleDelayMsProperty);
        set => SetValue(HighQualitySettleDelayMsProperty, value);
    }

    public Mii3DRender()
    {
        InitializeComponent();
        ImageBorder.IsHitTestVisible = Interactive;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        InvalidatePendingWork();
        DisposeInteractionSettleCts();
        DisposeSurfaceBitmap();
        _upscaledPreviewBuffer = null;
        _stableSurfaceWidth = 0;
        _stableSurfaceHeight = 0;
        _forceNextSurfaceRecreate = false;
        _hasPresentedFrame = false;
        _lastModelChangeUtc = DateTime.MinValue;
        RenderImage.Source = null;
    }

    private static MiiImageSpecifications CoerceVariant(AvaloniaObject o, MiiImageSpecifications value)
    {
        ((Mii3DRender)o).OnVariantChanged(value);
        return value;
    }

    private static bool CoerceInteractive(AvaloniaObject o, bool value)
    {
        ((Mii3DRender)o).OnInteractiveChanged(value);
        return value;
    }

    protected void OnVariantChanged(MiiImageSpecifications newSpecifications)
    {
        _baseVariant = newSpecifications.Clone();
        _currentYaw = _baseVariant.CharacterRotate.Y;
        _currentPitch = _baseVariant.CameraRotate.X;
        _currentZoom = Math.Clamp(_baseVariant.CameraZoom, MinZoom, MaxZoom);
        _stableSurfaceWidth = 0;
        _stableSurfaceHeight = 0;
        _upscaledPreviewBuffer = null;
        _forceNextSurfaceRecreate = true;
        QueueRenderCurrentView(renderScale: 1f);
    }

    private void OnInteractiveChanged(bool interactive)
    {
        _isDragging = false;
        if (ImageBorder != null)
            ImageBorder.IsHitTestVisible = interactive;
        if (!interactive)
        {
            DisposeInteractionSettleCts();
            QueueRenderCurrentView(renderScale: 1f);
        }
    }

    protected override void OnMiiChanged(Mii? newMii)
    {
        _currentMii = newMii;
        UpdateStudioDataAndQueue(forcePreview: false);
    }

    public void RefreshCurrentMii()
    {
        _currentMii = Mii ?? _currentMii;
        UpdateStudioDataAndQueue(forcePreview: true);
    }

    private void UpdateStudioDataAndQueue(bool forcePreview)
    {
        if (_currentMii == null)
        {
            _studioData = null;
            ClearSurface();
            return;
        }

        var serialized = MiiStudioDataSerializer.Serialize(_currentMii);
        if (serialized.IsFailure)
        {
            _studioData = null;
            ClearSurface();
            return;
        }

        _studioData = serialized.Value;

        var shouldUsePreview = forcePreview || ShouldUsePreviewForModelChange();
        if (shouldUsePreview)
        {
            QueueRenderCurrentView(renderScale: GetPreviewRenderScale());
            ScheduleHighQualityRefresh();
            return;
        }

        QueueRenderCurrentView(renderScale: 1f);
    }

    private void QueueRenderCurrentView(float renderScale)
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
        variant.RenderScale = Math.Clamp(renderScale, 0.05f, 1f);

        var generation = Interlocked.Increment(ref _latestQueuedGeneration);
        MiiLoaded = false;

        var shouldStartWorker = false;
        var cancellation = new CancellationTokenSource();
        lock (_renderLock)
        {
            _pendingRender?.Cancellation.Cancel();
            _pendingRender?.Cancellation.Dispose();

            _pendingRender = new PendingRender(generation, mii, _studioData!, variant, cancellation);
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

        var renderScale = Math.Clamp(render.Specifications.RenderScale, 0.05f, 1f);
        var surfaceWidth = buffer.Width;
        var surfaceHeight = buffer.Height;
        byte[] pixelsToPresent = buffer.BgraPixels;

        if (renderScale >= 0.999f)
        {
            _stableSurfaceWidth = buffer.Width;
            _stableSurfaceHeight = buffer.Height;
        }
        else
        {
            if (_stableSurfaceWidth > 0 && _stableSurfaceHeight > 0)
            {
                surfaceWidth = _stableSurfaceWidth;
                surfaceHeight = _stableSurfaceHeight;
            }
            else if (_surfaceBitmap is { } existingSurface)
            {
                surfaceWidth = existingSurface.PixelSize.Width;
                surfaceHeight = existingSurface.PixelSize.Height;
            }

            if (surfaceWidth != buffer.Width || surfaceHeight != buffer.Height)
                pixelsToPresent = UpscalePreviewBuffer(buffer.BgraPixels, buffer.Width, buffer.Height, surfaceWidth, surfaceHeight);
        }

        EnsureSurfaceBitmap(surfaceWidth, surfaceHeight);
        if (_surfaceBitmap == null)
            return;

        using var locked = _surfaceBitmap!.Lock();
        CopyBufferToSurface(locked, pixelsToPresent, surfaceWidth, surfaceHeight);
        var forceFullSurfaceRefresh = _forceNextSurfaceRecreate || renderScale < 0.999f;
        if (forceFullSurfaceRefresh)
            RenderImage.Source = null;
        _forceNextSurfaceRecreate = false;
        RenderImage.Source = _surfaceBitmap;
        ImageBorder.IsVisible = true;
        _hasPresentedFrame = true;
        MiiLoaded = true;
    }

    private byte[] UpscalePreviewBuffer(byte[] source, int sourceWidth, int sourceHeight, int targetWidth, int targetHeight)
    {
        if (sourceWidth <= 0 || sourceHeight <= 0 || targetWidth <= 0 || targetHeight <= 0)
            return source;

        if (sourceWidth == targetWidth && sourceHeight == targetHeight)
            return source;

        var requiredLength = checked(targetWidth * targetHeight * 4);
        if (_upscaledPreviewBuffer == null || _upscaledPreviewBuffer.Length != requiredLength)
            _upscaledPreviewBuffer = new byte[requiredLength];

        for (var y = 0; y < targetHeight; y++)
        {
            var srcY = Math.Min(sourceHeight - 1, y * sourceHeight / targetHeight);
            var srcRow = srcY * sourceWidth * 4;
            var dstRow = y * targetWidth * 4;

            for (var x = 0; x < targetWidth; x++)
            {
                var srcX = Math.Min(sourceWidth - 1, x * sourceWidth / targetWidth);
                var srcIndex = srcRow + srcX * 4;
                var dstIndex = dstRow + x * 4;
                _upscaledPreviewBuffer[dstIndex + 0] = source[srcIndex + 0];
                _upscaledPreviewBuffer[dstIndex + 1] = source[srcIndex + 1];
                _upscaledPreviewBuffer[dstIndex + 2] = source[srcIndex + 2];
                _upscaledPreviewBuffer[dstIndex + 3] = source[srcIndex + 3];
            }
        }

        return _upscaledPreviewBuffer;
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

        DisposeSurfaceBitmap();
        RenderImage.Source = null;
        ImageBorder.IsVisible = false;
        _upscaledPreviewBuffer = null;
        _stableSurfaceWidth = 0;
        _stableSurfaceHeight = 0;
        _hasPresentedFrame = false;
        _lastModelChangeUtc = DateTime.MinValue;
        MiiLoaded = true;
    }

    private int InvalidatePendingWork()
    {
        var generation = Interlocked.Increment(ref _latestQueuedGeneration);
        _lastPresentedGeneration = generation;
        DisposeInteractionSettleCts();
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

    private void ScheduleHighQualityRefresh()
    {
        DisposeInteractionSettleCts();

        var cts = new CancellationTokenSource();
        _interactionSettleCts = cts;
        var token = cts.Token;
        var settleDelay = Math.Clamp(HighQualitySettleDelayMs, 20, 1000);

        _ = Task.Run(
            async () =>
            {
                try
                {
                    await Task.Delay(settleDelay, token);
                    await Dispatcher.UIThread.InvokeAsync(
                        () =>
                        {
                            if (!token.IsCancellationRequested)
                                QueueRenderCurrentView(renderScale: 1f);
                        },
                        DispatcherPriority.Background
                    );
                }
                catch (OperationCanceledException)
                {
                    // Ignore cancellation: new interaction superseded this request.
                }
            },
            token
        );
    }

    private void DisposeInteractionSettleCts()
    {
        _interactionSettleCts?.Cancel();
        _interactionSettleCts?.Dispose();
        _interactionSettleCts = null;
    }

    private void DisposeSurfaceBitmap()
    {
        _surfaceBitmap?.Dispose();
        _surfaceBitmap = null;
    }

    private float GetPreviewRenderScale() => Math.Clamp(PreviewRenderScale, 0.05f, 1f);

    private bool ShouldUsePreviewForModelChange()
    {
        var now = DateTime.UtcNow;
        var shouldUsePreview = _hasPresentedFrame && now - _lastModelChangeUtc <= RapidModelUpdateThreshold;
        _lastModelChangeUtc = now;
        return shouldUsePreview;
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

        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
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

        _currentYaw += (float)(deltaX * YawDragSensitivity);
        _currentPitch += (float)(deltaY * PitchDragSensitivity);
        QueueRenderCurrentView(renderScale: GetPreviewRenderScale());
        ScheduleHighQualityRefresh();
    }

    private void ImageBorder_OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_isDragging)
            return;

        _isDragging = false;
        DisposeInteractionSettleCts();
        QueueRenderCurrentView(renderScale: 1f);
        e.Pointer.Capture(null);
    }

    private void ImageBorder_OnPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        _isDragging = false;
        DisposeInteractionSettleCts();
        QueueRenderCurrentView(renderScale: 1f);
    }

    private void ImageBorder_OnPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (!Interactive)
            return;

        if (Math.Abs(e.Delta.Y) < float.Epsilon)
            return;

        _currentZoom -= (float)e.Delta.Y * ZoomStep;
        _currentZoom = Math.Clamp(_currentZoom, MinZoom, MaxZoom);
        QueueRenderCurrentView(renderScale: GetPreviewRenderScale());
        ScheduleHighQualityRefresh();
    }

    private sealed record PendingRender(
        int Generation,
        Mii Mii,
        string StudioData,
        MiiImageSpecifications Specifications,
        CancellationTokenSource Cancellation
    );
}
