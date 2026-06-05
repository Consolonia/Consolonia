using System;
using System.Linq.Expressions;
using System.Reflection;
using Avalonia.Animation;
using Avalonia.Logging;
using Avalonia.Media;
using Avalonia.Media.Immutable;

// ReSharper disable CheckNamespace
namespace Consolonia.Controls.Brushes
{
    /// <summary>
    ///     Animates a <see cref="LineBrush" /> by interpolating its inner brush between keyframes.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Avalonia animates any <see cref="IBrush" /> property through its internal <c>BaseBrushAnimator</c>,
    ///         which only knows how to interpolate solid color and gradient brushes. A <see cref="LineBrush" /> is
    ///         neither, so an animated <c>BorderBrush</c> that uses one would snap between keyframes instead of
    ///         interpolating.
    ///     </para>
    ///     <para>
    ///         The intended extension point is <c>BaseBrushAnimator.RegisterBrushAnimator&lt;TAnimator&gt;</c>, which
    ///         registers a per-brush-type animator. Unfortunately its constraint (<c>where TAnimator : IAnimator</c>)
    ///         references types that Avalonia keeps internal, so it cannot be called from outside the framework. We
    ///         therefore register into the same private registry by reflection, supplying an animator built on the
    ///         public <see cref="InterpolatingAnimator{T}" /> base. Matching is by brush type, so other brushes keep
    ///         using Avalonia's own animators untouched.
    ///     </para>
    /// </remarks>
    public sealed class LineBrushAnimator : InterpolatingAnimator<IBrush>
    {
        // Reflected handle to Avalonia's internal GradientBrushAnimator.Interpolate, used to animate a wrapped
        // gradient exactly the way Avalonia animates an unwrapped one.
        private static readonly Func<double, IGradientBrush, IGradientBrush, IGradientBrush> GradientInterpolator =
            CreateGradientInterpolator();

        private static readonly object RegistrationGate = new();
        private static bool _registered;

        /// <summary>
        ///     Registers the animator with Avalonia so that <see cref="LineBrush" /> values animate. Safe to call
        ///     multiple times; only the first call has an effect, and any failure is logged rather than thrown.
        /// </summary>
        public static void EnsureRegistered()
        {
            lock (RegistrationGate)
            {
                if (_registered)
                    return;
                _registered = true;
            }

            try
            {
                Register();
            }
#pragma warning disable CA1031 // registration touches Avalonia internals reflectively; never fail app startup
            catch (Exception e)
#pragma warning restore CA1031
            {
                Logger.TryGet(LogEventLevel.Warning, LogArea.Animations)?.Log(null,
                    "Failed to register {Animator}: {Message}", nameof(LineBrushAnimator), e.Message);
            }
        }

        public override IBrush Interpolate(double progress, IBrush oldValue, IBrush newValue)
        {
            if (oldValue is not LineBrush oldLine || newValue is not LineBrush newLine)
                // Not a pair of LineBrushes (e.g. animating to/from a plain brush): fall back to a discrete switch.
                return progress >= 0.5 ? newValue : oldValue;

            IBrush interpolatedBrush = InterpolateInner(progress, oldLine.Brush, newLine.Brush);

            // The LineStyle selects glyphs and cannot be interpolated, so switch it at the halfway point.
            LineStyles lineStyle = progress >= 0.5 ? newLine.LineStyle : oldLine.LineStyle;

            // Allocates a LineBrush per animation frame; acceptable for a terminal UI's frame rate.
            return new LineBrush
            {
                Brush = interpolatedBrush,
                LineStyle = lineStyle
            };
        }

        private static IBrush InterpolateInner(double progress, IBrush oldInner, IBrush newInner)
        {
            if (oldInner is IGradientBrush oldGradient && newInner is IGradientBrush newGradient &&
                GradientInterpolator != null)
                return GradientInterpolator(progress, oldGradient, newGradient);

            if (oldInner is ISolidColorBrush oldSolid && newInner is ISolidColorBrush newSolid)
                return new ImmutableSolidColorBrush(InterpolateColor(progress, oldSolid.Color, newSolid.Color));

            // Mixed or unsupported inner brushes (or gradient animation unavailable): fall back to a discrete switch.
            return progress >= 0.5 ? newInner : oldInner;
        }

        private static Color InterpolateColor(double progress, Color from, Color to)
        {
            return Color.FromArgb(
                Lerp(from.A, to.A, progress),
                Lerp(from.R, to.R, progress),
                Lerp(from.G, to.G, progress),
                Lerp(from.B, to.B, progress));
        }

        private static byte Lerp(byte from, byte to, double progress)
        {
            return (byte)Math.Round(from + (to - from) * progress);
        }

        /// <summary>
        ///     Inserts a (matches LineBrush, build a wrapped LineBrushAnimator) entry into Avalonia's internal
        ///     <c>BaseBrushAnimator</c> brush-animator registry.
        /// </summary>
        private static void Register()
        {
            Assembly avalonia = typeof(InterpolatingAnimator<>).Assembly;
            Type baseBrushAnimator = avalonia.GetType("Avalonia.Animation.Animators.BaseBrushAnimator", true)!;

            FieldInfo listField = baseBrushAnimator.GetField("_brushAnimators",
                                      BindingFlags.NonPublic | BindingFlags.Static)
                                  ?? throw new MissingFieldException(baseBrushAnimator.FullName, "_brushAnimators");

            object list = listField.GetValue(null)!;
            Type listType = listField.FieldType; // List<(Func<Type,bool> Match, Type AnimatorType, Func<IAnimator> Factory)>
            Type tupleType = listType.GetGenericArguments()[0];
            Type funcIAnimatorType = tupleType.GetGenericArguments()[2]; // Func<IAnimator>
            Type iAnimatorType = funcIAnimatorType.GetGenericArguments()[0]; // internal IAnimator

            Func<Type, bool> match = type => typeof(LineBrush).IsAssignableFrom(type);

            // Build a Func<IAnimator> factory whose return type (IAnimator) we cannot name, via an expression tree.
            MethodInfo create = typeof(LineBrushAnimator).GetMethod(nameof(CreateWrapperInstance),
                BindingFlags.NonPublic | BindingFlags.Static)!;
            Delegate factory = Expression
                .Lambda(funcIAnimatorType, Expression.Convert(Expression.Call(create), iAnimatorType))
                .Compile();

            object entry = Activator.CreateInstance(tupleType, match, typeof(LineBrushAnimator), factory)!;

            // Insert at the front so a LineBrush is matched before the solid/gradient fallbacks.
            listType.GetMethod("Insert")!.Invoke(list, new[] { 0, entry });
        }

        // Avalonia drives a registered brush animator through the IAnimator returned by ICustomAnimator.CreateWrapper.
        // CreateWrapper's return type (IAnimator) is internal, so it is invoked reflectively and surfaced as object.
        private static readonly MethodInfo CreateWrapperMethod =
            typeof(ICustomAnimator).GetMethod("CreateWrapper",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)!;

        private static object CreateWrapperInstance()
        {
            return CreateWrapperMethod.Invoke(new LineBrushAnimator(), null)!;
        }

        private static Func<double, IGradientBrush, IGradientBrush, IGradientBrush> CreateGradientInterpolator()
        {
            try
            {
                Assembly avalonia = typeof(InterpolatingAnimator<>).Assembly;
                Type gradientAnimator = avalonia.GetType("Avalonia.Animation.Animators.GradientBrushAnimator", true)!;
                object instance = Activator.CreateInstance(gradientAnimator, true)!;
                MethodInfo interpolate = gradientAnimator.GetMethod("Interpolate",
                    new[] { typeof(double), typeof(IGradientBrush), typeof(IGradientBrush) })!;

                return (Func<double, IGradientBrush, IGradientBrush, IGradientBrush>)Delegate.CreateDelegate(
                    typeof(Func<double, IGradientBrush, IGradientBrush, IGradientBrush>), instance, interpolate);
            }
#pragma warning disable CA1031 // binding to an Avalonia internal; degrade to discrete switching on failure
            catch (Exception e)
#pragma warning restore CA1031
            {
                Logger.TryGet(LogEventLevel.Warning, LogArea.Animations)?.Log(null,
                    "{Animator} could not bind the gradient interpolator: {Message}", nameof(LineBrushAnimator),
                    e.Message);
                return null;
            }
        }
    }
}
