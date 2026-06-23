// Enables C# 9 'init' accessors and records on netstandard2.0.
// Compiled only for the netstandard2.0 target (see Directory.Build.props).
#if NETSTANDARD2_0
namespace System.Runtime.CompilerServices
{
    using System.ComponentModel;

    /// <summary>Reserved compiler infrastructure to support init-only setters on legacy targets.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    internal static class IsExternalInit
    {
    }
}
#endif
