using System.Numerics;
using MiiAnim.Core.Evaluation;
using MiiAnim.Core.Rig;
using Silk.NET.OpenGL;
using WheelWizard.MiiRendering.Configuration;
using WheelWizard.MiiRendering.Services;

namespace WheelWizard.MiiRendering.Realtime;

/// <summary>One frame for <see cref="MiiGpuRenderer"/>: a posed Mii plus how the CPU renderer would frame it.</summary>
public sealed record MiiGpuFrame(MiiRealtimeFrameSetup Setup, MiiPose Pose, IReadOnlyList<HeadMeshData>? Head, string? HeadKey)
{
    /// <summary>Particles in stage space (see <see cref="MiiAnim.Core.Evaluation.MiiStage"/>), drawn over the Mii.</summary>
    public IReadOnlyList<Particle> Particles { get; init; } = [];
}

/// <summary>
/// OpenGL renderer for one Mii (skinned body + FFL head) with the same shading as the CPU renderer.
/// Clears to transparent so it can sit on top of any UI. All calls must happen on the GL thread.
/// </summary>
internal sealed unsafe class MiiGpuRenderer : IDisposable
{
    private readonly GL _gl;
    private readonly uint _headProgram;
    private readonly uint _bodyProgram;
    private readonly Dictionary<string, GpuMesh[]> _heads = new();
    private readonly Dictionary<MiiBodyModel, GpuMesh[]> _bodies = new();
    private readonly ParticleRenderer? _particles;

    private sealed record GpuMesh(uint Vao, uint Vbo, uint Ebo, int IndexCount, uint Texture, HeadMeshData? Head, bool IsPants);

    public MiiGpuRenderer(GL gl, bool isEs)
    {
        _gl = gl;
        var header = MiiShaders.Header(isEs);
        _headProgram = CreateProgram(header + MiiShaders.HeadVertex, header + MiiShaders.HeadFragment);
        _bodyProgram = CreateProgram(header + MiiShaders.BodyVertex, header + MiiShaders.BodyFragment);
        try
        {
            _particles = new ParticleRenderer(gl, header);
        }
        catch (InvalidOperationException)
        {
            // Particles are an extra: still draw the Mii when their shader doesn't compile on this driver.
            _particles = null;
        }
    }

    public void Render(MiiGpuFrame? frame, int width, int height)
    {
        _gl.Viewport(0, 0, (uint)width, (uint)height);
        _gl.ClearColor(0f, 0f, 0f, 0f);
        _gl.ClearDepth(1f);
        _gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
        if (frame is null)
            return;

        _gl.Enable(EnableCap.DepthTest);
        _gl.DepthFunc(DepthFunction.Lequal);
        _gl.DepthMask(true);
        _gl.Enable(EnableCap.Blend);
        // Premultiplied result in the target, which is what the compositor expects for a transparent layer.
        _gl.BlendFuncSeparate(
            BlendingFactor.SrcAlpha,
            BlendingFactor.OneMinusSrcAlpha,
            BlendingFactor.One,
            BlendingFactor.OneMinusSrcAlpha
        );
        _gl.FrontFace(FrontFaceDirection.Ccw);

        var setup = frame.Setup;
        if (frame.Pose.Visible)
        {
            if (setup.HasBody)
                DrawBody(frame, setup);
            if (frame.Head is not null && frame.HeadKey is not null)
                DrawHead(frame, setup);
        }

        if (frame.Particles.Count > 0)
            _particles?.Draw(frame.Particles, setup.StageToWorld, setup.View, setup.Projection);

        _gl.Disable(EnableCap.CullFace);
        _gl.BindVertexArray(0);
        _gl.UseProgram(0);
    }

    private void DrawBody(MiiGpuFrame frame, MiiRealtimeFrameSetup setup)
    {
        var meshes = GetBody(MiiBodyModel.Get(setup.Female));
        _gl.UseProgram(_bodyProgram);
        SetLighting(_bodyProgram);
        SetMatrix(_bodyProgram, "uView", setup.View);
        SetMatrix(_bodyProgram, "uProj", setup.Projection);
        var bodyMatrix = setup.BodyMatrixFor(frame.Pose);
        var bones = stackalloc float[16 * 16];
        for (var i = 0; i < MiiSkeletonInfo.BoneCount; i++)
        {
            var m = frame.Pose.SkinMatrix[i] * bodyMatrix;
            System.Runtime.CompilerServices.Unsafe.CopyBlock(bones + i * 16, &m, 64);
        }

        _gl.UniformMatrix4(_gl.GetUniformLocation(_bodyProgram, "uBones"), 16, false, bones);
        _gl.Uniform1(_gl.GetUniformLocation(_bodyProgram, "uHighlightMask"), 0);
        _gl.Uniform1(_gl.GetUniformLocation(_bodyProgram, "uHoverMask"), 0);
        SetVec4(_bodyProgram, "uTint", Vector4.Zero);
        _gl.Uniform1(_gl.GetUniformLocation(_bodyProgram, "uAlpha"), 1f);
        _gl.Enable(EnableCap.CullFace);
        _gl.CullFace(TriangleFace.Back);
        foreach (var mesh in meshes)
        {
            var material = NativeMiiRenderer.BodyMaterial(mesh.IsPants);
            SetMaterial(_bodyProgram, material.Ambient, material.Diffuse, material.Specular, material.Power, 0, material.Rim);
            SetVec4(_bodyProgram, "uColor", mesh.IsPants ? setup.PantsColor : setup.BodyColor);
            _gl.BindVertexArray(mesh.Vao);
            _gl.DrawElements(PrimitiveType.Triangles, (uint)mesh.IndexCount, DrawElementsType.UnsignedInt, null);
        }
    }

    private void DrawHead(MiiGpuFrame frame, MiiRealtimeFrameSetup setup)
    {
        var meshes = GetHead(frame.HeadKey!, frame.Head!);
        _gl.UseProgram(_headProgram);
        SetLighting(_headProgram);
        SetMatrix(_headProgram, "uView", setup.View);
        SetMatrix(_headProgram, "uProj", setup.Projection);
        SetMatrix(_headProgram, "uModel", setup.HeadMatrix(frame.Pose));
        SetVec4(_headProgram, "uTint", Vector4.Zero);
        _gl.Uniform1(_gl.GetUniformLocation(_headProgram, "uAlpha"), 1f);
        _gl.Uniform1(_gl.GetUniformLocation(_headProgram, "uTex"), 0);
        foreach (var mesh in meshes)
        {
            var data = mesh.Head!;
            if (data.CullMode == 1)
            {
                _gl.Enable(EnableCap.CullFace);
                _gl.CullFace(TriangleFace.Back);
            }
            else if (data.CullMode == 2)
            {
                _gl.Enable(EnableCap.CullFace);
                _gl.CullFace(TriangleFace.Front);
            }
            else
            {
                _gl.Disable(EnableCap.CullFace);
            }

            SetMaterial(
                _headProgram,
                data.Ambient,
                data.Diffuse,
                data.Specular,
                data.SpecularPower,
                data.HasTangent ? data.SpecularMode : 0,
                data.RimColor
            );
            _gl.Uniform1(_gl.GetUniformLocation(_headProgram, "uMode"), data.ModulateMode);
            SetVec4(_headProgram, "uColR", data.ColorR);
            SetVec4(_headProgram, "uColG", data.ColorG);
            SetVec4(_headProgram, "uColB", data.ColorB);
            _gl.Uniform1(_gl.GetUniformLocation(_headProgram, "uHasTangent"), data.HasTangent ? 1 : 0);
            _gl.Uniform1(_gl.GetUniformLocation(_headProgram, "uHasTex"), mesh.Texture != 0 ? 1 : 0);
            _gl.ActiveTexture(TextureUnit.Texture0);
            _gl.BindTexture(TextureTarget.Texture2D, mesh.Texture);
            _gl.BindVertexArray(mesh.Vao);
            _gl.DrawElements(PrimitiveType.Triangles, (uint)mesh.IndexCount, DrawElementsType.UnsignedInt, null);
        }

        _gl.BindTexture(TextureTarget.Texture2D, 0);
    }

    /// <summary>Frees uploaded heads that are no longer needed (e.g. after the Mii changed).</summary>
    public void RetainHeads(IReadOnlySet<string> keep)
    {
        foreach (var key in _heads.Keys.Where(k => !keep.Contains(k)).ToList())
        {
            foreach (var mesh in _heads[key])
                DeleteMesh(mesh);
            _heads.Remove(key);
        }
    }

    private GpuMesh[] GetBody(MiiBodyModel model)
    {
        if (_bodies.TryGetValue(model, out var existing))
            return existing;

        var meshes = new List<GpuMesh>();
        foreach (var mesh in model.Meshes)
        {
            var interleaved = new float[mesh.Positions.Length * 14];
            for (var v = 0; v < mesh.Positions.Length; v++)
            {
                var o = v * 14;
                interleaved[o] = mesh.Positions[v].X;
                interleaved[o + 1] = mesh.Positions[v].Y;
                interleaved[o + 2] = mesh.Positions[v].Z;
                interleaved[o + 3] = mesh.Normals[v].X;
                interleaved[o + 4] = mesh.Normals[v].Y;
                interleaved[o + 5] = mesh.Normals[v].Z;
                for (var k = 0; k < 4; k++)
                {
                    interleaved[o + 6 + k] = mesh.Joints[v * 4 + k];
                    interleaved[o + 10 + k] = mesh.Weights[v * 4 + k];
                }
            }

            var vao = _gl.GenVertexArray();
            _gl.BindVertexArray(vao);
            var vbo = UploadArray(interleaved);
            var ebo = UploadIndices(mesh.Indices);
            const uint stride = 14 * 4;
            Attribute(0, 3, stride, 0);
            Attribute(1, 3, stride, 12);
            Attribute(2, 4, stride, 24);
            Attribute(3, 4, stride, 40);
            _gl.BindVertexArray(0);
            meshes.Add(new GpuMesh(vao, vbo, ebo, mesh.Indices.Length, 0, null, mesh.IsPants));
        }

        return _bodies[model] = meshes.ToArray();
    }

    private GpuMesh[] GetHead(string key, IReadOnlyList<HeadMeshData> data)
    {
        if (_heads.TryGetValue(key, out var existing))
            return existing;

        var meshes = new List<GpuMesh>();
        foreach (var mesh in data)
        {
            var count = mesh.Positions.Length;
            var interleaved = new float[count * 16];
            for (var v = 0; v < count; v++)
            {
                var o = v * 16;
                interleaved[o] = mesh.Positions[v].X;
                interleaved[o + 1] = mesh.Positions[v].Y;
                interleaved[o + 2] = mesh.Positions[v].Z;
                interleaved[o + 3] = mesh.Normals[v].X;
                interleaved[o + 4] = mesh.Normals[v].Y;
                interleaved[o + 5] = mesh.Normals[v].Z;
                interleaved[o + 6] = mesh.Tangents[v].X;
                interleaved[o + 7] = mesh.Tangents[v].Y;
                interleaved[o + 8] = mesh.Tangents[v].Z;
                interleaved[o + 9] = mesh.Texcoords[v].X;
                interleaved[o + 10] = mesh.Texcoords[v].Y;
                interleaved[o + 11] = mesh.Parameters[v].X;
                interleaved[o + 12] = mesh.Parameters[v].Y;
                interleaved[o + 13] = mesh.Parameters[v].Z;
                interleaved[o + 14] = mesh.Parameters[v].W;
            }

            var vao = _gl.GenVertexArray();
            _gl.BindVertexArray(vao);
            var vbo = UploadArray(interleaved);
            var ebo = UploadIndices(mesh.Indices);
            const uint stride = 16 * 4;
            Attribute(0, 3, stride, 0);
            Attribute(1, 3, stride, 12);
            Attribute(2, 3, stride, 24);
            Attribute(3, 2, stride, 36);
            Attribute(4, 4, stride, 44);
            _gl.BindVertexArray(0);

            uint texture = 0;
            if (mesh.TexturePixels is { } pixels && mesh.TextureWidth > 0 && mesh.TextureHeight > 0)
            {
                texture = _gl.GenTexture();
                _gl.BindTexture(TextureTarget.Texture2D, texture);
                fixed (byte* p = pixels)
                    _gl.TexImage2D(
                        TextureTarget.Texture2D,
                        0,
                        InternalFormat.Rgba8,
                        (uint)mesh.TextureWidth,
                        (uint)mesh.TextureHeight,
                        0,
                        PixelFormat.Rgba,
                        PixelType.UnsignedByte,
                        p
                    );
                _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
                _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
                _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.MirroredRepeat);
                _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.MirroredRepeat);
                _gl.BindTexture(TextureTarget.Texture2D, 0);
            }

            meshes.Add(new GpuMesh(vao, vbo, ebo, mesh.Indices.Length, texture, mesh, false));
        }

        return _heads[key] = meshes.ToArray();
    }

    private uint UploadArray(float[] data)
    {
        var vbo = _gl.GenBuffer();
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, vbo);
        fixed (float* p = data)
            _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(data.Length * 4), p, BufferUsageARB.StaticDraw);
        return vbo;
    }

    private uint UploadIndices(int[] indices)
    {
        var ebo = _gl.GenBuffer();
        _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, ebo);
        fixed (int* p = indices)
            _gl.BufferData(BufferTargetARB.ElementArrayBuffer, (nuint)(indices.Length * 4), p, BufferUsageARB.StaticDraw);
        return ebo;
    }

    private void Attribute(uint index, int size, uint stride, int offset)
    {
        _gl.EnableVertexAttribArray(index);
        _gl.VertexAttribPointer(index, size, VertexAttribPointerType.Float, false, stride, (void*)offset);
    }

    private void SetLighting(uint program)
    {
        var (ambient, diffuse, specular, direction) = NativeMiiRenderer.LightConstants;
        SetVec3(program, "uLightAmb", ambient);
        SetVec3(program, "uLightDiff", diffuse);
        SetVec3(program, "uLightSpec", specular);
        SetVec3(program, "uLightDir", direction);
        var p = MiiLightingProfiles.Default;
        var profile =
            stackalloc float[] {
                p.AmbientScale,
                p.DirectionalLightInfluence,
                p.DiffuseScale,
                p.DiffuseFloor,
                p.SpecularScale,
                p.RimScale,
                p.RimPower,
            };
        _gl.Uniform1(_gl.GetUniformLocation(program, "uProfile"), 7, profile);
    }

    private void SetMaterial(uint program, Vector3 ambient, Vector3 diffuse, Vector3 specular, float power, int specularMode, Vector3 rim)
    {
        SetVec3(program, "uMatAmb", ambient);
        SetVec3(program, "uMatDiff", diffuse);
        SetVec3(program, "uMatSpec", specular);
        SetVec3(program, "uMatRim", rim);
        _gl.Uniform1(_gl.GetUniformLocation(program, "uSpecPow"), power);
        _gl.Uniform1(_gl.GetUniformLocation(program, "uSpecMode"), specularMode);
    }

    private void SetMatrix(uint program, string name, Matrix4x4 matrix) =>
        _gl.UniformMatrix4(_gl.GetUniformLocation(program, name), 1, false, (float*)&matrix);

    private void SetVec3(uint program, string name, Vector3 v) => _gl.Uniform3(_gl.GetUniformLocation(program, name), v.X, v.Y, v.Z);

    private void SetVec4(uint program, string name, Vector4 v) => _gl.Uniform4(_gl.GetUniformLocation(program, name), v.X, v.Y, v.Z, v.W);

    private uint CreateProgram(string vertexSource, string fragmentSource)
    {
        var vs = Compile(ShaderType.VertexShader, vertexSource);
        var fs = Compile(ShaderType.FragmentShader, fragmentSource);
        var program = _gl.CreateProgram();
        _gl.AttachShader(program, vs);
        _gl.AttachShader(program, fs);
        _gl.LinkProgram(program);
        _gl.GetProgram(program, ProgramPropertyARB.LinkStatus, out var status);
        if (status == 0)
            throw new InvalidOperationException("Shader link failed: " + _gl.GetProgramInfoLog(program));
        _gl.DeleteShader(vs);
        _gl.DeleteShader(fs);
        return program;
    }

    private uint Compile(ShaderType type, string source)
    {
        var shader = _gl.CreateShader(type);
        _gl.ShaderSource(shader, source);
        _gl.CompileShader(shader);
        _gl.GetShader(shader, ShaderParameterName.CompileStatus, out var status);
        if (status == 0)
            throw new InvalidOperationException($"{type} compile failed: {_gl.GetShaderInfoLog(shader)}");
        return shader;
    }

    private void DeleteMesh(GpuMesh mesh)
    {
        _gl.DeleteVertexArray(mesh.Vao);
        _gl.DeleteBuffer(mesh.Vbo);
        _gl.DeleteBuffer(mesh.Ebo);
        if (mesh.Texture != 0)
            _gl.DeleteTexture(mesh.Texture);
    }

    public void Dispose()
    {
        foreach (var mesh in _heads.Values.SelectMany(m => m).Concat(_bodies.Values.SelectMany(m => m)))
            DeleteMesh(mesh);
        _heads.Clear();
        _bodies.Clear();
        _gl.DeleteProgram(_headProgram);
        _gl.DeleteProgram(_bodyProgram);
        _particles?.Dispose();
    }
}
