using System.Reflection;
using Avalonia.Media;

namespace Consolonia.Controls.Brushes
{
    internal static class BrushExtensions
    {
        private static readonly MethodInfo ToImmutableMethod = typeof(IBrush).Assembly
            .GetType("Avalonia.Media.IMutableBrush")!
            .GetMethod("ToImmutable", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;

        public static IBrush ToImmutable(this IBrush brush)
        {
            if (brush is null) return null;
            if (brush is IImmutableBrush immutable) return immutable;
            return (IBrush)ToImmutableMethod.Invoke(brush, null)!;
        }
    }
}
