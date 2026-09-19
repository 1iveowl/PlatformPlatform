namespace Blazor.Host.Shell;

// Marks a page that hosts a WebAssembly component. Only such pages render the runtime preload links and carry the
// data-interactive-surface attribute that makes wwwroot/js/Blazor.Host.lib.module.js load the component library's browser
// bundle, so the static server-rendered public surface downloads neither the runtime nor that bundle.
[AttributeUsage(AttributeTargets.Class)]
public sealed class InteractiveSurfaceAttribute : Attribute;
