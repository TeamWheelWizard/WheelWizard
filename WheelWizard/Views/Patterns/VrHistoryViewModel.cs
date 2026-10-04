using System.ComponentModel;
using WheelWizard.RrRooms;
using WheelWizard.Shared.Services;

namespace WheelWizard.Views.Patterns;

public sealed class VrHistoryViewModel(IApiCaller<IRwfcApi> apiCaller) : INotifyPropertyChanged, IDisposable
{
    private CancellationTokenSource? _pending;
    private int _generation;
    private bool _disposed;

    public bool IsLoading { get; private set; }
    public bool HasFriendCode { get; private set; }
    public int HistoryDays { get; private set; } = 30;
    public RwfcPlayerVrHistoryResponse? History { get; private set; }
    public OperationError? Error { get; private set; }
    public event PropertyChangedEventHandler? PropertyChanged;

    public async Task LoadAsync(string? friendCode, int days)
    {
        if (_disposed)
            return;

        CancelPending();
        var generation = _generation;
        friendCode = NormalizeFriendCode(friendCode);
        HasFriendCode = !IsMissingFriendCode(friendCode);
        HistoryDays = days;
        Error = null;
        if (!HasFriendCode)
        {
            History = null;
            NotifyChanged();
            return;
        }

        using var pending = new CancellationTokenSource();
        _pending = pending;
        IsLoading = true;
        NotifyChanged();
        try
        {
            var result = await apiCaller.CallApiAsync(api => api.GetPlayerVrHistoryAsync(friendCode, days)).WaitAsync(pending.Token);
            if (generation != _generation)
                return;

            History = result.IsSuccess ? result.Value : null;
            Error = result.Error;
        }
        catch (OperationCanceledException) when (pending.IsCancellationRequested) { }
        catch (Exception exception)
        {
            if (generation != _generation)
                return;
            History = null;
            Error = new OperationError { Message = exception.Message, Exception = exception };
        }
        finally
        {
            if (generation == _generation)
            {
                _pending = null;
                IsLoading = false;
                NotifyChanged();
            }
        }
    }

    public void CancelPending()
    {
        ++_generation;
        _pending?.Cancel();
        _pending = null;
        IsLoading = false;
    }

    public void Dispose()
    {
        _disposed = true;
        CancelPending();
    }

    private void NotifyChanged() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));

    private static string NormalizeFriendCode(string? friendCode)
    {
        var trimmed = friendCode?.Trim() ?? string.Empty;
        var digits = new string(trimmed.Where(char.IsDigit).ToArray());
        return digits.Length == 12 ? $"{digits[..4]}-{digits.Substring(4, 4)}-{digits.Substring(8, 4)}" : trimmed;
    }

    private static bool IsMissingFriendCode(string friendCode)
    {
        var digits = new string(friendCode.Where(char.IsDigit).ToArray());
        return digits.Length == 0 || digits.All(digit => digit == '0');
    }
}
