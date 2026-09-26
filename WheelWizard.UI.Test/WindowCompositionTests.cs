using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using WheelWizard.Views;
using WheelWizard.Views.Popups.Base;

namespace WheelWizard.UI.Test;

public class WindowCompositionTests
{
    [AvaloniaFact]
    public void PopupScale_UsesApplicationResourcesWithoutAServiceProvider()
    {
        Application.Current!.Resources[WindowAppearance.ScaleResourceKey] = 1.5;
        var popup = new PopupWindow(true, false, false, "Scale test");
        try
        {
            popup.Show();
            popup.UpdateLayout();
            Assert.Equal(1.5, popup.RequestedWindowScale);
            Application.Current.Resources[WindowAppearance.ScaleResourceKey] = 1.25;
            Assert.Equal(1.25, popup.RequestedWindowScale);
        }
        finally
        {
            popup.Close();
            Application.Current.Resources.Remove(WindowAppearance.ScaleResourceKey);
        }
    }

    [AvaloniaFact]
    public void OwnedPopups_DisableOnlyTheirWindowGroup_AndRestoreInteraction()
    {
        var first = new TestWindow();
        var second = new TestWindow();
        var popup = new TestWindow();
        var nested = new TestWindow();
        try
        {
            first.Show();
            second.Show();
            Assert.True(first.CanInteract);
            Assert.True(second.CanInteract);
            popup.Show(first);
            Assert.False(first.CanInteract);
            Assert.True(second.CanInteract);
            nested.Show(popup);
            Assert.False(popup.CanInteract);
            nested.Close();
            Assert.True(popup.CanInteract);
            Assert.False(first.CanInteract);
            popup.Close();
            Assert.True(first.CanInteract);
        }
        finally
        {
            nested.Close();
            popup.Close();
            first.Close();
            second.Close();
        }
    }

    [AvaloniaFact]
    public void ClosingOwner_ClosesItsNonmodalPopups()
    {
        var owner = new TestWindow();
        var popup = new TestWindow(allowParentInteraction: true);
        owner.Show();
        popup.Show(owner);
        Assert.True(owner.CanInteract);
        owner.Close();
        Assert.False(popup.IsVisible);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void PopupShow_PreservesOwnershipAndClosesWithItsOwner(bool allowParentInteraction)
    {
        var owner = new TestWindow();
        owner.Show();
        var popup = new OwnedPopup(owner, allowParentInteraction);
        try
        {
            popup.Show();
            Assert.Same(owner, popup.Owner);
            Assert.Equal(allowParentInteraction, owner.CanInteract);
            owner.Close();
            Assert.False(popup.IsVisible);
        }
        finally
        {
            popup.Close();
            owner.Close();
        }
    }

    private sealed class OwnedPopup : PopupWindow
    {
        public OwnedPopup(Window owner, bool allowParentInteraction)
            : base(true, allowParentInteraction, false, "Owned popup")
        {
            Owner = owner;
        }
    }

    private sealed class TestWindow : BaseWindow
    {
        private readonly Border _overlay = new();
        private readonly Border _content = new();
        protected override Control InteractionOverlay => _overlay;
        protected override Control InteractionContent => _content;
        public bool CanInteract => _content.IsEnabled;

        public TestWindow(bool allowParentInteraction = false)
        {
            AllowParentInteraction = allowParentInteraction;
            Content = _content;
        }
    }
}
