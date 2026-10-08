using Microsoft.Extensions.Logging.Abstractions;
using MiiAnim.Core.Animation;
using MiiAnim.Core.Rig;
using WheelWizard.MiiAnimations.Library;
using WheelWizard.MiiAnimations.Playback;

namespace WheelWizard.Test.Features.MiiAnimations;

public class MiiAnimationPlayerTests
{
    private static readonly TrackId HeadTurn = TrackId.Bone(MiiBone.Head, Channel.RotY);

    private static MiiAnimation Clip(float headTurn, int length = 60, params (int Frame, string Name)[] events)
    {
        var clip = new MiiAnimation
        {
            Name = "Test",
            Fps = 60,
            Length = length,
        };
        var curve = clip.GetOrCreateCurve(HeadTurn);
        curve.SetKey(0, headTurn);
        curve.SetKey(length, headTurn);
        foreach (var (frame, name) in events)
            clip.AddEvent(frame, name);
        return clip;
    }

    [Fact]
    public void Play_CrossFadesFromThePreviousClip()
    {
        var player = new MiiAnimationPlayer();
        player.Play(Clip(10), fadeSeconds: 0);
        player.Update(0.1);

        player.Play(Clip(30), fadeSeconds: 0.2);
        Assert.Equal(10, player.Sample(HeadTurn), 3);

        player.Update(0.1);
        Assert.Equal(20, player.Sample(HeadTurn), 3);

        player.Update(0.15);
        Assert.Equal(30, player.Sample(HeadTurn), 3);
    }

    [Fact]
    public void CrossFade_TurnsTheShortWayAfterAFullSpin()
    {
        var player = new MiiAnimationPlayer();
        player.Play(Clip(350), fadeSeconds: 0);
        player.Play(Clip(10), fadeSeconds: 0.2);
        player.Update(0.1);

        // Halfway from 350° to 10° the short way is 360°, not 180°.
        Assert.Equal(360, player.Sample(HeadTurn), 3);
    }

    [Fact]
    public void Update_RaisesEventsAndFinishOnce()
    {
        var clip = Clip(0, 30, (0, "start"), (20, "middle"));
        var player = new MiiAnimationPlayer();
        var events = new List<string>();
        var finished = 0;
        player.EventReached += (_, e) => events.Add(e.Name);
        player.Finished += _ => finished++;

        player.Play(clip);
        for (var i = 0; i < 60; i++)
        {
            player.Update(1 / 60.0);
            player.RaiseEvents();
        }

        Assert.Equal(["start", "middle"], events);
        Assert.Equal(1, finished);
        Assert.True(player.IsFinished);
    }

    [Fact]
    public void Look_TurnsTheHeadOnTopOfTheClip()
    {
        var player = new MiiAnimationPlayer();
        player.Play(Clip(10), fadeSeconds: 0);
        player.Look.TargetYaw = 20;
        for (var i = 0; i < 120; i++)
            player.Update(1 / 60.0);

        Assert.InRange(player.Sample(HeadTurn), 24.9f, 25.1f);
        Assert.InRange(player.Sample(TrackId.Bone(MiiBone.Chest, Channel.RotY)), 4.9f, 5.1f);
    }

    [Fact]
    public void Particles_KeepFlyingAfterTheClipIsReplaced()
    {
        var withPoof = Clip(0);
        withPoof.Particles.Add(new ParticleEffect { Count = 20 });
        var player = new MiiAnimationPlayer();
        var stage = Substitute.For<MiiAnim.Core.Evaluation.IParticleStage>();
        stage.FavoriteColor(Arg.Any<int>()).Returns(System.Numerics.Vector3.One);
        var particles = new List<MiiAnim.Core.Evaluation.Particle>();

        player.Play(withPoof);
        player.Update(0.05);
        player.Play(Clip(0));
        player.Update(0.05);
        player.CollectParticles(_ => stage, particles);

        Assert.NotEmpty(particles);
    }

    [Fact]
    public void Library_ReadsEveryShippedAnimation()
    {
        var library = new MiiAnimationLibrary(NullLogger<MiiAnimationLibrary>.Instance);
        var folders = new[] { "editor/idle", "editor/become_girl", "editor/favorite_color", "editor/save" };

        foreach (var folder in folders)
        {
            var paths = library.List(folder);
            Assert.NotEmpty(paths);
            Assert.All(paths, path => Assert.NotNull(library.Get(path)));
        }

        Assert.Equal(10, library.List("editor/idle").Count);
        Assert.Null(library.Get("editor/does_not_exist"));
    }

    [Fact]
    public void Library_ClipsBringTheirOwnParticleImages()
    {
        var library = new MiiAnimationLibrary(NullLogger<MiiAnimationLibrary>.Instance);

        var effects = library.List("editor/favorite_color").Select(library.Get).SelectMany(clip => clip!.Particles).ToList();

        Assert.NotEmpty(effects);
        Assert.All(effects, effect => Assert.True(effect.Shape is { } shape && ParticleShape.IsSvg(shape.Svg)));
    }
}
