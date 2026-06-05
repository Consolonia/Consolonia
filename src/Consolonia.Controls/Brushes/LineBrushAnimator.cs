using System;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading;
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

#pragma warning disable CA1031
        /// <summary>
        ///     Registers the animator with Avalonia so that <see cref="LineBrush" /> values animate. Safe to call
        ///     multiple times; only the first call has an effect, and any failure is logged rather than thrown.
        /// </summary>
        public static void EnsureRegistered()
        {
            lock (RegistrationGate)
            {
                if (!_registered)
                {
                    try
                    {
                        Register();
                        Thread.MemoryBarrier();
                        _registered = true;
                    }
                    catch (Exception e)
                    {
                        Logger.TryGet(LogEventLevel.Warning, LogArea.Animations)?.Log(
                            null,
                            "Failed to register {Animator}: {Message}",
                            nameof(LineBrushAnimator),
                            e.Message);
                    }
                }
            }
        }
#pragma warning restore CA1031

        /// <summary>
        ///     Interpolates between two <see cref="LineBrush" /> keyframe values by interpolating their inner brushes
        ///     and switching the (non-interpolatable) line style at the halfway point.
        /// </summary>
        /// <param name="progress">The animation progress, from 0 (<paramref name="oldValue" />) to 1 (<paramref name="newValue" />).</param>
        /// <param name="oldValue">The value being animated from.</param>
        /// <param name="newValue">The value being animated to.</param>
        /// <returns>
        ///     An interpolated <see cref="LineBrush" />, or a discrete switch between the two values when they are not
        ///     both <see cref="LineBrush" /> instances.
        /// </returns>
        public override IBrush Interpolate(double progress, IBrush oldValue, IBrush newValue)
        {
            if (oldValue is not LineBrush oldLine || newValue is not LineBrush newLine)
            {
                // Not a pair of LineBrushes (e.g. animating to/from a plain brush): fall back to a discrete switch.
                return progress >= 0.5 ? newValue : oldValue;
            }

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

        /// <summary>
        ///     Interpolates the brush wrapped by a <see cref="LineBrush" />: gradients via Avalonia's own gradient
        ///     animator, solid colors via channel-wise interpolation, and anything else via a discrete switch.
        /// </summary>
        /// <param name="progress">The animation progress, from 0 to 1.</param>
        /// <param name="oldInner">The inner brush being animated from.</param>
        /// <param name="newInner">The inner brush being animated to.</param>
        /// <returns>The interpolated inner brush.</returns>
        private static IBrush InterpolateInner(double progress, IBrush oldInner, IBrush newInner)
        {
            if (oldInner is IGradientBrush oldGradient &&
                newInner is IGradientBrush newGradient &&
                GradientInterpolator is {} interpolator)
            {
                return interpolator(progress, oldGradient, newGradient);
            }

            if (oldInner is ISolidColorBrush oldSolid && newInner is ISolidColorBrush newSolid)
                return new ImmutableSolidColorBrush(InterpolateColor(progress, oldSolid.Color, newSolid.Color));

            // Mixed or unsupported inner brushes (or gradient animation unavailable): fall back to a discrete switch.
            return progress >= 0.5 ? newInner : oldInner;
        }

        /// <summary>
        ///     Linearly interpolates each ARGB channel between two colors.
        /// </summary>
        /// <param name="progress">The animation progress, from 0 (<paramref name="from" />) to 1 (<paramref name="to" />).</param>
        /// <param name="from">The color being animated from.</param>
        /// <param name="to">The color being animated to.</param>
        /// <returns>The interpolated color.</returns>
        private static Color InterpolateColor(double progress, Color from, Color to)
        {
            return Color.FromArgb(
                Lerp(from.A, to.A, progress),
                Lerp(from.R, to.R, progress),
                Lerp(from.G, to.G, progress),
                Lerp(from.B, to.B, progress));
        }

        /// <summary>
        ///     Linearly interpolates a single byte channel and rounds to the nearest value.
        /// </summary>
        /// <param name="from">The channel value being animated from.</param>
        /// <param name="to">The channel value being animated to.</param>
        /// <param name="progress">The animation progress, from 0 to 1.</param>
        /// <returns>The interpolated channel value.</returns>
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

        /// <summary>
        ///     Creates the <c>IAnimator</c> wrapper that Avalonia drives, returned as <see cref="object" /> because its
        ///     type is internal to Avalonia. Used as the factory for the registry entry built in <see cref="Register" />.
        /// </summary>
        /// <returns>A fresh animator wrapper instance.</returns>
        private static object CreateWrapperInstance()
        {
            return CreateWrapperMethod.Invoke(new LineBrushAnimator(), null)!;
        }

        /// <summary>
        ///     Binds a delegate to Avalonia's internal <c>GradientBrushAnimator.Interpolate</c> so a wrapped gradient
        ///     animates exactly as an unwrapped one.
        /// </summary>
        /// <returns>
        ///     The bound interpolator, or <c>null</c> if it cannot be resolved (in which case gradient inner brushes
        ///     fall back to a discrete switch).
        /// </returns>
#pragma warning disable CA1031 // binding to an Avalonia internal; degrade to discrete switching on failure
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
            catch (Exception e)
            {
                Logger.TryGet(LogEventLevel.Warning, LogArea.Animations)?.Log(null,
                    "{Animator} could not bind the gradient interpolator: {Message}", nameof(LineBrushAnimator),
                    e.Message);
                return null;
            }
#pragma warning restore CA1031
        }
    }
}
