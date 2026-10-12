// Where a link into Settings goes: the phone page, or a group of the list
// (main.js). "settings.html#phone" opens the window on the phone page, any
// other "#id" scrolls the list to that group, and the same ids reach a window
// that is already open as settings:section.

/**
 * The page and section an id ("phone", "shortcuts", "#updates", "") names.
 * `section` is a group of the list page (the page ignores one it does not
 * have), null for none.
 * @returns {{ page: "settings" | "phone", section: string | null }}
 */
export function routeOf(hash) {
  const id = decode(hash.replace(/^#/, ""));
  if (id === "phone") return { page: "phone", section: null };
  return { page: "settings", section: id || null };
}

/** A link is typed by people too: a bad escape is just an id nothing has. */
function decode(text) {
  try {
    return decodeURIComponent(text);
  } catch {
    return text;
  }
}
