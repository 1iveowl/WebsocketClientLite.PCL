// The two types the C# 15 compiler needs to declare a union. .NET 11 has them; for the
// earlier targets they are declared here, internal, as IsExternalInit is in
// CompilerService.cs. They have the shape of the .NET 11 types. The test project reads the
// library's unions through InternalsVisibleTo on a .NET 11 host and sees the .NET 11
// types and these side by side; the compiler accepts that and matches the cases correctly.

#if !NET11_0_OR_GREATER

using System.ComponentModel;

// ReSharper disable once CheckNamespace
namespace System.Runtime.CompilerServices;

/// <summary>Reserved for the compiler: the value of a union.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
internal interface IUnion
{
    object? Value { get; }
}

/// <summary>Reserved for the compiler: marks a type declared with <c>union</c>.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
internal sealed class UnionAttribute : Attribute
{
}

#endif
