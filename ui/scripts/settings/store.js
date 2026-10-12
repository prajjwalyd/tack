// A tiny observable value. The Settings page keeps the latest SettingsView
// and UpdateStatus in one each; the sections subscribe and render from it.

export function createStore() {
  let value = null;
  const subscribers = new Set();
  return {
    get: () => value,
    /** Takes a new value and tells everyone; null (a failed command) is ignored. */
    set(next) {
      if (next == null) return;
      value = next;
      for (const fn of subscribers) fn(value);
    },
    /** Calls `fn` with each new value, and now if there is one. */
    subscribe(fn) {
      subscribers.add(fn);
      if (value != null) fn(value);
    },
  };
}
