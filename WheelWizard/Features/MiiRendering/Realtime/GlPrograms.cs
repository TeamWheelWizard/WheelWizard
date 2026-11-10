using System.Collections.Concurrent;
using Silk.NET.OpenGL;

namespace WheelWizard.MiiRendering.Realtime;

/// <summary>
/// Links shader programs, keeping their compiled binaries (where the driver offers them) so the next Mii view with the
/// same shaders loads them instead of compiling again. Every realtime Mii view has its own OpenGL context and its own
/// copies of the programs, and compiling them (through ANGLE on Windows, HLSL) is the slow part of starting a view.
/// </summary>
internal static unsafe class GlPrograms
{
    private static readonly ConcurrentDictionary<string, (GLEnum Format, byte[] Binary)> Binaries = new();

    /// <param name="what">Name for errors, e.g. "Head".</param>
    public static uint Create(GL gl, string vertexSource, string fragmentSource, string what)
    {
        var key = $"{gl.GetStringS(StringName.Renderer)}|{gl.GetStringS(StringName.Version)}|{vertexSource}|{fragmentSource}";
        if (Binaries.TryGetValue(key, out var stored) && Load(gl, stored.Format, stored.Binary) is { } loaded)
            return loaded;

        var program = Compile(gl, vertexSource, fragmentSource, what);
        if (Save(gl, program) is { } saved)
            Binaries[key] = saved;
        return program;
    }

    private static uint Compile(GL gl, string vertexSource, string fragmentSource, string what)
    {
        uint Shader(ShaderType type, string source)
        {
            var shader = gl.CreateShader(type);
            gl.ShaderSource(shader, source);
            gl.CompileShader(shader);
            gl.GetShader(shader, ShaderParameterName.CompileStatus, out var ok);
            if (ok == 0)
                throw new InvalidOperationException($"{what} {type} compile failed: {gl.GetShaderInfoLog(shader)}");
            return shader;
        }

        var vertex = Shader(ShaderType.VertexShader, vertexSource);
        var fragment = Shader(ShaderType.FragmentShader, fragmentSource);
        var program = gl.CreateProgram();
        gl.AttachShader(program, vertex);
        gl.AttachShader(program, fragment);
        // Ask to be able to read the binary back (some drivers only keep it when asked before linking).
        try
        {
            gl.ProgramParameter(program, ProgramParameterPName.BinaryRetrievableHint, 1);
        }
        catch (Exception)
        {
            // Not on this driver: the binary just won't be kept.
        }
        gl.GetError();
        gl.LinkProgram(program);
        gl.GetProgram(program, ProgramPropertyARB.LinkStatus, out var linked);
        gl.DeleteShader(vertex);
        gl.DeleteShader(fragment);
        if (linked == 0)
            throw new InvalidOperationException($"{what} shader link failed: {gl.GetProgramInfoLog(program)}");
        return program;
    }

    /// <summary>The program's binary, or null when the driver doesn't hand them out.</summary>
    private static (GLEnum Format, byte[] Binary)? Save(GL gl, uint program)
    {
        try
        {
            gl.GetError();
            gl.GetProgram(program, GLEnum.ProgramBinaryLength, out var length);
            if (gl.GetError() != GLEnum.NoError || length <= 0)
                return null;
            var binary = new byte[length];
            uint written;
            GLEnum format;
            fixed (byte* p = binary)
                gl.GetProgramBinary(program, (uint)length, &written, &format, p);
            if (gl.GetError() != GLEnum.NoError || written == 0)
                return null;
            return (format, binary[..(int)written]);
        }
        catch (Exception)
        {
            // No program binaries on this driver (or this GL binding): just compile every time.
            return null;
        }
    }

    /// <summary>A program made from a stored binary, or null when the driver doesn't take it (then compile).</summary>
    private static uint? Load(GL gl, GLEnum format, byte[] binary)
    {
        var program = gl.CreateProgram();
        try
        {
            gl.GetError();
            fixed (byte* p = binary)
                gl.ProgramBinary(program, format, p, (uint)binary.Length);
            gl.GetProgram(program, ProgramPropertyARB.LinkStatus, out var linked);
            if (gl.GetError() == GLEnum.NoError && linked != 0)
                return program;
        }
        catch (Exception)
        {
            // Fall back to compiling.
        }

        gl.DeleteProgram(program);
        gl.GetError();
        return null;
    }
}
