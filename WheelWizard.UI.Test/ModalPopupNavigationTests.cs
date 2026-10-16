using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WheelWizard.Views.Components;
using WheelWizard.Views.Dialogs.Base;
using WheelWizard.Views.Shell;

namespace WheelWizard.UI.Test;

public class ModalPopupNavigationTests
{
    [AvaloniaFact]
    public void SearchableDropdown_KeepsPopupFocusAndConsumesEscapeBeforeTheModal()
    {
        var owner = new PopupTestWindow();
        owner.Resources[WindowAppearance.ControlAnimationDurationResourceKey] = TimeSpan.Zero;
        var dropdown = new Dropdown
        {
            Width = 240,
            Search = true,
            ItemsSource = new[] { "First", "Second" },
            SelectedIndex = 0,
        };
        var modal = new TestModal { Content = dropdown };
        try
        {
            owner.Show();
            modal.SetModalOwner(owner).Show();
            Pump(owner);
            dropdown.Focus();
            dropdown.IsDropDownOpen = true;
            Pump(owner);
            var popup = Assert.Single(owner.OpenedPopups);
            var search = popup.Child!.GetVisualDescendants().OfType<TextBox>().First();
            search.Focus(NavigationMethod.Directional);
            Assert.Same(search, owner.FocusManager!.GetFocusedElement());
            Press(owner, Key.Down);
            Assert.IsType<DropdownItem>(owner.FocusManager.GetFocusedElement());
            Press(owner, Key.Escape);
            Assert.False(dropdown.IsDropDownOpen);
            Assert.Same(modal, owner.Modals.TopModal);
            Assert.Same(dropdown, owner.FocusManager.GetFocusedElement());
            Press(owner, Key.Escape);
            Assert.False(owner.Modals.HasModals);
        }
        finally
        {
            owner.Close();
        }
    }

    [AvaloniaFact]
    public void Back_ClosesTheInnermostMenuWithoutDismissingItsModal()
    {
        var owner = new PopupTestWindow();
        var nested = new MenuItem { Header = "Nested action" };
        var submenu = new MenuItem { Header = "More", Items = { nested } };
        var flyout = new MenuFlyout { Items = { submenu } };
        var action = new ActionButton { Text = "Actions", Flyout = flyout };
        var modal = new TestModal { Content = action };
        try
        {
            owner.Show();
            modal.SetModalOwner(owner).Show();
            Pump(owner);
            action.Focus();
            Press(owner, Key.Space);
            Pump(owner);
            Assert.True(flyout.IsOpen);
            submenu.Focus(NavigationMethod.Directional);
            Assert.Same(submenu, owner.FocusManager!.GetFocusedElement());
            submenu.IsSubMenuOpen = true;
            Pump(owner);
            nested.Focus(NavigationMethod.Directional);
            Assert.Same(nested, owner.FocusManager.GetFocusedElement());
            Assert.True(PopupNavigation.TryCloseTopPopup(owner, modal));
            Pump(owner);
            Assert.False(submenu.IsSubMenuOpen);
            Assert.True(flyout.IsOpen);
            Assert.Same(modal, owner.Modals.TopModal);
            Assert.True(PopupNavigation.TryCloseTopPopup(owner, modal));
            Pump(owner);
            Assert.False(flyout.IsOpen);
            Assert.Same(action, owner.FocusManager.GetFocusedElement());
            Assert.Same(modal, owner.Modals.TopModal);
        }
        finally
        {
            owner.Close();
        }
    }

    private static void Pump(Window owner)
    {
        owner.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        owner.UpdateLayout();
    }

    private static void Press(Window owner, Key key)
    {
        owner.KeyPress(key, RawInputModifiers.None, PhysicalKey.None, "");
        owner.KeyRelease(key, RawInputModifiers.None, PhysicalKey.None, "");
        Pump(owner);
    }

    private sealed class TestModal : ModalContent
    {
        public TestModal() => IsDismissible = true;
    }

    private sealed class PopupTestWindow : BaseWindow
    {
        private readonly Grid _frame = new();
        private readonly Grid _body = new();
        private readonly Border _overlay = new();
        protected override Control InteractionContent => _frame;
        protected override Control InteractionOverlay => _overlay;
        protected override Control ModalInteractionContent => _body;

        public PopupTestWindow()
        {
            Width = 1000;
            Height = 720;
            _body.Children.Add(new Button { Content = "Background" });
            _frame.Children.Add(_body);
            _frame.Children.Add(_overlay);
            Content = _frame;
        }
    }
}
