using System.Globalization;
using System.Numerics;
using Microsoft.UI.Composition;
using Tack.Core.Pinboard.Motion;

namespace Tack.App.Pinboard.Motion;

/// <summary>
/// Plays the board's motions on the compositor, off the UI thread: sampled
/// frames become keyframes played straight between them, so every easing is
/// exactly the one sampled in Core. Channels map to a layer's properties:
/// opacity, Translation.X and .Y (the layer's element needs translation
/// enabled), Scale (uniform) or Scale.X and .Y (not both on one layer), and
/// RotationAngleInDegrees about the layer's own axis and centre point.
/// UI thread only.
/// </summary>
sealed class Keyframer(Compositor compositor)
{
    readonly HashSet<Playing> _playing = [];
    readonly Dictionary<(Visual, string), Playing> _owners = [];
    CompositionEasingFunction? _linear;

    /// <summary>Motions still running.</summary>
    public int Running => _playing.Count;

    CompositionEasingFunction Linear => _linear ??= compositor.CreateLinearEasingFunction();

    /// <summary>
    /// Plays <paramref name="motion"/> on <paramref name="visual"/>. A fill
    /// backwards shows the first frame during the delay; without a fill
    /// forwards the layer goes back to the underlying values at the end.
    /// <paramref name="done"/> runs when it ends, not when it is cancelled.
    /// </summary>
    public Playing Play(SampledMotion motion, Visual visual, Action? done = null)
    {
        var batch = compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
        var started = new List<(string, KeyFrameAnimation)>();
        foreach (var channel in motion.Channels)
        {
            KeyFrameAnimation animation = channel == Channel.Scale ? ScaleFrames(motion) : ScalarFrames(motion, channel);
            Time(animation, motion.Duration, motion.Delay, motion.Fill is Fill.Backwards or Fill.Both);
            started.Add((PropertyOf(channel), animation));
        }
        var playing = Start(visual, started);
        batch.End();
        batch.Completed += (_, _) =>
        {
            if (!playing.Finish())
            {
                return;
            }
            if (motion.Fill is Fill.None or Fill.Backwards)
            {
                // Only what no later motion has taken over.
                foreach (var channel in motion.Channels.Where(c => playing.Properties.Contains(PropertyOf(c))))
                {
                    Hold(visual, channel, motion.Underlying(channel));
                }
            }
            done?.Invoke();
        };
        return playing;
    }

    /// <summary>
    /// Glides one channel from wherever it is now, mid-motion or not, to
    /// <paramref name="target"/> along <paramref name="easing"/>: a transition.
    /// </summary>
    public Playing Transition(
        Visual visual, Channel channel, double target, double durationMs, Easing easing, double delayMs = 0, Action? done = null)
    {
        var batch = compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
        KeyFrameAnimation animation;
        var to = Number(target);
        if (channel == Channel.Scale)
        {
            var scale = compositor.CreateVector3KeyFrameAnimation();
            foreach (var (t, e) in TransitionFrames(durationMs, easing))
            {
                scale.InsertExpressionKeyFrame(t, $"this.StartingValue + (Vector3({to}, {to}, 1) - this.StartingValue) * {Number(e)}", Linear);
            }
            animation = scale;
        }
        else
        {
            var scalar = compositor.CreateScalarKeyFrameAnimation();
            foreach (var (t, e) in TransitionFrames(durationMs, easing))
            {
                var value = $"this.StartingValue + ({to} - this.StartingValue) * {Number(e)}";
                scalar.InsertExpressionKeyFrame(t, channel == Channel.Opacity ? $"Clamp({value}, 0, 1)" : value, Linear);
            }
            animation = scalar;
        }
        Time(animation, durationMs, delayMs, backwards: false);
        var playing = Start(visual, [(PropertyOf(channel), animation)]);
        batch.End();
        batch.Completed += (_, _) =>
        {
            if (playing.Finish())
            {
                done?.Invoke();
            }
        };
        return playing;
    }

    /// <summary>Puts a channel at <paramref name="value"/> now, taking it from any motion on it.</summary>
    public void Set(Visual visual, Channel channel, double value)
    {
        var property = PropertyOf(channel);
        if (_owners.Remove((visual, property), out var previous))
        {
            previous.Lose(property);
        }
        Hold(visual, channel, value);
    }

    /// <summary>Stops every motion still running, where it is.</summary>
    public void CancelAll()
    {
        foreach (var playing in _playing.ToArray())
        {
            playing.Cancel();
        }
    }

    /// <summary>A motion is over: it no longer owns its properties.</summary>
    internal void Forget(Playing playing)
    {
        _playing.Remove(playing);
        foreach (var property in playing.Properties)
        {
            if (_owners.TryGetValue((playing.Visual, property), out var owner) && owner == playing)
            {
                _owners.Remove((playing.Visual, property));
            }
        }
    }

    /// <summary>The property each channel drives.</summary>
    public static string PropertyOf(Channel channel) => channel switch
    {
        Channel.Opacity => "Opacity",
        Channel.X => "Translation.X",
        Channel.Y => "Translation.Y",
        Channel.Scale => "Scale",
        Channel.ScaleX => "Scale.X",
        Channel.ScaleY => "Scale.Y",
        _ => "RotationAngleInDegrees",
    };

    Playing Start(Visual visual, List<(string Property, KeyFrameAnimation Animation)> animations)
    {
        var playing = new Playing(this, visual, [.. animations.Select(a => a.Property)]);
        foreach (var (property, animation) in animations)
        {
            if (_owners.TryGetValue((visual, property), out var previous))
            {
                previous.Lose(property);
            }
            _owners[(visual, property)] = playing;
            visual.StartAnimation(property, animation);
        }
        _playing.Add(playing);
        return playing;
    }

    ScalarKeyFrameAnimation ScalarFrames(SampledMotion motion, Channel channel)
    {
        var animation = compositor.CreateScalarKeyFrameAnimation();
        var values = motion[channel];
        for (var i = 0; i < motion.Times.Count; i++)
        {
            animation.InsertKeyFrame((float)motion.Times[i], (float)values[i], Linear);
        }
        return animation;
    }

    Vector3KeyFrameAnimation ScaleFrames(SampledMotion motion)
    {
        var animation = compositor.CreateVector3KeyFrameAnimation();
        var values = motion[Channel.Scale];
        for (var i = 0; i < motion.Times.Count; i++)
        {
            var s = (float)values[i];
            animation.InsertKeyFrame((float)motion.Times[i], new Vector3(s, s, 1), Linear);
        }
        return animation;
    }

    /// <summary>Puts a channel at <paramref name="value"/> and keeps it there, sub-properties included.</summary>
    void Hold(Visual visual, Channel channel, double value)
    {
        KeyFrameAnimation animation;
        if (channel == Channel.Scale)
        {
            var scale = compositor.CreateVector3KeyFrameAnimation();
            scale.InsertKeyFrame(1, new Vector3((float)value, (float)value, 1), Linear);
            animation = scale;
        }
        else
        {
            var scalar = compositor.CreateScalarKeyFrameAnimation();
            scalar.InsertKeyFrame(1, (float)value, Linear);
            animation = scalar;
        }
        animation.Duration = TimeSpan.FromMilliseconds(1);
        visual.StartAnimation(PropertyOf(channel), animation);
    }

    static void Time(KeyFrameAnimation animation, double durationMs, double delayMs, bool backwards)
    {
        animation.Duration = TimeSpan.FromMilliseconds(Math.Max(1, durationMs));
        if (delayMs > 0)
        {
            animation.DelayTime = TimeSpan.FromMilliseconds(delayMs);
            animation.DelayBehavior = backwards
                ? AnimationDelayBehavior.SetInitialValueBeforeDelay
                : AnimationDelayBehavior.SetInitialValueAfterDelay;
        }
    }

    /// <summary>A transition's frames: progress at most 120 a second, plus the easing's corners, with the eased progress at each.</summary>
    static IEnumerable<(float T, double E)> TransitionFrames(double durationMs, Easing easing)
    {
        var count = Math.Max(2, (int)Math.Ceiling(durationMs / Timeline.DefaultFrameMs));
        var times = Enumerable.Range(0, count + 1).Select(i => i / (double)count).Concat(easing.Knots);
        return times.Select(t => (float)t).Distinct().Order().Select(t => (t, t >= 1 ? 1.0 : easing.At(t)));
    }

    /// <summary>A number as the expression language reads it: invariant, never in exponent form.</summary>
    static string Number(double value) => value.ToString("0.#########", CultureInfo.InvariantCulture);
}
