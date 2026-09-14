namespace Blazor.Host.Shell;

// Marks a page that hosts a WebAssembly component. Only such pages render the runtime preload links, so the static
// server-rendered public surface starts no WebAssembly download.
[AttributeUsage(AttributeTargets.Class)]
public sealed class InteractiveSurfaceAttribute : Attribute;
