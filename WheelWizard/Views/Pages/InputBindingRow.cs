using System.ComponentModel;
using System.Runtime.CompilerServices;
using WheelWizard.Services.Input;

namespace WheelWizard.Views.Pages;

public sealed class InputBindingRow(MarioKartInputAction action, string title) : INotifyPropertyChanged
{
    private string _value = "Not set";
    private string _actionButtonText = "Change";

    public MarioKartInputAction Action { get; } = action;
    public string Title { get; } = title;

    public string Value
    {
        get => _value;
        set => SetField(ref _value, value);
    }

    public string ActionButtonText
    {
        get => _actionButtonText;
        set => SetField(ref _actionButtonText, value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return;

        field = value;
        PropertyChanged?.Invoke(this, new(propertyName));
    }
}
