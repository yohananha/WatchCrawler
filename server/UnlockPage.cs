namespace GarminAchievements;

/// <summary>The page a user opens on their phone after the watch says "Out of Mana": type the code from the
/// watch (and a coupon, if they have one), see the price, pay with PayPal (or redeem a free coupon).
/// Plain HTML + the PayPal JS SDK; the server does the create/capture calls (see Program.cs /unlock/*).</summary>
public static class UnlockPage
{
    public static string Html(HostedSettings s, string? paypalClientId, bool lemonSqueezy = false) => Template
        .Replace("{{LEMON}}", lemonSqueezy ? "true" : "false")
        .Replace("{{PRICE}}", s.PriceUsd.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture))
        .Replace("{{YEARS}}", s.LicenseYears.ToString())
        .Replace("{{PER_DAY}}", s.AiPerDay.ToString())
        .Replace("{{TRIAL_DAYS}}", s.TrialDays.ToString())
        .Replace("{{PAYPAL_SDK}}", paypalClientId is null
            ? ""
            : $"<script src=\"https://www.paypal.com/sdk/js?client-id={Uri.EscapeDataString(paypalClientId)}&currency=USD&intent=capture\"></script>");

    private const string Template = """
        <!doctype html>
        <html lang="en">
        <head>
        <meta charset="utf-8">
        <meta name="viewport" content="width=device-width, initial-scale=1">
        <title>WatchCrawler: restore your Mana</title>
        <style>
          :root { color-scheme: light dark; }
          body { font-family: system-ui, sans-serif; max-width: 440px; margin: 32px auto; padding: 0 16px; line-height: 1.45; }
          h1 { font-size: 1.5rem; margin-bottom: 4px; }
          .muted { opacity: .7; font-size: .95rem; }
          label { display: block; margin-top: 14px; font-weight: 600; }
          input { width: 100%; box-sizing: border-box; font-size: 1.25rem; padding: 10px; margin-top: 4px;
                  letter-spacing: .15em; text-transform: uppercase; border: 1px solid #8884; border-radius: 8px; }
          input.small { font-size: 1rem; letter-spacing: .05em; }
          button { margin-top: 14px; width: 100%; padding: 12px; font-size: 1.05rem; border-radius: 8px; border: 0;
                   background: #4b3cff; color: #fff; cursor: pointer; }
          button[disabled] { opacity: .5; cursor: default; }
          #status { margin-top: 14px; min-height: 1.4em; }
          .ok { color: #1a9f4a; font-weight: 600; }
          .err { color: #d33; }
          #pay { margin-top: 16px; }
          .price { font-size: 1.4rem; font-weight: 700; margin-top: 12px; }
          .price s { opacity: .5; font-weight: 400; font-size: 1rem; margin-right: 6px; }
          footer { margin-top: 36px; font-size: .85rem; opacity: .65; }
        </style>
        {{PAYPAL_SDK}}
        </head>
        <body>
        <h1>Restore your Mana</h1>
        <p class="muted">WatchCrawler narrates your Garmin with AI-written achievements. The first {{TRIAL_DAYS}} days are free.
        A one-time unlock gives <b>{{YEARS}} years</b> of narration (up to {{PER_DAY}} AI achievements a day; built-in lines after that).</p>

        <label for="code">Code from your watch</label>
        <input id="code" maxlength="8" autocomplete="off" autocapitalize="characters" placeholder="K7P3QX">
        <label for="coupon">Coupon <span class="muted">(optional)</span></label>
        <input id="coupon" class="small" autocomplete="off" placeholder="">
        <button id="check" onclick="check()">Check code</button>

        <div id="status"></div>
        <div id="priceBox" class="price" hidden></div>
        <div id="pay"></div>
        <a id="buy" hidden><button>Pay now</button></a>
        <button id="redeem" hidden onclick="redeem()">Unlock for free</button>

        <footer>One-time payment, no subscription. If it doesn't work on your watch within a day, reply to your receipt email and
        you'll get a refund. Your activity data goes from the watch to this server and to the AI provider only to write the jokes;
        nothing is sold or shared. <a href="/privacy">Privacy policy</a>.</footer>

        <script>
          var LEMON = {{LEMON}};
          var state = { code: null, coupon: null, price: null };
          function el(id) { return document.getElementById(id); }
          function say(text, cls) { var s = el('status'); s.textContent = text; s.className = cls || ''; }
          function norm(v) { return (v || '').toUpperCase().replace(/[^A-Z2-9]/g, ''); }

          async function check() {
            var code = norm(el('code').value), coupon = el('coupon').value.trim();
            el('pay').innerHTML = ''; el('redeem').hidden = true; el('priceBox').hidden = true; el('buy').hidden = true;
            if (code.length < 6) { say('Enter the 6-character code shown on your watch.', 'err'); return; }
            say('Checking…');
            try {
              var r = await fetch('/unlock/price?code=' + encodeURIComponent(code) + '&coupon=' + encodeURIComponent(coupon));
              var d = await r.json();
              if (!r.ok) { say(d.error || 'Unknown code.', 'err'); return; }
              state = { code: code, coupon: d.coupon, price: d.price };
              var box = el('priceBox');
              box.hidden = false;
              box.innerHTML = (d.coupon ? '<s>$' + d.listPrice.toFixed(2) + '</s>' : '') + (d.price === 0 ? 'Free' : '$' + d.price.toFixed(2))
                + (d.discountAtCheckout ? ' <small class="muted">(discount ' + d.discountAtCheckout + ' applied at checkout)</small>' : '');
              say(d.status, d.licensed ? 'ok' : '');
              if (d.licensed) return;
              if (d.price === 0) { el('redeem').hidden = false; return; }
              if (LEMON) {
                if (!d.checkoutUrl) { say('Payments are not configured on this server.', 'err'); return; }
                var a = el('buy'); a.href = d.checkoutUrl; a.hidden = false;
                say('Pay by card or PayPal on the next page (secure checkout by Lemon Squeezy). Your watch unlocks within a few hours of paying.');
                return;
              }
              if (!window.paypal) { say('Payments are not configured on this server.', 'err'); return; }
              paypal.Buttons({
                createOrder: async function () {
                  var r = await fetch('/unlock/order', { method: 'POST', headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({ code: state.code, coupon: state.coupon }) });
                  var d = await r.json();
                  if (!r.ok) throw new Error(d.error || 'Could not start the payment.');
                  return d.orderId;
                },
                onApprove: async function (data) {
                  say('Confirming payment…');
                  var r = await fetch('/unlock/capture', { method: 'POST', headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({ orderId: data.orderID }) });
                  var d = await r.json();
                  if (!r.ok) { say(d.error || 'Payment could not be confirmed.', 'err'); return; }
                  done(d);
                },
                onError: function (e) { say('PayPal error: ' + e, 'err'); }
              }).render('#pay');
            } catch (e) { say('Request failed: ' + e, 'err'); }
          }

          async function redeem() {
            el('redeem').disabled = true;
            say('Unlocking…');
            try {
              var r = await fetch('/unlock/redeem', { method: 'POST', headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ code: state.code, coupon: state.coupon }) });
              var d = await r.json();
              if (!r.ok) { say(d.error || 'Could not redeem.', 'err'); el('redeem').disabled = false; return; }
              done(d);
            } catch (e) { say('Request failed: ' + e, 'err'); el('redeem').disabled = false; }
          }

          function done(d) {
            el('pay').innerHTML = ''; el('redeem').hidden = true;
            say('Mana restored until ' + d.licensedUntil + '. Your watch picks it up within a few hours (open the app to hurry it).', 'ok');
          }
          el('code').addEventListener('keydown', function (e) { if (e.key === 'Enter') check(); });
        </script>
        </body>
        </html>
        """;
}
