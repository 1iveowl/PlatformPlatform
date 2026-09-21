---
paths: blazor/**
description: The accessibility and mobile bar every Blazor surface meets at phone and desktop width in both cultures, the check that enforces each criterion, and the cells only a person on a device can settle
---

# Accessibility and Mobile

The release bar for the Blazor edition. Every surface, public and authenticated, meets all of it at phone width and at desktop width, in en-US and in da-DK. Each criterion below names the check that enforces it, so a criterion is never an opinion in review: it either has a failing case or it does not. The criteria a machine cannot settle are marked manual and are recorded by the device pass, with the device, the assistive technology and the date.

A surface is finished when its checks pass, not when it looks right. A check that cannot run is reported as not run; none is weakened, skipped or narrowed to make a run pass.

## Implementation

1. **An accessible name on every control, dialog and live region.** A control is named by its visible text, by a wrapping or associated `<label>`, or by `aria-label` from the resources in both cultures; a dialog by `aria-label` or `aria-labelledby`; a live region by the element it announces. Checked by `blazor-harness accessibility` (the axe rules `button-name`, `link-name`, `input-button-name`, `aria-command-name`, `aria-dialog-name`, `label`), by `mobile-surfaces` (every text field names its type, its autocomplete and an accessible name) and by `side-pane` and `shell-layout` for the pane and the mobile menu. Manual: that an assistive technology reads the name a user expects, not merely a name.
2. **`aria-invalid` and `aria-describedby` on every invalid field, refused by the browser, by a data annotation or by the account API.** A field renders `@attributes` from `FieldAria.For(() => Input.Field, "field-name")` and its messages through `<FieldValidation FieldName="field-name" For="() => Input.Field"/>`; the form renders `<FormErrorAlert Errors="_formErrors" Id="<form>-error"/>` and the mapper is created with that id, so every field points at its own messages and at the form's alert. Never hand-write the pair, and never describe a field by an element that is not always rendered. Checked by `FieldAriaTests`, by `HostSecurityTests.FormErrors`, by `blazor-harness form-errors` for the static and the interactive fixture, and by `mobile-surfaces` for a refusal inside a dialog.
3. **Focus order and a visible focus ring.** Tab reaches the controls in document order, never lands on the document body and never stalls on one element, and the focused control is always distinguishable from its unfocused state. Checked by `mobile-surfaces` (the keyboard walk on every surface and inside every dialog). Manual: a focus ring that is visible against the brand colours a downstream fork sets.
4. **The focus trap in dialogs and panes.** A modal dialog and a full-screen side pane keep Tab inside themselves and wrap at both ends. Only elements a user can Tab to count: the selector in `wwwroot/js/unsaved-changes.js` excludes `[tabindex="-1"]` on every branch, so the pane's backdrop button, which exists for the pointer, is never a stop. Checked by `StylesheetAccessibilityTests` on the selector, by `shell-layout` for the mobile menu, by `side-pane` for the pane and by `mobile-surfaces` for the pane's backdrop and for every dialog.
5. **Touch targets of at least 44 pixels in both axes.** Every control a finger aims at meets it; a link rendered inline in a sentence is not a control and is left out, as WCAG 2.5.8 does. The minimum is a class rule in `app.css`, never a style attribute. Checked by `mobile-surfaces` (`tapTargets` on every surface, dialog and toast). Manual: a real touch screen, which the automation library cannot press and hold.
6. **200 % zoom without loss of content or function.** No horizontal overflow and no control pushed out of reach at 640 and at 320 CSS pixels, at the largest zoom level the preferences page offers, and with the longer Danish labels. Checked by `mobile-surfaces` (the reflow cases). Manual: the browser's own zoom, as opposed to the viewport emulation those cases use, and the operating system's large-text setting.
7. **Reduced motion honoured.** `app.css` carries one `@media (prefers-reduced-motion: reduce)` block that zeroes every animation, transition and smooth scroll on the document, the component library's included. Checked by `StylesheetAccessibilityTests` on the block and by `mobile-surfaces` (no element computes a duration above zero under the preference).
8. **Contrast of at least 4.5:1 for text, and no information carried by colour alone.** A state that colour shows also has a text, an icon or an attribute a machine can read: a refused field carries `aria-invalid`, a current navigation item `aria-current`, a verification outcome `data-verification-state`. Checked by `blazor-harness accessibility` (`color-contrast` at both widths in both cultures). Manual: contrast after a downstream fork changes the brand tokens, and the meaning of a colour to a person who cannot see it.
9. **A keyboard path for every action.** Every action reachable by pointer is reachable by keyboard, and the list keyboard model in `data-list.js` owns arrows, Home, End, Space, Enter and Escape rather than a handler on a row. Checked by `mobile-surfaces` (the keyboard walk), `data-list`, `side-pane` and `unsaved-changes`.
10. **A field a software keyboard can serve.** Every text field names its `type` and its `autocomplete`, and a field whose value the browser fills computes a font size of at least 16 pixels, because iOS Safari zooms the page when the field it focuses is smaller and moves the rest of the form off screen. Checked by `mobile-surfaces` and by `one-time-password` for the enhanced code input, and by `StylesheetAccessibilityTests` for the stylesheet. Manual: the zoom itself on a device.
11. **Run the bar before the surface is called done**: `build --blazor`, then `test --blazor --no-build`, then `blazor-harness accessibility --browser all`, `blazor-harness mobile-surfaces --browser all` and the harness scripts for the surface. A violation of serious or critical impact fails the scan; a moderate or minor one is recorded in the result and reviewed, because a scanner's severity is not the bar. A violation whose fix would need a change inside the component library is a stop condition: ask, never widen the bar.

## Examples

### Example 1 - A Field That Says It Is Invalid

```razor
@* ✅ DO: one mapping for the control and one element for its messages, both derived from the field's name
   (blazor/Blazor.Host/Components/Pages/Public/Login.razor) *@
<InputText @bind-Value="Input.Email" type="email" autocomplete="email" data-testid="email" @attributes='_fieldAria.For(() => Input.Email, "email")'/>
<FieldValidation FieldName="email" For="() => Input.Email"/>

<FormErrorAlert Errors="_formErrors" Id="login-error"/>

@code {

    protected override void OnInitialized()
    {
        _editContext = new EditContext(Input);
        _formErrors = new FormErrorMapper(_editContext);
        _fieldAria = new FieldAria(_editContext, "login-error");
    }

}
```

```razor
@* ❌ DON'T: a message with no id for the field to point at, so a refusal is announced to nobody *@
<InputText @bind-Value="Input.Email" type="email" data-testid="email"/>
<ValidationMessage For="() => Input.Email"/>

@* ❌ DON'T: hand-written ids, which drift from the element that carries the messages *@
<InputText @bind-Value="Input.Email" aria-invalid="true" aria-describedby="email-error-text"/>
```

### Example 2 - Only What a User Can Tab To

```javascript
// ✅ DO: every branch excludes tabindex="-1" (blazor/Blazor.Client/wwwroot/js/unsaved-changes.js)
const focusableSelector = ["a[href]", "button:not([disabled])", "input:not([disabled])", "select:not([disabled])", "textarea:not([disabled])", "[tabindex]"]
  .map((selector) => `${selector}:not([tabindex='-1'])`)
  .join(", ");

// ❌ DON'T: a branch without it; the side pane's backdrop button becomes the trap's first stop
const focusableSelector = "a[href], button:not([disabled]), [tabindex]:not([tabindex='-1'])";
```

```css
/* ✅ DO: one preference block for the whole document, including the component library's rules (app.css) */
@media (prefers-reduced-motion: reduce) {
    *,
    *::before,
    *::after {
        animation-duration: 0s !important;
        transition-duration: 0s !important;
        scroll-behavior: auto !important;
    }
}

/* ❌ DON'T: a field small enough for iOS to zoom the page when it takes focus */
.one-time-password-enhanced .one-time-password-input {
    font-size: 1px;
}
```
