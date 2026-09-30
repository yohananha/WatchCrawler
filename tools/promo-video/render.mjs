// Renders promo.html frame by frame in headless Chromium and encodes an MP4.
//   node render.mjs            -> out/watchcrawler-promo.mp4 (with the 8-bit music from sound.mjs) + out/poster.png
//   node render.mjs --serve    -> just serve the page for previewing in a browser
import { createServer } from "node:http";
import { readFile, mkdir, writeFile } from "node:fs/promises";
import { spawn, spawnSync } from "node:child_process";
import { dirname, extname, join, normalize, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { renderSoundtrack } from "./sound.mjs";

const here = dirname(fileURLToPath(import.meta.url));
const repoRoot = resolve(here, "../..");
const outDir = join(here, "out");
const PAGE = "/tools/promo-video/promo.html";
const POSTER_TIME = 5.2; // seconds into the video: the legendary unlock

const TYPES = { ".html": "text/html", ".png": "image/png", ".jpg": "image/jpeg", ".fnt": "text/plain", ".js": "text/javascript" };

// promo.html fetch()es the watch's font files, which file:// URLs don't allow.
function serve(port) {
  const server = createServer(async (req, res) => {
    const path = normalize(join(repoRoot, decodeURIComponent(new URL(req.url, "http://x").pathname)));
    if (!path.startsWith(repoRoot)) { res.writeHead(403).end(); return; }
    try {
      const body = await readFile(path);
      res.writeHead(200, { "Content-Type": TYPES[extname(path)] || "application/octet-stream" }).end(body);
    } catch {
      res.writeHead(404).end();
    }
  });
  return new Promise(ok => server.listen(port, "127.0.0.1", () => ok(server)));
}

async function ffmpegPath() {
  if (spawnSync("ffmpeg", ["-version"]).status === 0) return "ffmpeg";
  return (await import("ffmpeg-static")).default;
}

async function main() {
  const preview = process.argv.includes("--serve");
  const server = await serve(preview ? 8765 : 0);
  const url = `http://127.0.0.1:${server.address().port}${PAGE}`;
  if (preview) {
    console.log(`Preview at ${url}`);
    return;
  }

  const { chromium } = await import("playwright");
  const browser = await chromium.launch();
  const page = await browser.newPage({ viewport: { width: 1080, height: 1920 } });
  await page.goto(url + "?headless");
  await page.waitForFunction(() => window.ready === true);
  const { total, fps } = await page.evaluate(() => ({ total: window.TOTAL, fps: window.FPS }));
  const frames = Math.ceil(total * fps);

  // Refuse to encode if any frame draws text outside the round watch screen.
  const overflow = await page.evaluate(({ frames, fps }) => {
    window.boundsLog = [];
    const bad = new Map();
    for (let i = 0; i < frames; i++) {
      window.boundsLog.length = 0;
      window.renderFrame(i / fps);
      for (const v of window.boundsLog) if (!bad.has(v.s)) bad.set(v.s, `${(i / fps).toFixed(2)}s "${v.s}" at ${v.x},${v.y}`);
    }
    window.boundsLog = null;
    return [...bad.values()];
  }, { frames, fps });
  if (overflow.length) {
    console.error("Text outside the watch screen:\n  " + overflow.join("\n  "));
    process.exit(1);
  }
  console.log(`Bounds check passed. Rendering ${frames} frames (${total.toFixed(1)} s at ${fps} fps)`);

  await mkdir(outDir, { recursive: true });
  const wav = join(outDir, "soundtrack.wav");
  await writeFile(wav, renderSoundtrack(frames / fps));

  const out = join(outDir, "watchcrawler-promo.mp4");
  const ffmpeg = spawn(await ffmpegPath(), [
    "-y", "-loglevel", "error",
    "-f", "image2pipe", "-framerate", String(fps), "-c:v", "png", "-i", "-",
    "-i", wav,
    "-c:v", "libx264", "-preset", "slow", "-crf", "18", "-pix_fmt", "yuv420p",
    "-c:a", "aac", "-b:a", "192k", "-shortest",
    "-movflags", "+faststart", out,
  ], { stdio: ["pipe", "inherit", "inherit"] });
  const done = new Promise((ok, fail) => ffmpeg.on("close", code => code === 0 ? ok() : fail(new Error("ffmpeg exited " + code))));

  const grab = t => page.evaluate(t => {
    window.renderFrame(t);
    return document.getElementById("stage").toDataURL("image/png").split(",")[1];
  }, t);

  for (let i = 0; i < frames; i++) {
    const png = Buffer.from(await grab(i / fps), "base64");
    if (!ffmpeg.stdin.write(png)) await new Promise(ok => ffmpeg.stdin.once("drain", ok));
    if (i % fps === 0) process.stdout.write(`\r${i}/${frames}`);
  }
  ffmpeg.stdin.end();
  await done;
  await writeFile(join(outDir, "poster.png"), Buffer.from(await grab(POSTER_TIME), "base64"));
  console.log(`\rWrote ${out}`);

  await browser.close();
  server.close();
}

main().catch(err => { console.error(err); process.exit(1); });
