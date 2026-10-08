// The hero page's only script: ?theme=light|dark forces the desktop and the
// board into one theme (default: follow the system), before anything draws;
// ?paper= picks a note paper set.
// ?card turns the page into the GitHub social preview (docs/assets/
// social-preview.png): no taskbar, the headline under the board and the app
// icon by the name. The renderer sets the icon (window.setCardIcon) as a
// data URL, since docs/ is not served here.
const params = new URLSearchParams(location.search);
const theme = params.get("theme");
if (theme === "light" || theme === "dark") document.documentElement.dataset.theme = theme;

// ?paper=classic|card|sticky: the note paper set (styles/notes/), over the
// one tokens.css picks, for comparing them on the board.
const paper = params.get("paper");
if (["classic", "card", "sticky"].includes(paper)) {
  const link = document.createElement("link");
  link.rel = "stylesheet";
  link.href = `../styles/notes/${paper}.css`;
  document.head.appendChild(link);
}

if (params.has("card")) {
  document.documentElement.classList.add("card");
  // The card's lettering is Roboto Serif, Tack's brand face for images (the
  // app itself keeps the system font). Fetched only when rendering the card.
  const font = document.createElement("link");
  font.rel = "stylesheet";
  font.href = "https://fonts.googleapis.com/css2?family=Roboto+Serif:ital,opsz,wdth,wght@0,8..144,50..150,100..900;1,8..144,50..150,100..900&display=block";
  document.head.appendChild(font);
  const card = document.createElement("div");
  card.id = "card";
  card.setAttribute("aria-hidden", "true");
  card.innerHTML = `
    <h1>Snip it. It’s tacked.</h1>
    <p class="sub">The top of your screen, finally useful.</p>
    <p class="brand"><img alt=""><b>Tack</b>&nbsp;for Windows</p>`;
  document.body.appendChild(card);
  window.setCardIcon = (src) => new Promise((resolve) => {
    const img = card.querySelector("img");
    img.onload = resolve;
    img.src = src;
  });
}
