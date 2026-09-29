using System;
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
        public void InterpolatesSolidColorInsideLineBrushAsBefore()
        {
            var from = new LineBrush { Brush = new SolidColorBrush(Colors.Black) };
            var to = new LineBrush { Brush = new SolidColorBrush(Colors.White) };

            var result = (LineBrush)new LineBrushAnimator().Interpolate(0.5, from, to);

            Assert.AreEqual(Color.FromRgb(128, 128, 128), ((ISolidColorBrush)result.Brush).Color);
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

        [Test]
        public void InterpolatesOrdinarySolidBrushUsingSrgb()
        {
            var from = new SolidColorBrush(Color.FromRgb(0, 0, 0)) { Opacity = 0.2 };
            var to = new SolidColorBrush(Color.FromRgb(255, 255, 255)) { Opacity = 0.8 };

            var result = (ISolidColorBrush)new LineBrushAnimator().Interpolate(0.5, from, to);

            Assert.AreEqual(Color.FromRgb(188, 188, 188), result.Color);
            Assert.AreEqual(0.5, result.Opacity, 1e-6);
        }

        [Test]
        public void InterpolatesOrdinaryGradientAndSolidBrush()
        {
            IBrush gradient = MakeLineBrush(0.0).Brush;
            var solid = new SolidColorBrush(Colors.Green);

            var result = (ILinearGradientBrush)new LineBrushAnimator().Interpolate(0.5, gradient, solid);
            var reversed = (ILinearGradientBrush)new LineBrushAnimator().Interpolate(0.5, solid, gradient);

            Assert.AreEqual(((ILinearGradientBrush)gradient).StartPoint, result.StartPoint);
            Assert.AreEqual(2, result.GradientStops.Count);
            Assert.AreEqual(Color.FromRgb(188, 92, 0), result.GradientStops[0].Color);
            Assert.AreEqual(Color.FromRgb(188, 92, 0), reversed.GradientStops[0].Color);
        }

        [Test]
        public void InterpolatesOrdinaryGradientStopsOfDifferentLengths()
        {
            var from = (LinearGradientBrush)MakeLineBrush(0).Brush;
            var to = (LinearGradientBrush)MakeLineBrush(1).Brush;
            to.GradientStops.Add(new GradientStop(Colors.Green, 0.75));

            var result = (ILinearGradientBrush)new LineBrushAnimator().Interpolate(0.5, from, to);

            Assert.AreEqual(0.5, result.StartPoint.Point.X, 1e-6);
            Assert.AreEqual(3, result.GradientStops.Count);
            Assert.AreEqual(0.875, result.GradientStops[2].Offset, 1e-6);
        }

        [Test]
        public void EmptyGradientStopsSwitchDiscretely()
        {
            var from = new LinearGradientBrush();
            var to = new LinearGradientBrush { GradientStops = { new GradientStop(Colors.Red, 0) } };
            var animator = new LineBrushAnimator();

            Assert.AreSame(from, animator.Interpolate(0.25, from, to));
            Assert.AreSame(to, animator.Interpolate(0.75, from, to));
        }

        [Test]
        public void InterpolatesRadialAndConicGradientGeometry()
        {
            var fromRadial = new RadialGradientBrush
            {
                Center = new RelativePoint(0, 0, RelativeUnit.Relative),
                RadiusX = new RelativeScalar(0.2, RelativeUnit.Relative),
                GradientStops = { new GradientStop(Colors.Red, 0) }
            };
            var toRadial = new RadialGradientBrush
            {
                Center = new RelativePoint(1, 1, RelativeUnit.Relative),
                RadiusX = new RelativeScalar(0.8, RelativeUnit.Relative),
                GradientStops = { new GradientStop(Colors.Blue, 1) }
            };
            var fromConic = new ConicGradientBrush
            {
                Angle = 30,
                GradientStops = { new GradientStop(Colors.Red, 0) }
            };
            var toConic = new ConicGradientBrush
            {
                Angle = 90,
                GradientStops = { new GradientStop(Colors.Blue, 1) }
            };
            var animator = new LineBrushAnimator();

            var radial = (IRadialGradientBrush)animator.Interpolate(0.5, fromRadial, toRadial);
            var conic = (IConicGradientBrush)animator.Interpolate(0.5, fromConic, toConic);

            Assert.AreEqual(0.5, radial.Center.Point.X, 1e-6);
            Assert.AreEqual(0.5, radial.RadiusX.Scalar, 1e-6);
            Assert.AreEqual(60, conic.Angle, 1e-6);
        }

        [Test]
        public void AvaloniaDrivesAnimatorForBorderBrushAnimation()
        {
            var brush = AnimateBorderBrush(MakeLineBrush(0.0), MakeLineBrush(1.0)) as LineBrush;

            Assert.IsNotNull(brush, "BorderBrush should be an interpolated LineBrush.");
            var inner = brush.Brush as ILinearGradientBrush;
            Assert.IsNotNull(inner, "Inner brush should remain a linear gradient.");
            Assert.AreEqual(0.5, inner.StartPoint.Point.X, 0.05);
        }

        [Test]
        public void AvaloniaDrivesAnimatorForOrdinarySolidBrushAnimation()
        {
            LineBrushAnimator.EnsureRegistered();
            LineBrushAnimator.EnsureRegistered();

            var result = AnimateBorderBrush(new SolidColorBrush(Colors.Black),
                new SolidColorBrush(Colors.White)) as ISolidColorBrush;

            Assert.IsNotNull(result);
            Assert.AreEqual(Color.FromRgb(188, 188, 188), result.Color);
        }

        // Avalonia's clock implementation is internal; only the test harness uses reflection to pulse it.
        private static IBrush AnimateBorderBrush(IBrush from, IBrush to)
        {
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
                        Setters = { new Setter(Border.BorderBrushProperty, from) }
                    },
                    new KeyFrame
                    {
                        Cue = new Cue(1d),
                        Setters = { new Setter(Border.BorderBrushProperty, to) }
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
                if (m.Name == "Apply" && m.GetParameters().Length == 5)
                {
                    apply = m;
                    break;
                }

            using var _ = (IDisposable)apply!.Invoke(animation,
                new[] { border, clock, new AlwaysTrueObservable(), null, false })!;

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

            return border.BorderBrush;
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