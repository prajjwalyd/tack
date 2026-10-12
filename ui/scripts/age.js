// How long ago a moment was, as the board's captions, the screen-reader
// labels and the Settings window say it. Pure, with no imports.

const relative = new Intl.RelativeTimeFormat(undefined, { numeric: "auto" });
const midnight = (d) => new Date(d.getFullYear(), d.getMonth(), d.getDate());

/**
 * "just now", "5 minutes ago", "3 hours ago" (anything under a day), "yesterday", the weekday within
 * the week, then the date (with the year when it is not this year). `on`
 * goes before a weekday or date ("on Tuesday"); `month` is the date's month
 * style ("short" or "long").
 */
export function ago(t, { now = Date.now(), on = "", month = "short" } = {}) {
  const s = Math.max(0, now - t) / 1000;
  if (s < 45) return "just now";
  const min = Math.max(1, Math.round(s / 60));
  if (min < 60) return relative.format(-min, "minute");
  if (min < 24 * 60) return relative.format(-Math.floor(min / 60), "hour");
  const a = new Date(t), b = new Date(now);
  const days = Math.round((midnight(b) - midnight(a)) / 86400000);
  if (days === 1) return relative.format(-1, "day");
  if (days < 7) return `${on}${a.toLocaleDateString(undefined, { weekday: "long" })}`;
  return `${on}${a.toLocaleDateString(undefined, { day: "numeric", month, year: a.getFullYear() === b.getFullYear() ? undefined : "numeric" })}`;
}
