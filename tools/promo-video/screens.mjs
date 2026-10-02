// Store screenshots: renders the 454x454 watch screen (the same replica promo.html uses) at the
// moments where a scene is fully drawn, and writes round-masked PNGs for the Connect IQ listing
// (each must be under 150 KB). Run after `npm install` + `npx playwright install chromium`:
//   node screens.mjs            -> out/screens/*.png
import { chromium } from "playwright";
import { createServer } from "node:http";
import { readFile, mkdir, writeFile } from "node:fs/promises";
import { extname, join, resolve } from "node:path";

const ROOT = resolve(import.meta.dirname, "../..");
const TYPES = { ".html": "text/html", ".png": "image/png", ".jpg": "image/jpeg", ".fnt": "text/plain", ".mjs": "text/javascript", ".js": "text/javascript" };
const server = createServer(async (req, res) => {
  try {
    const path = join(ROOT, decodeURIComponent(new URL(req.url, "http://x").pathname));
    res.writeHead(200, { "Content-Type": TYPES[extname(path)] ?? "application/octet-stream" });
    res.end(await readFile(path));
  } catch { res.writeHead(404); res.end(); }
}).listen(0, "127.0.0.1");
await new Promise(ok => server.once("listening", ok));
const url = `http://127.0.0.1:${server.address().port}/tools/promo-video/promo.html?headless`;

const browser = await chromium.launch();
const page = await browser.newPage();
await page.goto(url);
await page.waitForFunction(() => window.ready === true);

// [file, what to draw]: each achievement at the end of its record scene (text fully typed) and of its
// reward scene, plus two Hall of Shame entries.
const shots = await page.evaluate(() => {
  // A still image shouldn't show the blinking typing cursor: same text, no "_".
  typeText = (g, text, font, cx, y, color, t, rate) => {
    if (t < 0) return;
    const shown = text.substring(0, Math.min(Math.floor(t * rate), text.length));
    drawText(g, Math.round(cx - textWidth(shown, font) / 2), y, font, shown, color);
  };
  const out = [];
  const shot = (name, draw) => {
    sg.fillStyle = "#000";
    sg.fillRect(0, 0, 454, 454);
    draw(sg);
    // Round watch face: everything outside the circle transparent.
    const c = document.createElement("canvas");
    c.width = c.height = 454;
    const g = c.getContext("2d");
    g.beginPath(); g.arc(227, 227, 227, 0, 2 * Math.PI); g.clip();
    g.drawImage(screen, 0, 0);
    out.push([name, c.toDataURL("image/png").split(",")[1]]);
  };
  ACHIEVEMENTS.forEach((a, i) => {
    const n = ["run", "steps", "couch"][i];
    shot(`${i + 1}-${n}-unlocked.png`, g => drawAchievement(g, a, a.durUnlock - 0.05));
    shot(`${i + 1}-${n}-record.png`, g => drawAchievement(g, a, a.durUnlock + a.durRecord - 0.05));
    shot(`${i + 1}-${n}-reward.png`, g => drawAchievement(g, a, a.animTotal - 0.05));
  });
  shot("4-hall-of-shame.png", g => drawHallOfShame(g, 3));
  return out;
});

const dir = join(import.meta.dirname, "out", "screens");
await mkdir(dir, { recursive: true });
for (const [name, b64] of shots) {
  const buf = Buffer.from(b64, "base64");
  await writeFile(join(dir, name), buf);
  console.log(`${name}  ${(buf.length / 1024).toFixed(0)} KB${buf.length > 150 * 1024 ? "  (over the store's 150 KB!)" : ""}`);
}
await browser.close();
server.close();
