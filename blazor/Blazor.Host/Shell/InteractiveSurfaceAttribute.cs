// Spike code (Blazor edition, stage B2): marks a page that hosts a WebAssembly component. Only such pages render the
// runtime preload links, so the static SSR public surface starts no WebAssembly download.

namespace Blazor.Host.Shell;

[AttributeUsage(AttributeTargets.Class)]
public sealed class InteractiveSurfaceAttribute : Attribute;
