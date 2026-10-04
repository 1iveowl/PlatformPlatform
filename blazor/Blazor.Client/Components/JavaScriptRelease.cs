using Microsoft.JSInterop;

namespace Blazor.Client.Components;

// Releases what a wrapper acquired from a JavaScript module in wwwroot/js. A document that is already gone (a full document
// navigation leaving the interactive surface) took the listeners with it, so that one failure is expected and ignored; any
// other failure reaches the caller, after the steps that follow it have still run.
public static class JavaScriptRelease
{
    // Calls the handle's dispose, which removes the listeners it attached, and then releases the handle itself
    public static async ValueTask HandleAsync(IJSObjectReference handle)
    {
        try
        {
            await handle.InvokeVoidAsync("dispose");
        }
        catch (JSDisconnectedException)
        {
            // The document is already gone, and the listeners the handle attached went with it
        }
        finally
        {
            await ReferenceAsync(handle);
        }
    }

    // Releases a module or a handle on the .NET side and in the browser
    public static async ValueTask ReferenceAsync(IJSObjectReference reference)
    {
        try
        {
            await reference.DisposeAsync();
        }
        catch (JSDisconnectedException)
        {
            // The document is already gone, and the object with it
        }
    }
}
