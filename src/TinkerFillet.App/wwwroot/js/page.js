// The few things the page needs from the browser that Blazor has no API for.

/**
 * Focuses an element and selects what is in it.
 *
 * Blazor can focus an element on its own but cannot select its contents, and a
 * field that opens holding the previous value needs both: without the
 * selection the next keystroke lands behind that value instead of replacing
 * it.
 */
export function focusAndSelect(selector) {
  const element = document.querySelector(selector);
  if (!element) return;

  element.focus();
  element.select?.();
}
