// Enhances the verification code input of the static verification pages into six slots, without the WebAssembly runtime.
// The server renders one labelled text input that posts the form on its own; this module keeps that input as the only
// field and draws its value into six presentation slots laid over it, so label, autofill, paste, the keyboard and the error
// association stay on the real input. It upper-cases the value, submits the form once when six characters are entered and
// lets a form submit only once per document, so an auto-submit and a click never post twice. The account API stays
// authoritative for the code, attempts and expiry. The host page loads it once with the nonce; it rescans after every
// enhanced navigation and attaches to an input only once.
const codeLength = 6;
const attached = new WeakSet();
const renderers = new WeakMap();

function attach(root) {
  if (attached.has(root)) return;
  attached.add(root);

  const input = root.querySelector("[data-one-time-password-input]");
  const slotContainer = root.querySelector("[data-one-time-password-slots]");
  const slots = [...root.querySelectorAll("[data-one-time-password-slot]")];
  const form = input.form;
  let hasAutoSubmitted = false;

  const render = () => {
    const isFocused = document.activeElement === input;
    const caret = Math.min(input.selectionStart ?? input.value.length, codeLength - 1);
    slots.forEach((slot, index) => {
      slot.textContent = input.value[index] ?? "";
      slot.classList.toggle("one-time-password-slot-active", isFocused && !input.disabled && index === caret);
    });
  };

  const normalize = () => {
    const normalized = input.value.replace(/\s/g, "").toUpperCase().slice(0, codeLength);
    if (normalized !== input.value) {
      const caret = Math.min(input.selectionStart ?? normalized.length, normalized.length);
      input.value = normalized;
      input.setSelectionRange(caret, caret);
    }
    render();
    return normalized;
  };

  const onInput = () => {
    if (normalize().length === codeLength && !hasAutoSubmitted && form !== null) {
      hasAutoSubmitted = true;
      form.requestSubmit();
    }
  };

  input.addEventListener("input", onInput);
  input.addEventListener("focus", render);
  input.addEventListener("blur", render);
  renderers.set(input, render);
  guardSubmit(form);
  // A document restored by Back from the cache may auto-submit again
  window.addEventListener("pageshow", (event) => {
    if (event.persisted) hasAutoSubmitted = false;
  });

  root.classList.add("one-time-password-enhanced");
  slotContainer.hidden = false;
  // A value already present (a restored document) is shown but never submitted without new input
  normalize();
  if (!input.disabled && document.activeElement !== input) input.focus();
  render();
}

// A static form posts a new document, so a second submit before it arrives would send the same code twice
function guardSubmit(form) {
  if (form === null || attached.has(form)) return;
  attached.add(form);
  form.addEventListener("submit", (event) => {
    if (form.dataset.submitting === "true") {
      event.preventDefault();
      return;
    }
    form.dataset.submitting = "true";
  });
  // A document restored by Back from the cache is a new attempt
  window.addEventListener("pageshow", (event) => {
    if (event.persisted) delete form.dataset.submitting;
  });
}

function start() {
  for (const root of document.querySelectorAll("[data-one-time-password]")) attach(root);
}

// The caret moves with arrow keys, Home, End, a click and Backspace; the active slot follows it
document.addEventListener("selectionchange", () => {
  const render = renderers.get(document.activeElement);
  if (render !== undefined) render();
});

start();
window.Blazor?.addEventListener("enhancedload", start);
