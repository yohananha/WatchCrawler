using System.Net;

namespace GarminAchievements;

/// <summary>The hosted server's home page (GET / when HOSTED=true): what WatchCrawler is, what it costs, and the
/// legal links Lemon Squeezy's review and buyers expect. Images are in wwwroot/img (copies of docs/store).
/// CONNECTIQ_URL (env) is the store listing; until it is set, the page says "coming soon".</summary>
public static class LandingPage
{
    public static string Html(HostedSettings s, string? contact, string? storeUrl) => Template
        .Replace("{{STORE_CTA}}", string.IsNullOrWhiteSpace(storeUrl)
            ? "<span class=\"soon\">Coming soon to the Garmin Connect IQ Store</span>"
            : $"<a class=\"btn\" href=\"{WebUtility.HtmlEncode(storeUrl.Trim())}\">Get it on Connect IQ</a>")
        .Replace("{{FOOTER}}", Site.Footer(contact))
        .Replace("{{PRICE}}", s.PriceUsd.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture))
        .Replace("{{TRIAL_DAYS}}", s.TrialDays.ToString())
        .Replace("{{YEARS}}", s.LicenseYears.ToString())
        .Replace("{{PER_DAY}}", s.AiPerDay.ToString())
        .Replace("{{REFUND_DAYS}}", LegalPages.RefundDays.ToString());

    private const string Template = """
        <!doctype html>
        <html lang="en">
        <head>
        <meta charset="utf-8">
        <meta name="viewport" content="width=device-width, initial-scale=1">
        <title>WatchCrawler: your Garmin, narrated as an RPG</title>
        <meta name="description" content="A Garmin watch app that turns your runs, steps and couch time into sarcastic AI-written RPG achievements. Free {{TRIAL_DAYS}}-day trial, then ${{PRICE}} once.">
        <meta property="og:title" content="WatchCrawler">
        <meta property="og:description" content="Your Garmin, narrated as a sarcastic RPG.">
        <meta property="og:image" content="/img/hero-1440x720.jpg">
        <link rel="icon" href="/img/cover-500.png">
        <style>
          :root { color-scheme: light dark; --accent: #4b3cff; --line: #8884; --card: #8881; }
          * { box-sizing: border-box; }
          body { font-family: system-ui, sans-serif; margin: 0; line-height: 1.55; }
          main { max-width: 880px; margin: 0 auto; padding: 0 16px; }
          a { color: #6a5cff; }
          header.top { text-align: center; padding: 40px 0 8px; }
          h1 { font-size: clamp(2rem, 6vw, 3rem); margin: 0; letter-spacing: -.02em; }
          .tag { font-size: 1.2rem; opacity: .8; margin: 8px 0 20px; }
          .hero { width: 100%; height: auto; border-radius: 14px; display: block; }
          .cta { display: flex; flex-wrap: wrap; gap: 12px; justify-content: center; align-items: center; margin: 22px 0 6px; }
          .btn { display: inline-block; padding: 12px 22px; border-radius: 10px; background: var(--accent); color: #fff;
                 text-decoration: none; font-weight: 600; }
          .btn.ghost { background: transparent; color: inherit; border: 1px solid var(--line); }
          .soon { padding: 12px 18px; border-radius: 10px; border: 1px dashed var(--line); font-weight: 600; }
          h2 { font-size: 1.4rem; margin: 44px 0 14px; }
          .steps { display: grid; grid-template-columns: repeat(3, 1fr); gap: 14px; padding: 0; list-style: none; margin: 0; }
          .steps li { background: var(--card); border: 1px solid var(--line); border-radius: 12px; padding: 16px; }
          .steps b { display: block; margin-bottom: 4px; }
          .shots { display: grid; grid-template-columns: repeat(4, 1fr); gap: 14px; }
          .shots figure { margin: 0; text-align: center; font-size: .9rem; opacity: .9; }
          .shots img { width: 100%; max-width: 200px; height: auto; border-radius: 50%; background: #000; }
          .price { background: var(--card); border: 1px solid var(--line); border-radius: 14px; padding: 22px; }
          .price .big { font-size: 2.2rem; font-weight: 700; }
          .price ul { margin: 10px 0 16px; padding-left: 20px; }
          footer.site { max-width: 880px; margin: 48px auto 0; padding: 16px 16px 32px; border-top: 1px solid var(--line);
                        font-size: .9rem; opacity: .8; }
          footer.site p { margin: 4px 0; }
          @media (max-width: 640px) {
            .steps { grid-template-columns: 1fr; }
            .shots { grid-template-columns: repeat(2, 1fr); }
          }
        </style>
        </head>
        <body>
        <main>
          <header class="top">
            <h1>WatchCrawler</h1>
            <p class="tag">Your Garmin, narrated as a sarcastic RPG.</p>
            <img class="hero" src="/img/hero-1440x720.jpg" width="1440" height="720"
                 alt="A Garmin watch showing a WatchCrawler achievement">
            <div class="cta">
              {{STORE_CTA}}
              <a class="btn ghost" href="/unlock">Unlock with your watch code</a>
            </div>
          </header>

          <h2>How it works</h2>
          <ol class="steps">
            <li><b>1. Install</b>Add WatchCrawler to your Garmin watch from the Connect IQ Store. No account, no setup.</li>
            <li><b>2. Live your life</b>It notices your runs, step goals, new records, and the hours you spent welded to the couch.</li>
            <li><b>3. Get roasted</b>Each one becomes a short AI-written achievement on your wrist, saved in your Hall of Shame.</li>
          </ol>

          <h2>On your wrist</h2>
          <div class="shots">
            <figure><img src="/img/screen-1-run-unlocked.png" alt="Run achievement" loading="lazy"><figcaption>A run, celebrated</figcaption></figure>
            <figure><img src="/img/screen-2-steps-reward.png" alt="Step goal reward" loading="lazy"><figcaption>Step goal loot</figcaption></figure>
            <figure><img src="/img/screen-3-couch-record.png" alt="Couch record" loading="lazy"><figcaption>Couch records, too</figcaption></figure>
            <figure><img src="/img/screen-4-hall-of-shame.png" alt="Hall of Shame list" loading="lazy"><figcaption>Your Hall of Shame</figcaption></figure>
          </div>

          <h2>Pricing</h2>
          <div class="price">
            <div class="big">${{PRICE}} <span style="font-size:1rem;font-weight:400;opacity:.75">one time</span></div>
            <ul>
              <li>Free {{TRIAL_DAYS}}-day trial of AI narration. No card needed.</li>
              <li>One payment unlocks {{YEARS}} years of AI narration for your watch, up to {{PER_DAY}} AI achievements a day.</li>
              <li>No subscription. After the trial or the {{YEARS}} years, the app keeps working with its built-in lines.</li>
              <li>{{REFUND_DAYS}}-day refund, no questions asked.</li>
            </ul>
            <a class="btn" href="/unlock">Unlock WatchCrawler</a>
          </div>

          <h2>Questions</h2>
          <p><b>Which watches?</b> Garmin watches that run Connect IQ apps.<br>
          <b>Where do I get the code?</b> When the trial ends the watch shows it, and it's always on the app's MENU screen.<br>
          <b>What happens to my data?</b> Only the activity numbers needed to write the joke are sent; no GPS, no name, nothing sold.
          See the <a href="/privacy">privacy policy</a>.</p>
        </main>
        {{FOOTER}}
        </body>
        </html>
        """;
}
