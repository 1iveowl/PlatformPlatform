---
paths: blazor/**/*.razor,blazor/**/*.js,blazor/**/*.css,blazor/**/Shell/*.cs
description: Rules for keeping Blazor markup, scripts, styles and components inside the nonce-only content security policy the host sends
---

# Content Security Policy

The Blazor host sends the React edition's policy with `'wasm-unsafe-eval'` added: scripts and stylesheets need a per-request nonce or a trusted host, there is no `'unsafe-inline'` anywhere, no `style-src-attr` directive, `base-uri 'none'` and `worker-src 'self'`. Every component, module and stylesheet is written so the policy never has to widen; `ShellTests` and the `shell-policy` harness fail when it does.

## Implementation

1. Put the request nonce on every script and stylesheet element the host page renders: the stylesheet links, the `<ImportMap>` and the three scripts in `App.razor` (the third, `js/verification-timer.js`, serves the static verification pages, which have no runtime to import a module) carry `nonce="@_nonce"`, read with `HostShell.GetNonce(HttpContext)`. Preload links are URL-checked and take no nonce. No other page or component renders a script or stylesheet element; `Development/PolicyProbe.razor` is the one exception, which exists to prove the policy blocks what it should.
2. Keep the nonced import map as the one inline element: `<ImportMap ImportMapDefinition="_importMap" nonce="@_nonce"/>` is what lets enhanced navigation start the runtime. It is not a style block, and `BuildContentSecurityPolicy` keeps `'nonce-{nonce}'` on `script-src` and `script-src-elem` for it.
3. Never render an inline `<style>` block, in `<head>` or anywhere else: enhanced navigation re-inserts inline head elements from the new document with a nonce the governing header does not carry. Brand values are served as the external `/brand.css`, versioned by content, and everything else is a class in `wwwroot/app.css` or a scoped `.razor.css`.
4. Never write a `style` attribute, from markup or from script, and never add `style-src-attr 'unsafe-inline'` to the policy; `ShellTests.BuildContentSecurityPolicy_ShouldHaveNoUnsafeSourceAndTheWorkerDirective` asserts the policy contains no `style-src-attr`. Wrap or avoid a component library component that writes style attributes: the toast provider and the dialog are replaced by the in-house `ToastRegion` and `ModalDialog` on the native `<dialog>`, and the data grid by QuickGrid inside `DataList`. Check a component new to the edition in the browser harness (`securitypolicyviolation` events must be 0) before relying on it.
5. Never use an inline event handler attribute (`onclick="..."`) or a `javascript:` URL. Use `@onclick` and the other Blazor event attributes, or a JavaScript module that attaches listeners with `addEventListener` and removes them on dispose.
6. Load browser code only as same-origin modules from `wwwroot/js/` through the import map (`import` of `./js/<name>.js`); they run under the trusted host list without a nonce. Never load a script from a host outside the policy and never inject script text.
7. Build every URL root-absolute with `AppUrls.ToAbsolute`, because `base-uri 'none'` makes the browser ignore `<base href>` and the document URL becomes the base for anything relative.
8. Change a directive in one place, `HostShell.BuildContentSecurityPolicy`, with the reason in its comment, then update `ShellTests` and run `blazor-harness shell-policy --browser all` in Development and with `--environment production`.

## Examples

### Example 1 - The Host Page

```razor
@* ✅ DO: nonce on the stylesheet links, the import map and the scripts, nothing inline (blazor/Blazor.Host/Components/App.razor) *@
<link rel="stylesheet" href="@Shell.BrandStylesheetUrl" nonce="@_nonce"/>
<link rel="stylesheet" href="@AppUrls.ToAbsolute(Assets["app.css"])" nonce="@_nonce"/>
<ImportMap ImportMapDefinition="_importMap" nonce="@_nonce"/>
<HeadOutlet/>
</head>

<body>
<Routes/>
<script src="@AppUrls.ToAbsolute(Assets["js/document-base-uri.js"])" data-base="@AppUrls.PathBase/" nonce="@_nonce"></script>
<script src="@AppUrls.ToAbsolute(Assets["_framework/blazor.web.js"])" nonce="@_nonce"></script>
</body>

@* ❌ DON'T: an inline brand block, which enhanced navigation re-inserts with a stale nonce *@
<style nonce="@_nonce">:root { --brand-primary: @Shell.Brand.PrimaryColorLight; }</style>

@* ❌ DON'T: a script without the nonce, or an inline handler; both are blocked and the handler never runs *@
<script>window.appReady = true;</script>
<button onclick="dismiss()">Close</button>
```

### Example 2 - A Component Styled by Classes Only

```razor
@* ✅ DO: every state is a class in app.css; the text renders as text (blazor/Blazor.Client/Forms/ToastRegion.razor) *@
<div @key="toast.Id" role="alert" class="toast @(toast.Kind == ToastKind.Error ? "toast-error" : "toast-warning")" data-testid="@toast.TestId">
    <p class="toast-title" data-testid="toast-title">@toast.Title</p>
    <button type="button" class="toast-dismiss" aria-label="@CommonStrings.DismissNotification" @onclick="() => Toasts.Dismiss(toast.Id)">×</button>
</div>

@* ❌ DON'T: a style attribute, from markup or from the component library's provider *@
<div role="alert" style="position: fixed; bottom: 1rem; z-index: 999">@toast.Title</div>
<FluentToastProvider/>
```

### Example 3 - Browser Code as a Module

```javascript
// ✅ DO: a same-origin module with addEventListener and a dispose (blazor/Blazor.Client/wwwroot/js/data-list.js)
export function attach(root, dotNet, options) {
  ...
  root.addEventListener("keydown", onKeyDown);
  root.addEventListener("click", onClick);
  return {
    ...
    dispose: () => {
      root.removeEventListener("keydown", onKeyDown);
      root.removeEventListener("click", onClick);
    }
  };
}

// ❌ DON'T: script text or a foreign host; both are blocked by script-src-elem
document.head.appendChild(Object.assign(document.createElement("script"), { textContent: "..." }));
import("https://cdn.example.com/grid.js");
```
