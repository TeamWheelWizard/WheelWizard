using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NSubstitute;
using WheelWizard.CustomDistributions;
using WheelWizard.CustomDistributions.Presentation;
using WheelWizard.Shared;
using WheelWizard.Views.Components;
using WheelWizard.Views.Dialogs;
using WheelWizard.Views.Dialogs.Base;
using WheelWizard.Views.Shell;
using WheelWizard.WiiManagement.MiiManagement.Domain.Mii;
using WheelWizard.WiiManagement.MiiManagement.Views;
using WheelWizard.WiiManagement.MiiManagement.Views.Dialogs;

namespace WheelWizard.UI.Test;

public class InAppModalTests
{
    [AvaloniaTheory]
    [InlineData("backdrop")]
    [InlineData("escape")]
    [InlineData("close")]
    public async Task Dismissal_UsesCancelOutcomeInsideSameWindow(string gesture)
    {
        using var owner = new ModalTestWindow();
        owner.Show();
        var nativeWindows = 0;
        using var opened = Window.WindowOpenedEvent.AddClassHandler<Window>((_, _) => nativeWindows++);
        var modal = new TextInputWindow().SetMainText("Name").SetInitialText("Keep me");
        var answer = modal.ShowDialog();
        Layout(owner);
        Assert.Same(owner, TopLevel.GetTopLevel(modal));
        Assert.Equal(0, nativeWindows);
        Assert.False(owner.CanInteract);
        Assert.IsType<InputField>(owner.FocusManager!.GetFocusedElement());
        Dismiss(owner, gesture);
        Assert.Null(await answer.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.True(owner.CanInteract);
        Assert.False(owner.Modals.HasModals);
    }

    [AvaloniaTheory]
    [InlineData("backdrop")]
    [InlineData("escape")]
    public async Task NondismissibleModal_RequiresExplicitAction(string gesture)
    {
        using var owner = new ModalTestWindow();
        owner.Show();
        var modal = new TestModal();
        var completion = modal.ShowDialog();
        Layout(owner);
        Dismiss(owner, gesture);
        Assert.Same(modal, owner.Modals.TopModal);
        Assert.False(completion.IsCompleted);
        modal.Close();
        await completion.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(1, modal.ClosedCount);
        Assert.True(owner.CanInteract);
    }

    [AvaloniaFact]
    public async Task NestedModal_RestoresInputFocusAndKeepsUnderlyingModalBlocked()
    {
        using var owner = new ModalTestWindow();
        owner.Show();
        owner.BackgroundButton.Focus();
        var first = new TextInputWindow().SetInitialText("Unsaved input");
        var second = new MessageBoxWindow().SetTitleText("Nested prompt");
        var firstClosed = first.ShowDialog();
        Layout(owner);
        var input = first.FindControl<InputField>("InputField")!;
        input.Focus();
        var secondClosed = second.ShowDialog();
        Layout(owner);
        Assert.False(first.IsEffectivelyEnabled);
        Assert.Same(second, owner.Modals.TopModal);
        for (var i = 0; i < 8; i++)
        {
            owner.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
            var focus = Assert.IsAssignableFrom<Control>(owner.FocusManager!.GetFocusedElement());
            Assert.Contains(owner.Modals, focus.GetVisualAncestors());
            Assert.DoesNotContain(first, focus.GetVisualAncestors());
        }
        second.Close();
        await secondClosed.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Same(first, owner.Modals.TopModal);
        Assert.Same(input, owner.FocusManager!.GetFocusedElement());
        Assert.Equal("Unsaved input", input.Text);
        Assert.False(owner.CanInteract);
        first.Close();
        await firstClosed.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(owner.CanInteract);
        Assert.Same(owner.BackgroundButton, owner.FocusManager.GetFocusedElement());
    }

    [AvaloniaFact]
    public async Task ClosingWindow_CompletesAllModalTasksAndAllowsRehosting()
    {
        using var owner = new ModalTestWindow();
        owner.Show();
        var first = new TestModal();
        var second = new TestModal();
        var firstTask = first.ShowDialog();
        var secondTask = second.ShowDialog();
        owner.Close();
        await Task.WhenAll(firstTask, secondTask).WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(1, first.ClosedCount);
        Assert.Equal(1, second.ClosedCount);
        Assert.False(owner.Modals.HasModals);
        using var next = new ModalTestWindow();
        next.Show();
        first.SetModalOwner(next);
        first.Show();
        Assert.Same(next, TopLevel.GetTopLevel(first));
    }

    [AvaloniaFact]
    public async Task ExplicitOwner_KeepsPromptInOriginatingWindow()
    {
        using var origin = new ModalTestWindow();
        origin.Show();
        using var other = new ModalTestWindow();
        other.Show();
        var prompt = new MessageBoxWindow().SetTitleText("Question");
        prompt.SetModalOwner(origin);
        var closed = prompt.ShowDialog();
        Assert.Same(origin, TopLevel.GetTopLevel(prompt));
        Assert.False(other.Modals.HasModals);
        prompt.Close();
        await closed.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(origin.CanInteract);
    }

    [AvaloniaFact]
    public async Task ExplicitOwner_KeepsNestedModPromptInsideTheMainWindow()
    {
        var main = new ModalTestWindow();
        main.Show();
        var mod = new TestModal();
        mod.SetModalOwner(main);
        var modClosed = mod.ShowDialog();
        try
        {
            Layout(main);
            var size = main.ClientSize;
            var prompt = new MessageBoxWindow().SetTitleText("Mod question");
            prompt.SetModalOwner(mod);
            var closed = prompt.ShowDialog();
            Layout(main);
            Assert.Same(main, TopLevel.GetTopLevel(mod));
            Assert.Same(main, TopLevel.GetTopLevel(prompt));
            Assert.Same(prompt, main.Modals.TopModal);
            Assert.Equal(size, main.ClientSize);
            prompt.Close();
            await closed.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.False(main.CanInteract);
            mod.Close();
            await modClosed.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.True(main.CanInteract);
        }
        finally
        {
            mod.Close();
            main.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(480, 420)]
    [InlineData(656, 876)]
    public void TallTextModal_ScrollsBodyAndKeepsActionsInView(double width, double height)
    {
        using var owner = new ModalTestWindow { Width = width, Height = height };
        owner.Show();
        var modal = new YesNoWindow()
            .SetMainText("Long question")
            .SetExtraText(string.Join("\n", Enumerable.Repeat("More information", 100)));
        modal.Show();
        Layout(owner);
        var scroll = modal.GetVisualDescendants().OfType<ScrollViewer>().First();
        Assert.True(scroll.Extent.Height > scroll.Viewport.Height);
        foreach (var name in new[] { "YesButton", "NoButton" })
        {
            var button = modal.FindControl<ActionButton>(name)!;
            var position = button.TranslatePoint(default, owner)!.Value;
            Assert.True(button.Bounds.Height > 0);
            Assert.InRange(position.Y, 0, height - button.Bounds.Height);
            Assert.InRange(position.X, 0, width - button.Bounds.Width);
        }
    }

    [AvaloniaFact]
    public async Task ClosingTransition_KeepsWindowBlockedUntilItCompletes_AndShutdownFinishesImmediately()
    {
        var resources = Application.Current!.Resources;
        var key = WindowAppearance.ControlAnimationDurationResourceKey;
        var hadDuration = resources.TryGetValue(key, out var previous);
        resources[key] = TimeSpan.FromMilliseconds(120);
        using var owner = new ModalTestWindow();
        owner.Show();
        try
        {
            var modal = new TestModal();
            var closed = modal.ShowDialog();
            Layout(owner);
            var close = owner.Modals.GetVisualDescendants().OfType<ActionButton>().Single();
            Assert.Equal(ActionButtonVariant.Ghost, close.Variant);
            Assert.NotNull(close.FindAncestorOfType<ModalHeading>());
            modal.Close();
            Assert.False(closed.IsCompleted);
            Assert.False(owner.CanInteract);
            Assert.Same(modal, owner.Modals.TopModal);
            await closed.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.True(owner.CanInteract);
            Assert.Equal(1, modal.ClosedCount);
            var shutdownModal = new TestModal();
            var shutdownClosed = shutdownModal.ShowDialog();
            Layout(owner);
            shutdownModal.Close();
            owner.Close();
            Assert.True(shutdownClosed.IsCompleted);
            Assert.Equal(1, shutdownModal.ClosedCount);
        }
        finally
        {
            owner.Close();
            if (hadDuration)
                resources[key] = previous;
            else
                resources.Remove(key);
        }
    }

    [AvaloniaTheory]
    [InlineData("cancel", false)]
    [InlineData("close", false)]
    [InlineData("close", true)]
    public async Task DistributionProgress_ActualButtonsCancelWithoutEasyDismissal(string button, bool isUpdate)
    {
        using var owner = new ModalTestWindow { Height = 420 };
        owner.Show();
        var modal = new ProgressWindow("Retro Rewind");
        var distribution = Substitute.For<IDistribution>();
        var started = new TaskCompletionSource<DistributionOperation>();
        var finished = new TaskCompletionSource<OperationResult>();
        Task<OperationResult> Start(DistributionOperation operation)
        {
            operation.Report(
                new DistributionProgress(
                    Message: string.Join("\n", Enumerable.Repeat("Downloading Retro Rewind files...", 50)),
                    Goal: "Retro Rewind",
                    CanCancel: true
                )
            );
            started.SetResult(operation);
            return finished.Task;
        }
        distribution.InstallAsync(Arg.Any<DistributionOperation>()).Returns(call => Start(call.Arg<DistributionOperation>()));
        distribution.UpdateAsync(Arg.Any<DistributionOperation>()).Returns(call => Start(call.Arg<DistributionOperation>()));
        var closed = modal.ShowDialog();
        var running = isUpdate ? distribution.UpdateAsync(modal) : distribution.InstallAsync(modal);
        var operation = await started.Task;
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        Layout(owner);
        var close = owner
            .Modals.GetVisualDescendants()
            .OfType<ActionButton>()
            .Single(control => Avalonia.Automation.AutomationProperties.GetName(control) == "Close");
        Assert.True(close.IsVisible);
        Assert.True(modal.IsCancelable);
        Assert.False(modal.IsDismissible);
        Dismiss(owner, "backdrop");
        Dismiss(owner, "escape");
        Assert.False(operation.CancellationToken.IsCancellationRequested);
        var target = button == "close" ? close : modal.FindControl<ActionButton>("CancelButton")!;
        var point = target.TranslatePoint(new Point(target.Bounds.Width / 2, target.Bounds.Height / 2), owner)!.Value;
        owner.MouseDown(point, MouseButton.Left);
        owner.MouseUp(point, MouseButton.Left);
        Assert.True(operation.CancellationToken.IsCancellationRequested);
        Assert.True(modal.WasCancellationRequested);
        Assert.Same(modal, owner.Modals.TopModal);
        Assert.False(closed.IsCompleted);
        Assert.False(target.IsEnabled && target.IsVisible);
        finished.SetResult(new OperationError { Message = "Cancelled" });
        Assert.True((await running).IsSuccess);
        modal.Close();
        await closed.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void ColoredHeaders_ReachThePanelEdgesWithoutAnExtraTitleBar(bool progress)
    {
        using var owner = new ModalTestWindow();
        owner.Show();
        using var cancellation = new CancellationTokenSource();
        ModalContent modal = progress
            ? new ProgressWindow("Progress")
                .SetCancellationTokenSource(cancellation)
                .SetGoal(string.Join(" ", Enumerable.Repeat("Installing Retro Rewind", 10)))
            : new MessageBoxWindow().SetTitleText(string.Join(" ", Enumerable.Repeat("Success", 20))).SetInfoText("Completed");
        modal.Show();
        Layout(owner);
        var colored = modal.FindControl<Border>(progress ? "GoalBorder" : "TitleBorder")!;
        var close = owner
            .Modals.GetVisualDescendants()
            .OfType<ActionButton>()
            .Single(control => Avalonia.Automation.AutomationProperties.GetName(control) == "Close");
        var panel = colored.GetVisualAncestors().OfType<Border>().Single(border => border.Name == "ModalPanel");
        Assert.NotNull(close.FindAncestorOfType<ModalHeading>());
        var position = colored.TranslatePoint(default, panel)!.Value;
        Assert.Equal(panel.BorderThickness.Left, position.X, 4);
        Assert.Equal(panel.Bounds.Width - panel.BorderThickness.Left - panel.BorderThickness.Right, colored.Bounds.Width, 4);
        Assert.Equal(panel.BorderThickness.Top, position.Y, 4);
        Assert.True(colored.Bounds.Height > 0);
        var title = modal.FindControl<SelectableTextBlock>(progress ? "GoalTextBlock" : "MessageTitleBlock")!;
        var titlePosition = title.TranslatePoint(default, colored)!.Value;
        var closePosition = close.TranslatePoint(default, colored)!.Value;
        Assert.True(titlePosition.X + title.Bounds.Width <= closePosition.X);
    }

    [AvaloniaFact]
    public async Task WindowDragStrip_RemainsInteractiveWithoutDismissingOrEnablingThePage()
    {
        using var owner = new ModalTestWindow();
        owner.Show();
        var modal = new YesNoWindow().SetMainText("Leave without saving?");
        var closed = modal.ShowDialog();
        Layout(owner);
        Assert.False(owner.CanInteract);
        Assert.True(owner.DragHandle.IsEffectivelyEnabled);
        var chromeDimmer = owner.GetVisualDescendants().OfType<Border>().Single(border => border.Name == "ModalChromeDimmer");
        Assert.True(chromeDimmer.IsVisible);
        Assert.False(chromeDimmer.IsHitTestVisible);
        Assert.Equal(120d / 255 * owner.Modals.Children.OfType<Grid>().Single().Opacity, chromeDimmer.Opacity, 6);
        Assert.Equal(30, chromeDimmer.Bounds.Height);
        var point = owner.DragHandle.TranslatePoint(new Point(200, 15), owner)!.Value;
        owner.MouseDown(point, MouseButton.Left);
        owner.MouseUp(point, MouseButton.Left);
        Assert.Equal(1, owner.DragPresses);
        Assert.False(closed.IsCompleted);
        Assert.Same(modal, owner.Modals.TopModal);
        Assert.DoesNotContain(owner.Modals.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "Wheel Wizard");
        var title = modal.FindControl<SelectableTextBlock>("MainTextBlock")!;
        Assert.NotNull(title.FindAncestorOfType<ModalHeading>());
        Dismiss(owner, "close");
        await closed.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(owner.CanInteract);
        Assert.False(chromeDimmer.IsVisible);
    }

    [AvaloniaFact]
    public void RenameInput_UsesNoteAndRequiresAValidChange_WithCharactersAlignedLeft()
    {
        using var owner = new ModalTestWindow();
        owner.Show();
        var modal = new TextInputWindow()
            .SetMainText("Rename Mii")
            .SetNote("Use 3 to 10 characters.")
            .SetInitialText("Mario")
            .SetRequireChange()
            .SetValidation(
                (_, name) =>
                    name.Length is >= 3 and <= 10 ? OperationResult.Ok() : (OperationResult)new OperationError { Message = "Invalid name" }
            )
            .SetCustomCharacters(new[] { '★', 'é' });
        modal.Show();
        Layout(owner);
        var input = modal.FindControl<InputField>("InputField")!;
        var apply = modal.FindControl<ActionButton>("SubmitButton")!;
        var characters = modal.FindControl<ActionButton>("CustomCharsButton")!;
        Assert.Equal("Use 3 to 10 characters.", input.Note);
        Assert.False(modal.FindControl<SelectableTextBlock>("ExtraTextBlock")!.IsVisible);
        Assert.False(apply.IsEnabled);
        input.Text = "Luigi";
        Layout(owner);
        Assert.True(apply.IsEnabled);
        input.Text = "Mario";
        Layout(owner);
        Assert.False(apply.IsEnabled);
        input.Text = "A";
        Layout(owner);
        Assert.False(apply.IsEnabled);
        Assert.NotNull(input.ErrorText);
        input.Text = "Mario";
        Layout(owner);
        Assert.Equal(Avalonia.Layout.HorizontalAlignment.Left, characters.HorizontalAlignment);
        Assert.Equal(12, characters.Margin.Bottom);
        Assert.Equal(0, characters.TranslatePoint(default, modal)!.Value.X, 4);
        var point = characters.TranslatePoint(new Point(characters.Bounds.Width / 2, characters.Bounds.Height / 2), owner)!.Value;
        owner.MouseDown(point, MouseButton.Left);
        owner.MouseUp(point, MouseButton.Left);
        Layout(owner);
        var choices = modal.FindControl<Avalonia.Controls.Primitives.UniformGrid>("CustomChars")!;
        Assert.True(choices.IsVisible);
        choices.Children.OfType<ActionButton>().First().RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Layout(owner);
        Assert.Equal("Mario★", input.Text);
        Assert.True(apply.IsEnabled);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void SelectorAndOptionsTitles_BindToTheModalRatherThanTheSurroundingPage(bool selector)
    {
        using var owner = new ModalTestWindow { DataContext = new { ModalTitle = "Wrong page title" } };
        owner.Show();
        ModalContent modal = selector ? new MiiSelectorWindow() : new OptionsWindow().SetWindowTitle("Choose a Dolphin executable");
        modal.Show();
        Layout(owner);
        var heading = modal.GetVisualDescendants().OfType<ModalHeading>().Single();
        var title = Assert.IsType<TextBlock>(heading.Content);
        Assert.False(string.IsNullOrWhiteSpace(title.Text));
        Assert.Equal(modal.ModalTitle, title.Text);
        Assert.NotEqual("Wrong page title", title.Text);
    }

    [AvaloniaFact]
    public void ChromeDimming_FollowsAnimatedBackdropValues_AndHeaderControlsRemainUsable()
    {
        var resources = Application.Current!.Resources;
        var key = WindowAppearance.ControlAnimationDurationResourceKey;
        var hadDuration = resources.TryGetValue(key, out var previous);
        resources[key] = TimeSpan.Zero;
        using var owner = new ModalTestWindow();
        owner.Show();
        var first = new MessageBoxWindow().SetTitleText("First prompt");
        var second = new MessageBoxWindow().SetTitleText("Second prompt");
        try
        {
            first.Show();
            Layout(owner);
            var dimmer = owner.GetVisualDescendants().OfType<Border>().Single(border => border.Name == "ModalChromeDimmer");
            var firstLayer = owner.Modals.Children.OfType<Grid>().Single();
            foreach (var opacity in new[] { 0d, 0.25, 0.75, 1d, 0.75, 0.25, 0d })
            {
                firstLayer.SetValue(Visual.OpacityProperty, opacity, Avalonia.Data.BindingPriority.Animation);
                Assert.Equal(120d / 255 * opacity, dimmer.Opacity, 6);
            }
            firstLayer.SetValue(Visual.OpacityProperty, 1d, Avalonia.Data.BindingPriority.Animation);
            second.Show();
            Layout(owner);
            var secondLayer = owner.Modals.Children.OfType<Grid>().Last();
            secondLayer.SetValue(Visual.OpacityProperty, 0.5, Avalonia.Data.BindingPriority.Animation);
            Assert.Equal(1 - (1 - 120d / 255) * (1 - 60d / 255), dimmer.Opacity, 6);
            var point = owner.CaptionButton.TranslatePoint(new Point(20, 10), owner)!.Value;
            owner.MouseDown(point, MouseButton.Left);
            owner.MouseUp(point, MouseButton.Left);
            Assert.Equal(1, owner.CaptionClicks);
            Assert.Same(second, owner.Modals.TopModal);
            Assert.False(owner.CanInteract);
            var close = (WindowClosingEventArgs)
                Activator.CreateInstance(
                    typeof(WindowClosingEventArgs),
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
                    binder: null,
                    args: new object[] { WindowCloseReason.WindowClosing, false },
                    culture: null
                )!;
            owner.RequestNativeClose(close);
            Assert.False(close.Cancel);
        }
        finally
        {
            owner.Close();
            if (hadDuration)
                resources[key] = previous;
            else
                resources.Remove(key);
        }
    }

    [AvaloniaFact]
    public void MiiSelector_SaveRequiresADifferentSelection_AndReturningToOriginalDisablesIt()
    {
        var original = new Mii { MiiId = 1, Name = new MiiName("Mario") };
        var replacement = new Mii { MiiId = 2, Name = new MiiName("Luigi") };
        var modal = new MiiSelectorWindow().SetMiiOptions(new List<Mii> { original, replacement }, original, "00:00:00:00:00:00");
        var save = modal.FindControl<ActionButton>("SaveButton")!;
        var choices = modal.FindControl<WrapPanel>("MiiList")!.Children.OfType<MiiBlock>().ToList();
        Assert.False(save.IsEnabled);
        choices.Single(choice => choice.Mii == replacement).IsChecked = true;
        Assert.True(save.IsEnabled);
        choices.Single(choice => choice.Mii == original).IsChecked = true;
        Assert.False(save.IsEnabled);
        choices.ForEach(choice => choice.IsChecked = false);
        Assert.False(save.IsEnabled);
    }

    [AvaloniaTheory]
    [InlineData(0)]
    [InlineData(120)]
    public async Task ClosingModal_CompletesOwnedDescendantsOnce_AndLeavesSiblingOperationsOpen(int animationMilliseconds)
    {
        var owner = new ModalTestWindow();
        owner.Resources[WindowAppearance.ControlAnimationDurationResourceKey] = TimeSpan.FromMilliseconds(animationMilliseconds);
        owner.Show();
        try
        {
            var parent = new TestModal();
            var parentClosed = parent.ShowDialog();
            Layout(owner);
            var child = new TestModal();
            child.SetModalOwner(parent);
            var childClosed = child.ShowDialog();
            Layout(owner);
            var grandchild = new TestModal();
            grandchild.SetModalOwner(child);
            var grandchildClosed = grandchild.ShowDialog();
            var sibling = new TestModal();
            sibling.SetModalOwner(owner);
            var siblingClosed = sibling.ShowDialog();
            var closingCount = 0;
            parent.Closing += (_, _) => closingCount++;

            parent.Close();
            parent.Close();

            await Task.WhenAll(parentClosed, childClosed, grandchildClosed).WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal(1, closingCount);
            Assert.Equal(1, parent.ClosedCount);
            Assert.Equal(1, child.ClosedCount);
            Assert.Equal(1, grandchild.ClosedCount);
            Assert.Null(TopLevel.GetTopLevel(parent));
            Assert.Null(TopLevel.GetTopLevel(child));
            Assert.Null(TopLevel.GetTopLevel(grandchild));
            Assert.False(siblingClosed.IsCompleted);
            Assert.Same(sibling, owner.Modals.TopModal);
            Assert.False(owner.CanInteract);

            sibling.Close();
            await siblingClosed.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal(1, sibling.ClosedCount);
            Assert.False(owner.Modals.HasModals);
            Assert.True(owner.CanInteract);
        }
        finally
        {
            owner.Close();
        }
    }

    private static void Dismiss(Window owner, string gesture)
    {
        if (gesture == "escape")
            owner.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        else if (gesture == "backdrop")
        {
            owner.MouseDown(new Point(5, 50), MouseButton.Left);
            owner.MouseUp(new Point(5, 50), MouseButton.Left);
        }
        else
            owner
                .GetVisualDescendants()
                .OfType<ActionButton>()
                .Single(button => Avalonia.Automation.AutomationProperties.GetName(button) == "Close")
                .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
    }

    private static void Layout(Window owner)
    {
        owner.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        owner.UpdateLayout();
    }

    private sealed class TestModal : ModalContent
    {
        public int ClosedCount { get; private set; }

        public TestModal()
            : base("Required action")
        {
            Content = new StackPanel
            {
                Width = 300,
                Children =
                {
                    new ModalHeading { Content = new TextBlock { Text = "Required action" } },
                    new Button { Content = "Explicit action", Height = 120 },
                },
            };
        }

        protected override void BeforeClose() => ClosedCount++;
    }

    [AvaloniaFact]
    public async Task ClosingModSurface_CompletesItsNestedPromptWithoutClosingTheShell()
    {
        var owner = new ModalTestWindow();
        owner.Show();
        try
        {
            var surface = new TestModal();
            var surfaceClosed = surface.ShowDialog();
            var prompt = new YesNoWindow().SetMainText("Install this mod?");
            prompt.SetModalOwner(surface);
            var answer = prompt.AwaitAnswer();
            surface.Close();
            Assert.False(await answer.WaitAsync(TimeSpan.FromSeconds(2)));
            await surfaceClosed.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.True(owner.IsVisible);
            Assert.False(owner.Modals.HasModals);
            Assert.True(owner.CanInteract);
        }
        finally
        {
            owner.Close();
        }
    }

    [AvaloniaFact]
    public void ModalFocus_CannotEscapeToDirectionalChromeNavigation()
    {
        var owner = new ModalTestWindow { Width = 1280, Height = 720 };
        owner.Show();
        try
        {
            var modal = new TestModal();
            modal.Show();
            Layout(owner);
            owner.CaptionButton.Focus(NavigationMethod.Directional);
            var focused = Assert.IsAssignableFrom<Control>(owner.FocusManager!.GetFocusedElement());
            Assert.NotSame(owner.CaptionButton, focused);
            Assert.Contains(modal, focused.GetVisualAncestors());
        }
        finally
        {
            owner.Close();
        }
    }

    private sealed class ModalTestWindow : BaseWindow, IDisposable
    {
        public void Dispose() => Close();

        private readonly Grid _frame = new() { RowDefinitions = RowDefinitions.Parse("30,*") };
        private readonly Grid _body = new();
        public Border DragHandle { get; } = new() { Background = Avalonia.Media.Brushes.Gray };
        public int DragPresses { get; private set; }
        public Button CaptionButton { get; } =
            new()
            {
                Content = "Caption",
                Width = 40,
                Height = 20,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            };
        public int CaptionClicks { get; private set; }

        public void RequestNativeClose(WindowClosingEventArgs args) => base.OnClosing(args);

        private readonly Border _overlay = new();
        public Button BackgroundButton { get; } = new() { Content = "Background" };
        public bool CanInteract => _body.IsEnabled;
        protected override Control InteractionContent => _frame;
        protected override Control InteractionOverlay => _overlay;
        protected override Control ModalInteractionContent => _body;

        public ModalTestWindow()
        {
            Width = 656;
            Height = 876;
            ExtendClientAreaTitleBarHeightHint = 30;
            DragHandle.PointerPressed += (_, _) => DragPresses++;
            CaptionButton.Click += (_, _) => CaptionClicks++;
            DragHandle.Child = CaptionButton;
            _frame.Children.Add(DragHandle);
            _body.Children.Add(BackgroundButton);
            Grid.SetRow(_body, 1);
            _frame.Children.Add(_body);
            _frame.Children.Add(_overlay);
            Content = _frame;
        }
    }
}
