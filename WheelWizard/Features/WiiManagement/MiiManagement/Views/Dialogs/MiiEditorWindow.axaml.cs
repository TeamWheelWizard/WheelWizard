using System.ComponentModel;
using Avalonia.Threading;
using Testably.Abstractions;
using WheelWizard.MiiAnimations.Editor;
using WheelWizard.MiiAnimations.Library;
using WheelWizard.MiiRendering.Services;
using WheelWizard.Shared.Calendar;
using WheelWizard.Shared.MessageTranslations;
using WheelWizard.Views.Dialogs.Base;
using WheelWizard.Views.Shell;
using WheelWizard.Views.Shell.Navigation;
using WheelWizard.WiiManagement;
using WheelWizard.WiiManagement.MiiManagement;
using WheelWizard.WiiManagement.MiiManagement.Domain.Mii;
using WheelWizard.WiiManagement.MiiManagement.Views.Dialogs.MiiEditor;

namespace WheelWizard.WiiManagement.MiiManagement.Views.Dialogs;

public partial class MiiEditorWindow : PopupContent, INotifyPropertyChanged
{
    /// <summary>Close anyway if the save animation never reaches its "saved" marker.</summary>
    private static readonly TimeSpan SaveAnimationTimeout = TimeSpan.FromSeconds(4);

    private IPageFactory Pages { get; }

    // whether you want to save the Mii
    public bool Result { get; private set; } = false;
    private TaskCompletionSource<bool>? _tcs;

    private readonly MiiEditorStage _stage;
    private MiiEditorReaction? _pageReaction;
    private bool _saving;

    private Mii _mii = null!;
    public Mii Mii
    {
        get => _mii;
        private set
        {
            if (_mii != value)
            {
                _mii = value;
                OnPropertyChanged(nameof(Mii));
            }
        }
    }

    public MiiEditorWindow(
        IPageFactory pages,
        IMiiNativeRenderer renderer,
        IMiiAnimationLibrary animations,
        ISeasonalCalendar calendar,
        IRandomSystem random
    )
        : base(true, false, false, t("popup_title.mii_editor"))
    {
        Pages = pages;
        InitializeComponent();
        DataContext = this;

        _stage = new MiiEditorStage(renderer, animations, calendar, random.Random.Shared);
        _stage.Director.Cue += OnCue;
        _stage.Director.ReactionEnded += reaction =>
        {
            if (reaction == MiiEditorReaction.Save)
                FinishSave();
        };
        StageHost.Child = _stage;
    }

    protected override void BeforeOpen()
    {
        base.BeforeOpen();
        SetEditorPage(typeof(EditorStartPage));
        _stage.Present(Mii);
        _stage.Start();
    }

    public void SetEditorPage(Type pageType)
    {
        EditorPresenter.Content = Pages.Create(pageType, this);
        Window.WindowTitle = $"{t("popup_title.mii_editor")} - {Mii.Name}";

        // The face menus zoom in on the head, everything else shows the whole Mii.
        _pageReaction = FaceMenuReaction(pageType);
        _stage.SetFocus(_pageReaction is null ? MiiEditorFocus.Body : MiiEditorFocus.Face);
    }

    private static MiiEditorReaction? FaceMenuReaction(Type pageType) =>
        pageType.Name switch
        {
            nameof(EditorFace) => MiiEditorReaction.Face,
            nameof(EditorHair) => MiiEditorReaction.Hair,
            nameof(EditorEyebrows) => MiiEditorReaction.Eyebrows,
            nameof(EditorEyes) => MiiEditorReaction.Eyes,
            nameof(EditorNose) => MiiEditorReaction.Nose,
            nameof(EditorLips) => MiiEditorReaction.Mouth,
            nameof(EditorGlasses) => MiiEditorReaction.Glasses,
            nameof(EditorFacialHair) => MiiEditorReaction.FacialHair,
            nameof(EditorMole) => MiiEditorReaction.Mole,
            _ => null,
        };

    public MiiEditorWindow SetMii(Mii miiToEdit)
    {
        Window.WindowTitle = $"{t("popup_title.mii_editor")} - {miiToEdit.Name}";
        var miiResult = miiToEdit.Clone();
        if (miiResult.IsFailure)
        {
            DisableOpen(true);
            MessageTranslationHelper.ShowMessage(
                MessageTranslation.Error_MiiEditor_CantOpenEditor,
                null,
                new { error = miiResult.Error.Message }
            );
            return this;
        }

        Mii = miiResult.Value;
        return this;
    }

    /// <summary>Plays the save animation, then closes and returns the Mii. Saving again skips the animation.</summary>
    public void SignalSaveMii()
    {
        Result = true;
        if (_saving || !_stage.IsAnimated || !_stage.Director.React(MiiEditorReaction.Save))
        {
            FinishSave();
            return;
        }

        _saving = true;
        // No more edits while the Mii takes its bow.
        EditorPresenter.IsEnabled = false;
        DispatcherTimer.RunOnce(FinishSave, SaveAnimationTimeout);
    }

    private void FinishSave()
    {
        if (_tcs?.Task.IsCompleted == true || !Result)
            return;
        _tcs?.TrySetResult(true);
        Close();
    }

    private void OnCue(MiiEditorReaction reaction, string cue)
    {
        if (reaction == MiiEditorReaction.Save && cue == MiiEditorCues.Saved)
            FinishSave();
    }

    protected override void BeforeClose()
    {
        // If you want to return something different, then to the TrySetResult before you close it
        _tcs?.TrySetResult(false);
    }

    public async Task<bool> AwaitAnswer()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            return await Dispatcher.UIThread.InvokeAsync(() => AwaitAnswer());
        }
        _tcs = new();
        Show(); // Or ShowDialog(parentWindow) if you need it to be modal
        return await _tcs.Task;
    }

    /// <summary>Shows the edited Mii. In a face menu the Mii also reacts to the change.</summary>
    public void RefreshImage() => _stage.Present(Mii, _pageReaction);

    /// <summary>
    /// Shows the edited Mii with a reaction. With <paramref name="showAtCue"/> the change shows when the reaction
    /// reaches that marker (e.g. <see cref="MiiEditorCues.SwapGender"/>).
    /// </summary>
    public void RefreshImage(MiiEditorReaction reaction, string? showAtCue = null) => _stage.Present(Mii, reaction, showAtCue);

    /// <summary>Lets the Mii react to something that doesn't change how it looks (e.g. typing its name).</summary>
    public void React(MiiEditorReaction reaction) => _stage.Director.React(reaction);

    #region PropertyChanged

    public new event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new(propertyName));
    }

    #endregion
}
