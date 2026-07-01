using System;
using System.Collections;
using System.Reflection;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Consolonia.Controls.Brushes;
using NUnit.Framework;

namespace Consolonia.Core.Tests
{
    [TestFixture]
    public class LineBrushAnimatorTests
    {
        private static LineBrush MakeLineBrush(double startX, LineStyle lineStyle = LineStyle.SingleLine)
        {
            return new LineBrush
            {
                Brush = new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(startX, 0, RelativeUnit.Relative),
                    EndPoint = new RelativePoint(startX, 1, RelativeUnit.Relative),
                    GradientStops =
                    {
                        new GradientStop(Color.FromRgb(255, 0, 0), 0),
                        new GradientStop(Color.FromRgb(0, 0, 255), 1)
                    }
                },
                LineStyle = lineStyle
            };
        }

        [Test]
        public void InterpolatesInnerGradientBetweenLineBrushes()
        {
            var animator = new LineBrushAnimator();
            LineBrush from = MakeLineBrush(0.0);
            LineBrush to = MakeLineBrush(1.0);

            var result = (LineBrush)animator.Interpolate(0.5, from, to);

            Assert.IsNotNull(result, "Interpolation should produce a LineBrush, not null.");
            var inner = result.Brush as ILinearGradientBrush;
            Assert.IsNotNull(inner, "Inner brush should remain a linear gradient.");

            // The inner gradient's start point should be interpolated half-way (0.0 -> 1.0 == 0.5),
            // which proves the wrapped gradient is animated rather than snapped.
            Assert.AreEqual(0.5, inner.StartPoint.Point.X, 1e-6);
        }

        [Test]
        public void PreservesLineStyleAcrossInterpolation()
        {
            var animator = new LineBrushAnimator();
            LineBrush from = MakeLineBrush(0.0, LineStyle.DoubleLine);
            LineBrush to = MakeLineBrush(1.0, LineStyle.DoubleLine);

            var early = (LineBrush)animator.Interpolate(0.25, from, to);
            var late = (LineBrush)animator.Interpolate(0.75, from, to);

            Assert.AreEqual(LineStyle.DoubleLine, early.LineStyle.Top);
            Assert.AreEqual(LineStyle.DoubleLine, late.LineStyle.Top);
        }

        [Test]
        public void NonLineBrushPairFallsBackToDiscreteSwitch()
        {
            var animator = new LineBrushAnimator();
            IBrush solid = Brushes.Red;
            LineBrush line = MakeLineBrush(0.0);

            Assert.AreSame(solid, animator.Interpolate(0.25, solid, line));
            Assert.AreSame(line, animator.Interpolate(0.75, solid, line));
        }

        // Touching LineBrush runs its static constructor, which registers the animator with Avalonia's internal
        // BaseBrushAnimator registry. Verify by reflection that an entry now matches the LineBrush type and that
        // its factory produces a usable animator.
        [Test]
        public void RegistersWithAvaloniaBrushAnimatorRegistry()
        {
            LineBrushAnimator.EnsureRegistered();
            // Ensure the static constructor has run too.
            _ = MakeLineBrush(0.0);

            Assembly avalonia = typeof(InterpolatingAnimator<>).Assembly;
            Type baseBrushAnimator = avalonia.GetType("Avalonia.Animation.Animators.BaseBrushAnimator", true)!;
            FieldInfo listField = baseBrushAnimator.GetField("_brushAnimators",
                BindingFlags.NonPublic | BindingFlags.Static)!;
            var list = (IEnumerable)listField.GetValue(null)!;

            bool matchesLineBrush = false;
            foreach (object entry in list)
            {
                Type tupleType = entry.GetType();
                var match = (Func<Type, bool>)tupleType.GetField("Item1")!.GetValue(entry)!;
                if (match(typeof(LineBrush)))
                {
                    matchesLineBrush = true;

                    // The factory must yield a non-null animator (the wrapper Avalonia drives).
                    var factory = (Delegate)tupleType.GetField("Item3")!.GetValue(entry)!;
                    object animator = factory.DynamicInvoke();
                    Assert.IsNotNull(animator, "Registered factory should produce an animator instance.");
                    break;
                }
            }

            Assert.IsTrue(matchesLineBrush, "Expected a registered brush animator that matches LineBrush.");
        }

        // End-to-end: drive a real Avalonia keyframe animation of Border.BorderBrush (LineBrush keyframes) with a
        // real clock. This proves Avalonia's BaseBrushAnimator actually selects our registered animator and ticks
        // it, not merely that the registration entry exists. Avalonia's clock types are internal, so the concrete
        // ClockBase is created and pulsed by reflection.
        [Test]
        public void AvaloniaDrivesAnimatorForBorderBrushAnimation()
        {
            LineBrushAnimator.EnsureRegistered();

            var border = new Border();
            var animation = new Animation
            {
                Duration = TimeSpan.FromSeconds(1),
                FillMode = FillMode.Both,
                Children =
                {
                    new KeyFrame
                    {
                        Cue = new Cue(0d),
                        Setters = { new Setter(Border.BorderBrushProperty, MakeLineBrush(0.0)) }
                    },
                    new KeyFrame
                    {
                        Cue = new Cue(1d),
                        Setters = { new Setter(Border.BorderBrushProperty, MakeLineBrush(1.0)) }
                    }
                }
            };

            Assembly avalonia = typeof(InterpolatingAnimator<>).Assembly;
            Type clockType = avalonia.GetType("Avalonia.Animation.ClockBase", true)!;
            object clock = Activator.CreateInstance(clockType, true)!;
            MethodInfo pulse = clockType.GetMethod("Pulse",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null,
                new[] { typeof(TimeSpan) }, null)!;

            MethodInfo apply = null;
            foreach (MethodInfo m in typeof(Animation).GetMethods(
                         BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                if (m.Name == "Apply" && m.GetParameters().Length == 4)
                {
                    apply = m;
                    break;
                }

            using var _ = (IDisposable)apply!.Invoke(animation,
                new[] { border, clock, new AlwaysTrueObservable(), null })!;

            Assert.IsNotNull(apply, "apply");
            Assert.IsNotNull(pulse, "pulse");
            Assert.IsNotNull(clock, "clock");
            try
            {
                pulse.Invoke(clock, new object[] { TimeSpan.Zero });
                pulse.Invoke(clock, new object[] { TimeSpan.FromMilliseconds(500) }); // half-way through a 1s animation
            }
            catch (TargetInvocationException tie)
            {
                throw tie.InnerException!;
            }

            var brush = border.BorderBrush as LineBrush;
            Assert.IsNotNull(brush,
                "BorderBrush should be an interpolated LineBrush, proving Avalonia drove our animator.");
            var inner = brush.Brush as ILinearGradientBrush;
            Assert.IsNotNull(inner, "Inner brush should remain a linear gradient.");
            Assert.AreEqual(0.5, inner.StartPoint.Point.X, 0.05,
                "Inner gradient should be interpolated half-way at 50% progress.");
        }

        private sealed class AlwaysTrueObservable : IObservable<bool>
        {
            public IDisposable Subscribe(IObserver<bool> observer)
            {
                observer.OnNext(true);
                return new NoopDisposable();
            }
        }

        private sealed class NoopDisposable : IDisposable
        {
            public void Dispose()
            {
            }
        }
    }
}