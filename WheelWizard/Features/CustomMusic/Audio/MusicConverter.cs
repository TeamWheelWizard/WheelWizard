using WheelWizard.CustomMusic.Audio.Dsp;
using WheelWizard.CustomMusic.Audio.Formats;

namespace WheelWizard.CustomMusic.Audio;

/// <summary>How the final lap version of a course song is made.</summary>
public enum FinalLapStyle
{
    /// <summary>Speed and pitch +6%: exactly what Retro Rewind's own "music speed-up" does to the normal song.</summary>
    Faster,

    /// <summary>Tempo +8% with the pitch kept, closest to the original game's final lap tracks.</summary>
    FasterKeepPitch,

    /// <summary>The final lap plays the same song as laps 1 and 2.</summary>
    Unchanged,
}

/// <summary>Where a converted song's loop comes from.</summary>
public enum LoopKind
{
    /// <summary>Plays once (jingles and fanfares).</summary>
    None,

    /// <summary>Loop points stored in the file (WAV smpl chunk, LOOPSTART tags, BRSTM).</summary>
    FromFile,

    /// <summary>A repeat found in the music itself (<see cref="Dsp.LoopFinder"/>).</summary>
    Found,

    /// <summary>The ending crossfades into the opening.</summary>
    Crossfade,
}

public sealed record ConversionOptions
{
    /// <summary>Mario Kart Wii streams are 32 kHz.</summary>
    public int SampleRate { get; init; } = 32000;

    /// <summary>Whether the result loops (course and menu music) or plays once (fanfares and jingles).</summary>
    public bool Loop { get; init; } = true;

    /// <summary>Also produce the final lap file.</summary>
    public bool CreateFinalLap { get; init; }

    public FinalLapStyle FinalLapStyle { get; init; } = FinalLapStyle.Faster;

    /// <summary>Integrated loudness the song is matched to, normally that of the music it replaces.</summary>
    public double TargetLufs { get; init; } = MusicLoudness.DefaultTargetLufs;

    /// <summary>User adjustment on top of the matched loudness.</summary>
    public double VolumeOffsetDb { get; init; }

    /// <summary>
    /// The volume byte (1..127) of the music being replaced, or 0 for none. Retro Rewind reads it from the BRSTM
    /// header when the music starts and multiplies the sound's volume by byte / 127 (0 means full volume), so a
    /// replacement carries the same byte. When the song needs more level than its peaks allow, the byte is raised
    /// (up to 127) instead of limiting the song harder; without a byte the music already plays at full volume.
    /// </summary>
    public byte PulsarVolume { get; init; }

    /// <summary>Search the music for a seamless loop when the file has no loop points (otherwise crossfade).</summary>
    public bool FindLoop { get; init; } = true;

    /// <summary>Length of the crossfade that makes songs without loop points loop smoothly.</summary>
    public double CrossfadeSeconds { get; init; } = 2.5;

    public double MaxDurationSeconds { get; init; } = 15 * 60;
}

/// <param name="SourceLufs">Loudness of the song as it was dropped in.</param>
/// <param name="TargetLufs">Requested loudness of the converted file (the original's loudness plus the volume adjustment).</param>
/// <param name="OutputLufs">Loudness of the converted file. Lower than the target when the limiter had to hold the peaks back.</param>
/// <param name="PulsarVolume">Volume byte written to the BRSTM header (0 = none).</param>
public sealed record ConversionReport(
    double SourceLufs,
    double TargetLufs,
    double AppliedGainDb,
    double LimiterReductionDb,
    TimeSpan Duration,
    LoopKind Loop,
    int LoopStartSample,
    int SampleCount,
    double OutputLufs,
    byte PulsarVolume
);

public sealed record ConversionResult(byte[] Normal, byte[]? FinalLap, ConversionReport Report);

public static class MusicLoudness
{
    /// <summary>
    /// Fallback when the original music could not be measured: a typical level for game music (loud enough
    /// to sit over engine noise and effects). Real measurements of the replaced music always take priority.
    /// </summary>
    public const double DefaultTargetLufs = -16.0;

    public const double FinalLapPitchSpeed = 1.06;
    public const double FinalLapTempoSpeed = 1.08;

    /// <summary>Highest sample peak a converted file may have, leaving room for DSP-ADPCM's small overshoots.</summary>
    public const double PeakCeilingDb = -1.0;

    /// <summary>Largest volume byte Retro Rewind accepts; it means "full volume".</summary>
    public const byte MaxPulsarVolume = 0x7F;

    /// <summary>Loop points shorter than this are treated as broken; the game would repeat a tiny fragment as a buzz.</summary>
    public const double MinLoopSeconds = 1.0;

    /// <summary>Gain of a volume byte in dB (0 = no byte, which means the sound archive's volume).</summary>
    public static double VolumeByteDb(byte volume) => volume == 0 ? 0 : 20 * Math.Log10(Math.Min(volume, MaxPulsarVolume) / (double)MaxPulsarVolume);
}

public static class MusicConverter
{
    public static ConversionResult Convert(
        AudioBuffer source,
        ConversionOptions options,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default
    )
    {
        progress?.Report(0.05);
        // Damaged files can decode to NaN or infinity, which would poison every measurement below.
        AudioOps.ReplaceNonFinite(source);

        // Loop points from the file (WAV smpl, LOOPSTART tags, BRSTM) are used as-is; otherwise we build a loop.
        var hasSourceLoop =
            options.Loop
            && source.Loop is { } sourceLoop
            && sourceLoop.IsValid(source.SampleCount)
            && sourceLoop.Length >= source.SampleRate * MusicLoudness.MinLoopSeconds;
        if (!hasSourceLoop)
            source.Loop = null;

        var stereo = AudioOps.ToStereo(source);
        var trimmed = AudioOps.TrimSilence(stereo);
        cancellationToken.ThrowIfCancellationRequested();

        // Give the resampler real audio to look at across the loop seam.
        var extended = AudioOps.ExtendPastLoopEnd(trimmed, trimmed.SampleRate / 10);
        var audio = Resampler.Resample(extended, options.SampleRate);
        audio = AudioOps.Truncate(audio, (int)(options.MaxDurationSeconds * options.SampleRate));
        if (options.Loop && audio.SampleCount < options.SampleRate * MusicLoudness.MinLoopSeconds)
            throw new InvalidDataException("This song is too short to loop; use a song of at least a second.");
        progress?.Report(0.3);
        cancellationToken.ThrowIfCancellationRequested();

        // Loudness matching (measured on the part that actually plays, before any loop seam work).
        var measureLength = audio.Loop?.End ?? audio.SampleCount;
        var loudness = LoudnessMeter.Measure(audio.Channels, audio.SampleRate, 0, measureLength);
        if (loudness.IsSilent)
            throw new InvalidDataException("This song is silent, so there is nothing to convert.");

        var gainDb = options.TargetLufs - loudness.IntegratedLufs + options.VolumeOffsetDb;
        var pulsarVolume = Math.Min(options.PulsarVolume, MusicLoudness.MaxPulsarVolume);
        if (pulsarVolume > 0 && loudness.SamplePeak > 0)
        {
            // Peaks that would go over the ceiling are handled by raising the volume byte first (the game then
            // plays the file louder) and only the rest by the limiter, so the song keeps its dynamics.
            var excessDb = 20 * Math.Log10(loudness.SamplePeak) + gainDb - MusicLoudness.PeakCeilingDb;
            if (excessDb > 0)
            {
                var raised = (byte)Math.Min(MusicLoudness.MaxPulsarVolume, Math.Floor(pulsarVolume * Math.Pow(10, excessDb / 20)));
                if (raised > pulsarVolume)
                {
                    gainDb -= MusicLoudness.VolumeByteDb(raised) - MusicLoudness.VolumeByteDb(pulsarVolume);
                    pulsarVolume = raised;
                }
            }
        }

        AudioOps.ApplyGain(audio, gainDb);
        var limiterReduction = PeakLimiter.Apply(audio, MusicLoudness.PeakCeilingDb);
        progress?.Report(0.45);
        cancellationToken.ThrowIfCancellationRequested();

        var loopKind = !options.Loop ? LoopKind.None
            : hasSourceLoop ? LoopKind.FromFile
            : LoopKind.Crossfade;
        if (options.Loop && audio.Loop == null)
        {
            var beforeLoop = LoudnessMeter.Measure(audio.Channels, audio.SampleRate);
            // A repeat in the music gives a loop nobody can hear; a crossfade is the fallback.
            if (options.FindLoop && LoopFinder.Find(audio, cancellationToken: cancellationToken) is { } found)
            {
                audio = LoopFinder.Apply(audio, found);
                loopKind = LoopKind.Found;
            }
            else
            {
                audio = AudioOps.CreateCrossfadeLoop(audio, options.CrossfadeSeconds);
            }

            // Cutting the song (or an equal-power crossfade over a held note) shifts its loudness a little. Undo only
            // that change: comparing with the target instead would re-add whatever the limiter took off.
            var looped = LoudnessMeter.Measure(audio.Channels, audio.SampleRate, 0, audio.Loop?.End ?? audio.SampleCount);
            var correction = beforeLoop.IntegratedLufs - looped.IntegratedLufs;
            if (!looped.IsSilent && !beforeLoop.IsSilent && Math.Abs(correction) > 0.2)
            {
                AudioOps.ApplyGain(audio, correction);
                gainDb += correction;
                limiterReduction = Math.Max(limiterReduction, PeakLimiter.Apply(audio, MusicLoudness.PeakCeilingDb));
            }
        }
        else if (!options.Loop)
        {
            AudioOps.FadeOutTail(audio, 0.01);
        }

        var normalBuffer = options.Loop ? AudioOps.AlignLoopStart(audio, Brstm.SamplesPerBlock) : audio;
        AudioBuffer? finalBuffer = null;
        if (options.CreateFinalLap && options.FinalLapStyle != FinalLapStyle.Unchanged)
        {
            finalBuffer = options.FinalLapStyle == FinalLapStyle.FasterKeepPitch
                ? MakeFinalLap(audio, buffer => TimeStretcher.ChangeTempo(buffer, MusicLoudness.FinalLapTempoSpeed))
                : MakeFinalLap(audio, buffer => Resampler.Resample(buffer, buffer.SampleRate, MusicLoudness.FinalLapPitchSpeed));
        }

        progress?.Report(0.6);
        cancellationToken.ThrowIfCancellationRequested();

        // The DSP-ADPCM encode is the slow part; both versions encode at the same time.
        byte[] normal = [];
        byte[]? finalLap = null;
        Parallel.Invoke(
            () => normal = Encode(normalBuffer, pulsarVolume),
            () =>
            {
                if (finalBuffer != null)
                    finalLap = Encode(finalBuffer, pulsarVolume);
            }
        );
        if (options.CreateFinalLap && options.FinalLapStyle == FinalLapStyle.Unchanged)
            finalLap = normal;
        progress?.Report(1.0);

        var output = LoudnessMeter.Measure(normalBuffer.Channels, normalBuffer.SampleRate, 0, normalBuffer.Loop?.End ?? normalBuffer.SampleCount);
        var report = new ConversionReport(
            loudness.IntegratedLufs,
            options.TargetLufs + options.VolumeOffsetDb,
            gainDb,
            limiterReduction,
            TimeSpan.FromSeconds(normalBuffer.SampleCount / (double)normalBuffer.SampleRate),
            loopKind,
            normalBuffer.Loop?.Start ?? 0,
            normalBuffer.SampleCount,
            output.IntegratedLufs,
            pulsarVolume
        );
        return new ConversionResult(normal, finalLap, report);
    }

    private static AudioBuffer MakeFinalLap(AudioBuffer looped, Func<AudioBuffer, AudioBuffer> speedUp)
    {
        var extended = AudioOps.ExtendPastLoopEnd(looped, looped.SampleRate / 5);
        var faster = speedUp(extended);
        return faster.Loop != null ? AudioOps.AlignLoopStart(faster, Brstm.SamplesPerBlock) : faster;
    }

    private static byte[] Encode(AudioBuffer buffer, byte pulsarVolume)
    {
        var pcm = new short[buffer.ChannelCount][];
        for (var c = 0; c < buffer.ChannelCount; c++)
            pcm[c] = AudioOps.ToPcm16(buffer.Channels[c], seed: 0x5EED + c);

        return BrstmWriter.Write(
            new BrstmStream
            {
                Channels = pcm,
                SampleRate = buffer.SampleRate,
                Looping = buffer.Loop != null,
                LoopStart = buffer.Loop?.Start ?? 0,
                PulsarVolume = pulsarVolume == 0 ? null : Math.Min(pulsarVolume, MusicLoudness.MaxPulsarVolume),
            }
        );
    }
}
