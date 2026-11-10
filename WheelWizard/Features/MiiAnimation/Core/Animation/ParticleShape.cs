using System.Xml;

namespace MiiAnim.Core.Animation;

/// <summary>
/// A particle sprite image (an SVG document), stored in the .miianim file of every animation that uses it, so players
/// draw whatever the file brings without knowing any shapes themselves. Players draw the SVG with their own library,
/// at the size the particle shows up, and multiply its pixels with the particle colour: a white image takes any
/// colour, grey shading is kept.
/// <para>
/// The image's top points up on screen (or along the motion, see <see cref="ParticleOrientation"/>), and its
/// width-to-height ratio is kept (a sprite's size is its height). Shapes with the same SVG are equal whatever their
/// name, so files and texture caches keep just one copy.
/// </para>
/// </summary>
public sealed class ParticleShape : IEquatable<ParticleShape>
{
    public const int MaxNameLength = 32;

    /// <summary>Particle images are small drawings; this keeps files (and the players reading them) safe from huge ones.</summary>
    public const int MaxSvgLength = 256 * 1024;

    public ParticleShape(string name, string svg)
    {
        if (!IsSvg(svg))
            throw new ArgumentException("Particle shapes must be SVG documents.", nameof(svg));
        name = name.Trim();
        Name = name.Length > MaxNameLength ? name[..MaxNameLength] : name;
        Svg = svg;
    }

    /// <summary>Label shown in editors; not part of the identity.</summary>
    public string Name { get; }

    /// <summary>The SVG document.</summary>
    public string Svg { get; }

    /// <summary>Whether <paramref name="text"/> is well-formed XML with an &lt;svg&gt; root (DTDs aren't allowed).</summary>
    public static bool IsSvg(string text)
    {
        if (text.Length is 0 or > MaxSvgLength)
            return false;
        try
        {
            using var reader = XmlReader.Create(
                new StringReader(text),
                new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null }
            );
            if (reader.MoveToContent() != XmlNodeType.Element || reader.LocalName != "svg")
                return false;
            while (reader.Read()) { }
            return true;
        }
        catch (XmlException)
        {
            return false;
        }
    }

    public bool Equals(ParticleShape? other) => other is not null && string.Equals(Svg, other.Svg, StringComparison.Ordinal);

    public override bool Equals(object? obj) => Equals(obj as ParticleShape);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Svg);

    public static bool operator ==(ParticleShape? left, ParticleShape? right) => left?.Equals(right) ?? right is null;

    public static bool operator !=(ParticleShape? left, ParticleShape? right) => !(left == right);

    public override string ToString() => Name;
}
