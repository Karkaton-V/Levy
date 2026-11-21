// Builds the Levy logo concepts: one SVG per concept (ink colour baked in) and index.html, which previews them on light and dark.
// The L is a levee in cross-section: a sloped water-side face, a flat crown, and a foot that runs under "evy" to shelter it. Run: node build-logos.js
const fs = require("fs");
const path = require("path");

const INK = "#1C2433";
const WATER = "#2563EB";
const FONT = "Segoe UI, Inter, Helvetica Neue, Arial, sans-serif";

// The levee L shared by the wordmarks: water-side slope from (70,150) up to (100,40), crown to 132, foot out to 206.
const leveeL = (ink) => `<path fill="${ink}" d="M70 150 L100 40 L132 40 L132 118 L206 118 L206 150 Z"/>`;
const word = (ink, weight = 700) =>
  `<text x="214" y="150" font-family="${FONT}" font-weight="${weight}" font-size="150" letter-spacing="-4" fill="${ink}">evy</text>`;

const concepts = [
  {
    id: "levy-breaker",
    name: "Breaker",
    note: "A curling wave breaks on the L's sloped face; spray shows the impact.",
    viewBox: "0 0 480 190",
    body: (ink) => `
  <path fill="${WATER}" d="M0 150 C8 118 28 88 58 74 C76 66 92 64 102 70 L89 80 C78 80 70 88 68 100 C66 114 68 132 72 144 L70 150 Z"/>
  <circle cx="96" cy="55" r="3.5" fill="${WATER}"/><circle cx="85" cy="50" r="2.5" fill="${WATER}"/><circle cx="106" cy="47" r="2" fill="${WATER}"/>
  ${leveeL(ink)}
  ${word(ink)}`,
  },
  {
    id: "levy-tide",
    name: "Tide lines",
    note: "Three swells run into the wall and stop; a green crown marks the levee top.",
    viewBox: "0 0 480 190",
    body: (ink) => `
  <g fill="none" stroke="${WATER}" stroke-width="9" stroke-linecap="round">
    <path d="M6 140 q14 -10 28 0 t28 0 t28 0"/>
    <path d="M14 115 q14 -10 28 0 t28 0 t28 0" opacity="0.7"/>
    <path d="M24 90 q14 -10 28 0 t28 0 t28 0" opacity="0.45"/>
  </g>
  ${leveeL(ink)}
  <path fill="#16A34A" d="M97.8 48 L100 40 L132 40 L132 48 Z"/>
  ${word(ink)}`,
  },
  {
    id: "levy-monoline",
    name: "Monoline",
    note: "A single-stroke curl meets an outlined levee; lighter type for a calmer feel.",
    viewBox: "0 0 480 190",
    body: (ink) => `
  <path fill="none" stroke="${WATER}" stroke-width="8" stroke-linecap="round" stroke-linejoin="round"
        d="M6 150 C18 120 40 90 68 84 C86 80 96 94 89 104 C83 112 71 109 71 99 C71 93 77 90 81 93"/>
  <path fill="none" stroke="${ink}" stroke-width="10" stroke-linejoin="round" d="M75 145 L103 45 L127 45 L127 123 L201 123 L201 145 Z"/>
  ${word(ink, 500)}`,
  },
  {
    id: "levy-icon",
    name: "App icon",
    note: "The breaker and levee alone, for the app icon and the navigation rail.",
    viewBox: "0 0 256 256",
    body: (_ink, id) => `
  <defs>
    <clipPath id="${id}-tile"><rect width="256" height="256" rx="56"/></clipPath>
  </defs>
  <g clip-path="url(#${id}-tile)">
    <rect width="256" height="256" fill="${WATER}"/>
    <path fill="#FFFFFF" opacity="0.5" d="M20 200 C30 160 54 122 86 106 C104 97 120 95 130 101 L114 106 C102 108 94 118 92 132 C90 150 92 174 94.5 188 L92 200 Z"/>
    <circle cx="124" cy="82" r="4.5" fill="#FFFFFF" opacity="0.6"/><circle cx="110" cy="76" r="3.5" fill="#FFFFFF" opacity="0.6"/><circle cx="134" cy="72" r="3" fill="#FFFFFF" opacity="0.6"/>
    <path fill="#FFFFFF" d="M92 200 L124 62 L158 62 L158 168 L222 168 L222 200 Z"/>
    <rect y="200" width="256" height="56" fill="#1D4ED8"/>
  </g>`,
  },
];

const svg = (c, ink, id = c.id) =>
  `<svg xmlns="http://www.w3.org/2000/svg" viewBox="${c.viewBox}" role="img" aria-label="Levy"><title>Levy</title>${c.body(ink, id)}\n</svg>\n`;

const dir = __dirname;
for (const c of concepts) fs.writeFileSync(path.join(dir, `${c.id}.svg`), svg(c, INK));

// Preview: each concept on a light card and a dark card (the wordmark ink swaps to white on dark).
const cards = concepts.map((c) => `
    <section>
      <h2>${c.name}</h2>
      <p>${c.note} <code>${c.id}.svg</code></p>
      <div class="pair">
        <div class="tile light">${svg(c, INK, c.id + "-l")}</div>
        <div class="tile dark">${svg(c, "#FFFFFF", c.id + "-d")}</div>
      </div>
    </section>`).join("");

fs.writeFileSync(path.join(dir, "index.html"), `<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="utf-8">
<title>Levy logo concepts</title>
<style>
  body { margin: 0; padding: 40px; font-family: ${FONT}; background: #F3F5F8; color: ${INK}; }
  h1 { font-size: 22px; margin: 0 0 4px; }
  .lede { color: #5B6474; margin: 0 0 32px; }
  section { margin-bottom: 40px; }
  h2 { font-size: 16px; margin: 0 0 4px; }
  section p { color: #5B6474; margin: 0 0 12px; font-size: 14px; }
  code { font-size: 12px; background: #E6E9EF; padding: 1px 6px; border-radius: 4px; }
  .pair { display: grid; grid-template-columns: 1fr 1fr; gap: 16px; }
  .tile { border-radius: 12px; padding: 28px; display: flex; align-items: center; justify-content: center; height: 180px; }
  .tile svg { max-width: 100%; max-height: 100%; }
  .light { background: #FFFFFF; border: 1px solid #E1E5EB; }
  .dark { background: #0F1218; }
</style>
</head>
<body>
  <h1>Levy logo concepts</h1>
  <p class="lede">The L is a levee: a sloped face takes the wave on the left, and its foot runs under "evy" to keep the rest of the word dry.</p>${cards}
</body>
</html>
`);
console.log(`Wrote ${concepts.length} SVGs and index.html to ${dir}`);
