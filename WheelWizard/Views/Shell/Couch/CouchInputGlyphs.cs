using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace WheelWizard.Views.Shell.Couch;

internal enum CouchPrompt
{
    DPad,
    LeftStick,
    Confirm,
    Back,
    PreviousSection,
    NextSection,
    Menu,
    TextInput,
}

/// <summary>Kenney Input Prompts, Steam Deck monochrome set. Decoded once on the UI thread.</summary>
internal static class CouchInputGlyphs
{
    private static readonly Dictionary<CouchPrompt, Bitmap> Images = new(8);

    public static Image Create(CouchPrompt prompt, double size = 36)
    {
        if (!Images.TryGetValue(prompt, out var bitmap))
        {
            var name = prompt switch
            {
                CouchPrompt.DPad => "steamdeck_dpad",
                CouchPrompt.LeftStick => "steamdeck_stick_l",
                CouchPrompt.Confirm => "steamdeck_button_a_outline",
                CouchPrompt.Back => "steamdeck_button_b_outline",
                CouchPrompt.PreviousSection => "steamdeck_button_l1_outline",
                CouchPrompt.NextSection => "steamdeck_button_r1_outline",
                CouchPrompt.Menu => "steamdeck_button_options_outline",
                CouchPrompt.TextInput => "steamdeck_button_y_outline",
                _ => throw new ArgumentOutOfRangeException(nameof(prompt)),
            };
            using var stream = AssetLoader.Open(new Uri($"avares://WheelWizard/Resources/Images/ControllerPrompts/{name}.png"));
            bitmap = new Bitmap(stream);
            Images.Add(prompt, bitmap);
        }
        return new Image
        {
            Source = bitmap,
            Width = size,
            Height = size,
            Stretch = Stretch.Uniform,
            FlowDirection = FlowDirection.LeftToRight,
            // Subdue the upstream white artwork without altering the licensed assets.
            Opacity = 0.65,
        };
    }
}
