// The hero page's only script: ?theme=light|dark forces the desktop and the
// board into one theme (default: follow the system), before anything draws.
// ?card turns the page into the GitHub social preview (docs/assets/
// social-preview.png): no taskbar, the headline under the board and the app
// icon by the name. The renderer sets the icon (window.setCardIcon) as a
// data URL, since docs/ is not served here.
const params = new URLSearchParams(location.search);
const theme = params.get("theme");
if (theme === "light" || theme === "dark") document.documentElement.dataset.theme = theme;

if (params.has("card")) {
  document.documentElement.classList.add("card");
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
