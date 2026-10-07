using System.Numerics;
using MiiAnim.Core.Animation;
using MiiAnim.Core.Evaluation;
using Silk.NET.OpenGL;
using SkiaSharp;
using Svg.Skia;

namespace WheelWizard.MiiRendering.Realtime;

/// <summary>
/// Draws <see cref="Particle"/>s as camera-facing sprites of the SVG images the animation carries
/// (<see cref="ParticleEffect.Shape"/>), multiplied with each particle's colour. Each image is parsed once and drawn
/// into a texture big enough for the largest sprite on screen (redrawn sharper when sprites grow), so edges stay
/// crisp at any size. Copied as is from the Mii Animator (MiiAnimator/Viewport/ParticleRenderer.cs).
/// Draw after opaque geometry with the depth buffer still bound.
/// </summary>
internal sealed unsafe class ParticleRenderer : IDisposable
{
    private const int FloatsPerVertex = 9; // view-space position (3), texture coordinate (2), colour (4)
    private const int MinTextureHeight = 32;
    private const int MaxTextureHeight = 1024;

    private readonly GL _gl;
    private readonly uint _program;
    private readonly uint _vao;
    private readonly uint _vbo;
    private readonly int _projectionLocation;
    private readonly int _textureLocation;
    private readonly Dictionary<ParticleShape, ShapeImage> _images = new();

    /// <summary>White pixel for effects without a shape (plain squares).</summary>
    private readonly ShapeImage _plain;

    private float[] _vertices = new float[6 * FloatsPerVertex * 64];

    /// <param name="shaderHeader">"#version 330 core" or "#version 300 es" (+ precision) line(s).</param>
    public ParticleRenderer(GL gl, string shaderHeader)
    {
        _gl = gl;
        _program = Link(shaderHeader + VertexSource, shaderHeader + FragmentSource);
        _projectionLocation = gl.GetUniformLocation(_program, "uProj");
        _textureLocation = gl.GetUniformLocation(_program, "uShape");
        _vao = gl.GenVertexArray();
        _vbo = gl.GenBuffer();
        gl.BindVertexArray(_vao);
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
        const uint stride = FloatsPerVertex * 4;
        gl.EnableVertexAttribArray(0);
        gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, stride, (void*)0);
        gl.EnableVertexAttribArray(1);
        gl.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, stride, (void*)12);
        gl.EnableVertexAttribArray(2);
        gl.VertexAttribPointer(2, 4, VertexAttribPointerType.Float, false, stride, (void*)20);
        gl.BindVertexArray(0);

        uint white = 0xFFFFFFFF;
        _plain = new ShapeImage(null) { Texture = CreateTexture(1, 1, &white), Height = int.MaxValue };
    }

    /// <param name="stageToWorld">Stage space → world (identity, or the character turn the Miis are drawn with).</param>
    public void Draw(IReadOnlyList<Particle> particles, Matrix4x4 stageToWorld, Matrix4x4 view, Matrix4x4 projection)
    {
        if (particles.Count == 0)
            return;

        var viewport = stackalloc int[4];
        _gl.GetInteger(GetPName.Viewport, viewport);
        var viewportHeight = Math.Max(1, viewport[3]);

        var toView = stageToWorld * view;
        var placed = new List<(Particle Particle, Vector3 Center, Vector3 Velocity, ShapeImage Image)>(particles.Count);
        foreach (var particle in particles)
        {
            if (particle.Size <= 0f || particle.Color.W <= 0.002f)
                continue;
            var image = ImageFor(particle.Effect.Shape);
            if (!image.CanDraw)
                continue;
            var center = Vector3.Transform(particle.Position, toView);
            placed.Add((particle, center, Vector3.TransformNormal(particle.Velocity, toView), image));

            // How tall the sprite is on screen, so its image can be drawn at least that big.
            var w = center.X * projection.M14 + center.Y * projection.M24 + center.Z * projection.M34 + projection.M44;
            var pixels = particle.Size * 0.5f * projection.M22 / MathF.Max(1e-4f, MathF.Abs(w)) * viewportHeight;
            image.Wanted = Math.Max(image.Wanted, pixels);
        }

        foreach (var image in placed.Select(p => p.Image).Distinct())
        {
            Sharpen(image);
            image.Wanted = 0;
        }

        var sprites = placed.Where(p => p.Image.Texture != 0).Select(p => new Sprite(p.Particle, p.Center, p.Velocity, p.Image)).ToList();

        // Normal sprites back to front, then glowing ones (order doesn't matter when adding light, so group by image).
        var normal = sprites.Where(s => !s.Particle.Effect.Additive).OrderBy(s => s.Center.Z).ToList();
        var additive = sprites.Where(s => s.Particle.Effect.Additive).OrderBy(s => s.Image.Texture).ToList();

        _gl.UseProgram(_program);
        var projectionMatrix = projection;
        _gl.UniformMatrix4(_projectionLocation, 1, false, (float*)&projectionMatrix);
        _gl.Uniform1(_textureLocation, 0);
        _gl.ActiveTexture(TextureUnit.Texture0);
        _gl.Enable(EnableCap.DepthTest);
        _gl.DepthMask(false);
        _gl.Disable(EnableCap.CullFace);
        _gl.Enable(EnableCap.Blend);
        _gl.BindVertexArray(_vao);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);

        // The shader outputs premultiplied colour.
        _gl.BlendFuncSeparate(BlendingFactor.One, BlendingFactor.OneMinusSrcAlpha, BlendingFactor.One, BlendingFactor.OneMinusSrcAlpha);
        DrawSprites(normal);
        _gl.BlendFuncSeparate(BlendingFactor.One, BlendingFactor.One, BlendingFactor.Zero, BlendingFactor.One);
        DrawSprites(additive);

        _gl.BlendFuncSeparate(
            BlendingFactor.SrcAlpha,
            BlendingFactor.OneMinusSrcAlpha,
            BlendingFactor.One,
            BlendingFactor.OneMinusSrcAlpha
        );
        _gl.DepthMask(true);
        _gl.BindVertexArray(0);
        _gl.BindTexture(TextureTarget.Texture2D, 0);
    }

    /// <summary>Draws runs of sprites that share an image with one call each, keeping their order.</summary>
    private void DrawSprites(List<Sprite> sprites)
    {
        var start = 0;
        for (var i = 1; i <= sprites.Count; i++)
        {
            if (i < sprites.Count && sprites[i].Image == sprites[start].Image)
                continue;
            DrawBatch(sprites, start, i - start);
            start = i;
        }
    }

    private void DrawBatch(List<Sprite> sprites, int start, int count)
    {
        if (count == 0)
            return;
        var needed = count * 6 * FloatsPerVertex;
        if (_vertices.Length < needed)
            _vertices = new float[needed * 2];

        var o = 0;
        for (var s = start; s < start + count; s++)
        {
            var (particle, center, velocity, image) = sprites[s];
            var effect = particle.Effect;
            var half = particle.Size * 0.5f;
            var screenVelocity = new Vector2(velocity.X, velocity.Y);

            // Which way the image's top points.
            Vector2 up;
            if (effect.Orientation == ParticleOrientation.Upright)
                up = Vector2.UnitY;
            else if (effect.Orientation == ParticleOrientation.AlongVelocity && screenVelocity.LengthSquared() > 1e-4f)
                up = Vector2.Normalize(screenVelocity);
            else
            {
                var angle = particle.Rotation * MathF.PI / 180f;
                up = new Vector2(-MathF.Sin(angle), MathF.Cos(angle));
            }

            var right = new Vector2(up.Y, -up.X);
            var halfWidth = half * image.Aspect * MathF.Max(0f, effect.Aspect);
            // Stretched along its height by the distance it travels in Stretch seconds, like a motion blur.
            var halfHeight = half + screenVelocity.Length() * MathF.Max(0f, effect.Stretch);
            void Corner(float x, float y)
            {
                var offset = right * (x * halfWidth) + up * (y * halfHeight);
                _vertices[o++] = center.X + offset.X;
                _vertices[o++] = center.Y + offset.Y;
                _vertices[o++] = center.Z;
                _vertices[o++] = x * 0.5f + 0.5f;
                _vertices[o++] = 0.5f - y * 0.5f; // the image's first row is its top
                _vertices[o++] = particle.Color.X;
                _vertices[o++] = particle.Color.Y;
                _vertices[o++] = particle.Color.Z;
                _vertices[o++] = particle.Color.W;
            }

            Corner(-1, -1);
            Corner(1, -1);
            Corner(1, 1);
            Corner(-1, -1);
            Corner(1, 1);
            Corner(-1, 1);
        }

        _gl.BindTexture(TextureTarget.Texture2D, sprites[start].Image.Texture);
        fixed (float* p = _vertices)
            _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(o * 4), p, BufferUsageARB.StreamDraw);
        _gl.DrawArrays(PrimitiveType.Triangles, 0, (uint)(o / FloatsPerVertex));
    }

    private ShapeImage ImageFor(ParticleShape? shape)
    {
        if (shape is null)
            return _plain;
        if (!_images.TryGetValue(shape, out var image))
            _images[shape] = image = new ShapeImage(Parse(shape));
        return image;
    }

    /// <summary>Parses the SVG once. Null when it can't be drawn (those particles are skipped).</summary>
    private static SKSvg? Parse(ParticleShape shape)
    {
        var svg = new SKSvg();
        try
        {
            if (svg.FromSvg(shape.Svg) is { CullRect: { Width: > 0, Height: > 0 } })
                return svg;
        }
        catch (Exception)
        {
            // An SVG the library can't handle is drawn as nothing rather than breaking the animation.
        }

        svg.Dispose();
        return null;
    }

    /// <summary>(Re)draws the image into a texture when sprites show up bigger than its current one.</summary>
    private void Sharpen(ShapeImage image)
    {
        if (image.Svg?.Picture is not { } picture)
            return;
        var height = MinTextureHeight;
        while (height < image.Wanted * 1.25f && height < MaxTextureHeight)
            height *= 2;
        if (height <= image.Height)
            return;

        var bounds = picture.CullRect;
        var width = Math.Clamp((int)MathF.Round(height * image.Aspect), 1, MaxTextureHeight * 2);
        // Premultiplied, so filtering doesn't darken soft edges.
        var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var bitmap = new SKBitmap(info);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.Transparent);
            canvas.Scale(width / bounds.Width, height / bounds.Height);
            canvas.Translate(-bounds.Left, -bounds.Top);
            canvas.DrawPicture(picture);
        }

        if (image.Texture != 0)
            _gl.DeleteTexture(image.Texture);
        image.Texture = CreateTexture(width, height, (void*)bitmap.GetPixels());
        image.Height = height;
    }

    private uint CreateTexture(int width, int height, void* rgba)
    {
        var texture = _gl.GenTexture();
        _gl.BindTexture(TextureTarget.Texture2D, texture);
        _gl.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
        _gl.TexImage2D(
            TextureTarget.Texture2D,
            0,
            InternalFormat.Rgba8,
            (uint)width,
            (uint)height,
            0,
            PixelFormat.Rgba,
            PixelType.UnsignedByte,
            rgba
        );
        _gl.GenerateMipmap(TextureTarget.Texture2D);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.LinearMipmapLinear);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
        _gl.BindTexture(TextureTarget.Texture2D, 0);
        return texture;
    }

    private uint Link(string vertexSource, string fragmentSource)
    {
        uint Compile(ShaderType type, string source)
        {
            var shader = _gl.CreateShader(type);
            _gl.ShaderSource(shader, source);
            _gl.CompileShader(shader);
            _gl.GetShader(shader, ShaderParameterName.CompileStatus, out var ok);
            if (ok == 0)
                throw new InvalidOperationException($"Particle {type} failed: {_gl.GetShaderInfoLog(shader)}");
            return shader;
        }

        var vertex = Compile(ShaderType.VertexShader, vertexSource);
        var fragment = Compile(ShaderType.FragmentShader, fragmentSource);
        var program = _gl.CreateProgram();
        _gl.AttachShader(program, vertex);
        _gl.AttachShader(program, fragment);
        _gl.LinkProgram(program);
        _gl.GetProgram(program, ProgramPropertyARB.LinkStatus, out var linked);
        _gl.DeleteShader(vertex);
        _gl.DeleteShader(fragment);
        if (linked == 0)
            throw new InvalidOperationException($"Particle shader link failed: {_gl.GetProgramInfoLog(program)}");
        return program;
    }

    public void Dispose()
    {
        foreach (var image in _images.Values.Append(_plain))
        {
            if (image.Texture != 0)
                _gl.DeleteTexture(image.Texture);
            image.Svg?.Dispose();
        }

        _images.Clear();
        _gl.DeleteVertexArray(_vao);
        _gl.DeleteBuffer(_vbo);
        _gl.DeleteProgram(_program);
    }

    /// <summary>A shape's parsed SVG and the texture it's currently drawn into.</summary>
    private sealed class ShapeImage(SKSvg? svg)
    {
        public SKSvg? Svg { get; } = svg;

        /// <summary>Width / height of the drawing.</summary>
        public float Aspect { get; } = svg?.Picture is { } picture ? picture.CullRect.Width / picture.CullRect.Height : 1f;

        public bool CanDraw => Svg is not null || Texture != 0;

        public uint Texture { get; set; }

        /// <summary>Pixel height of <see cref="Texture"/>.</summary>
        public int Height { get; set; }

        /// <summary>Tallest on-screen sprite this frame, in pixels.</summary>
        public float Wanted { get; set; }
    }

    private readonly record struct Sprite(Particle Particle, Vector3 Center, Vector3 Velocity, ShapeImage Image);

    private const string VertexSource = """
        layout(location = 0) in vec3 aPos;
        layout(location = 1) in vec2 aUv;
        layout(location = 2) in vec4 aColor;
        uniform mat4 uProj;
        out vec2 vUv;
        out vec4 vColor;
        void main() {
            vUv = aUv;
            vColor = aColor;
            gl_Position = uProj * vec4(aPos, 1.0);
        }
        """;

    // The texture is premultiplied; the result is too (see the blend modes in Draw).
    private const string FragmentSource = """
        in vec2 vUv;
        in vec4 vColor;
        uniform sampler2D uShape;
        out vec4 fragColor;
        void main() {
            vec4 image = texture(uShape, vUv);
            float alpha = image.a * vColor.a;
            if (alpha <= 0.003) discard;
            fragColor = vec4(image.rgb * vColor.rgb * vColor.a, alpha);
        }
        """;
}
