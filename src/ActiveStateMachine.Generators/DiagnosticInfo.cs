using System;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace ActiveStateMachine.Generators
{
    /// <summary>Cache-friendly, equatable representation of a source location.</summary>
    internal readonly record struct LocationInfo(string FilePath, TextSpan TextSpan, LinePositionSpan LineSpan)
    {
        public Location ToLocation() => Location.Create(FilePath, TextSpan, LineSpan);

        public static LocationInfo? From(SyntaxNode? node)
        {
            if (node is null)
            {
                return null;
            }

            Location location = node.GetLocation();
            if (location.SourceTree is null)
            {
                return null;
            }

            return new LocationInfo(
                location.SourceTree.FilePath,
                location.SourceSpan,
                location.GetLineSpan().Span);
        }
    }

    /// <summary>Cache-friendly, equatable diagnostic captured in the pipeline transform.</summary>
    internal sealed record DiagnosticInfo(
        DiagnosticDescriptor Descriptor,
        LocationInfo? Location,
        EquatableArray<string> MessageArgs)
    {
        public Diagnostic ToDiagnostic()
        {
            var args = new object[MessageArgs.Count];
            for (int i = 0; i < MessageArgs.Count; i++)
            {
                args[i] = MessageArgs[i];
            }

            return Diagnostic.Create(Descriptor, Location?.ToLocation(), args);
        }

        public static DiagnosticInfo Create(DiagnosticDescriptor descriptor, SyntaxNode? node, params string[] args)
            => new(descriptor, LocationInfo.From(node), new EquatableArray<string>(args));
    }
}
