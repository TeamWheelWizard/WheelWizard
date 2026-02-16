namespace WheelWizard.WheelWizardData.Domain;

public class WhWzStatus
{
    /// <summary>
    /// The variant type (used for preset icon styles). Optional if Icon is provided.
    /// </summary>
    public WhWzStatusVariant? Variant { get; set; }

    /// <summary>
    /// Custom SVG path data for the icon. If provided, this takes precedence over Variant.
    /// </summary>
    public string? Icon { get; set; }

    /// <summary>
    /// Custom color for the icon (hex format like "#123456"). Used when Icon is provided.
    /// </summary>
    public string? Color { get; set; }

    public required string Message { get; set; }
}
