namespace GarminAchievements;

/// <summary>The privacy policy the Connect IQ Store listing and the unlock page link to (GET /privacy). It describes what
/// this code actually does, so keep it in step: if a new field is sent to the server or the LLM, say so here.
/// The contact comes from <see cref="Site.ContactFromEnvironment"/>.</summary>
public static class PrivacyPage
{
    public const string Updated = "2026-10-02";

    public static string Html(HostedSettings s, string? contact) => Template
        .Replace("{{UPDATED}}", Updated)
        .Replace("{{CONTACT}}", Site.ContactHtml(contact))
        .Replace("{{FOOTER}}", Site.Footer(contact))
        .Replace("{{TRIAL_DAYS}}", s.TrialDays.ToString())
        .Replace("{{YEARS}}", s.LicenseYears.ToString());

    private const string Template = """
        <!doctype html>
        <html lang="en">
        <head>
        <meta charset="utf-8">
        <meta name="viewport" content="width=device-width, initial-scale=1">
        <title>WatchCrawler privacy policy</title>
        <style>
          :root { color-scheme: light dark; }
          body { font-family: system-ui, sans-serif; max-width: 680px; margin: 32px auto; padding: 0 16px; line-height: 1.55; }
          h1 { font-size: 1.6rem; margin-bottom: 2px; }
          h2 { font-size: 1.15rem; margin-top: 28px; }
          .muted { opacity: .7; }
          table { border-collapse: collapse; width: 100%; font-size: .95rem; }
          th, td { text-align: left; vertical-align: top; padding: 6px 8px; border-bottom: 1px solid #8884; }
          footer.site { margin-top: 40px; padding-top: 14px; border-top: 1px solid #8884; font-size: .9rem; opacity: .8; }
          footer.site p { margin: 4px 0; }
        </style>
        </head>
        <body>
        <h1>WatchCrawler privacy policy</h1>
        <p class="muted">Last updated {{UPDATED}}</p>

        <p>WatchCrawler is a Garmin watch app that turns your activity into short, sarcastic RPG-style "achievements".
        This page explains what data the app and its server use, why, and what you can do about it. Short version:
        only what is needed to write the jokes and to know whether your watch is unlocked. Nothing is sold, shared
        for advertising, or used to build a profile of you.</p>

        <h2>What the watch sends, and why</h2>
        <table>
          <tr><th>Data</th><th>Why</th></tr>
          <tr><td>A device identifier (Garmin's per-app id for your watch)</td><td>To know which watch is on its {{TRIAL_DAYS}}-day trial or unlocked, and to keep its jokes from repeating. It is not your name, email or Garmin account.</td></tr>
          <tr><td>The event that happened: its type (e.g. a run, a step goal reached, a sedentary alert), the sport and the name of the watch activity profile you recorded it with (e.g. "Trail Run"), the relevant numbers (distance, duration, pace, steps and step goal, floors, resting heart rate, Body Battery) and how they compare with your own recent average</td><td>So the achievement can be about what you actually did.</td></tr>
          <tr><td>The time of the event</td><td>Some jokes depend on the time of day.</td></tr>
          <tr><td>Technical error codes from the watch (e.g. "no network")</td><td>To find and fix problems.</td></tr>
        </table>
        <p>The app does <b>not</b> send GPS location or routes, heart-rate recordings, sleep data, your name, your
        Garmin account details or your contacts. Activity detection and the averages are computed on the watch.</p>

        <h2>Who processes it</h2>
        <ul>
          <li><b>The WatchCrawler server</b> (run by the developer on Fly.io, in the United States) receives the data
          above, asks the AI to write the achievement, and sends the text back to your watch.</li>
          <li><b>Anthropic</b> (the Claude AI) receives the event details above to write the text. Anthropic does not
          use API data to train its models and deletes it after a limited retention period under its commercial
          terms. It never receives your device identifier.</li>
          <li><b>Lemon Squeezy</b> handles the purchase if you unlock the app. Your payment details, name, email and
          address go to them, not to the developer's server; the server only learns that the 6-character code from
          your watch was paid for. Their privacy policy applies to the checkout.</li>
          <li><b>Error reports</b> (what failed, an anonymous install id, the app version; never activity data or
          keys) go to the developer through a push-notification service (ntfy).</li>
        </ul>

        <h2>What is stored, and for how long</h2>
        <ul>
          <li>Per watch: the device identifier, its unlock code, when it was first and last seen, whether and until
          when it is unlocked (and the purchase's order number), how many AI achievements it used today and in total,
          and their cost. Kept while the app is in use, and deleted on request.</li>
          <li>The last 10 achievement texts written for that watch (so new ones don't repeat). Older ones are deleted
          automatically. The activity details are <b>not</b> stored in the database after the text is written (only briefly in the server logs, below).</li>
          <li>Server logs (request time, IP address, an abbreviated device id, the event type and its main number,
          the result) for troubleshooting, kept for a short period by the hosting provider.</li>
        </ul>
        <p>On the watch itself, the app keeps your recent achievements (the "Hall of Shame") and its settings. They
        are removed when you uninstall the app.</p>

        <h2>Your choices and rights</h2>
        <ul>
          <li><b>Use it without AI:</b> after the trial, the app keeps working offline with built-in lines.</li>
          <li><b>Delete your data:</b> send the 6-character code shown on your watch (WatchCrawler, MENU) and the
          server-side record for that watch, including its stored achievement texts, is deleted. A purchase record
          may be kept by Lemon Squeezy as required for tax and accounting.</li>
          <li><b>Access or correction:</b> ask with your code and you'll get what is stored for that watch.</li>
          <li>If you are in the EU/UK you may also complain to your data protection authority.</li>
        </ul>

        <h2>Children</h2>
        <p>WatchCrawler is not directed at children under 13 (16 in the EU), and does not knowingly collect their data.</p>

        <h2>Changes</h2>
        <p>If this policy changes, the date at the top changes too. Purchases already made keep their {{YEARS}} years of narration.</p>

        <h2>Contact</h2>
        <p>{{CONTACT}}</p>

        {{FOOTER}}
        </body>
        </html>
        """;
}
