// The keyboard side of a radio group built from buttons (role="radio" inside
// role="radiogroup"), as in the ARIA authoring practices: the group is one
// tab stop (the checked radio, or the first), and the arrow keys, Home and End
// move to a radio and choose it.

const KEYS = { ArrowRight: 1, ArrowDown: 1, ArrowLeft: -1, ArrowUp: -1 };

const radiosOf = (group) => [...group.querySelectorAll('[role="radio"]')];

/**
 * Makes `group` keyboard operable. A radio with `data-manual` (one that
 * opens a picker) is focused by the arrows but only chosen by Enter or Space.
 */
export function handleRadioKeys(group) {
  group.addEventListener("keydown", (e) => {
    const radios = radiosOf(group);
    const at = radios.indexOf(document.activeElement);
    if (at < 0 || e.ctrlKey || e.altKey || e.metaKey) return;
    let to = -1;
    if (e.key in KEYS) to = (at + KEYS[e.key] + radios.length) % radios.length;
    else if (e.key === "Home") to = 0;
    else if (e.key === "End") to = radios.length - 1;
    if (to < 0) return;
    e.preventDefault();
    radios[to].focus();
    if (!("manual" in radios[to].dataset)) radios[to].click();
  });
}

/** Checks the radio whose `data-value` is `value`, and makes it (or the first) the group's tab stop. */
export function showRadio(group, value) {
  const radios = radiosOf(group);
  const checked = radios.find((r) => r.dataset.value === value);
  for (const r of radios) {
    r.setAttribute("aria-checked", String(r === checked));
    r.tabIndex = r === (checked || radios[0]) ? 0 : -1;
  }
}
