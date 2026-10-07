# Distributing WatchCrawler: the hosted (store) version

Two ways to run WatchCrawler:

| | Self-hosted (`setup.sh`) | **Hosted (Connect IQ Store)** |
|---|---|---|
| Who pays the AI | The user, with their own key | The developer |
| Setup for the user | Key, Fly account, SDK, USB copy | Install from the store, done |
| Server | One per user | One for everyone (`HOSTED=true`) |
| Price | Free | Free 7-day trial, then **$7.99 once** for 3 years of AI narration |

This file is the reasoning behind the hosted numbers. The mechanics are in
[`server/README.md`](../server/README.md#hosted-mode) and [`watch/README.md`](../watch/README.md).

## Why not Garmin's own paid apps

Garmin's paid-app program (since 2024) costs **$100/year** plus **15%** of each sale, and only works in
a handful of countries. That is a lot before knowing anyone will buy. Garmin still allows third-party
payment for apps in the store, so instead:

- the app is **free in the store**;
- the server runs the **trial and the licence** (it needs to know every watch anyway);
- the unlock is sold through **Lemon Squeezy** as merchant of record: they run the checkout (card or
  PayPal), collect VAT/sales tax worldwide, handle refunds and disputes, and pay you out to a personal
  PayPal or bank account. 5% + $0.50 per sale, no yearly fee, no business account, nothing to file.
  (PayPal Checkout is wired in as an alternative; it's ~1.5 points cheaper but makes *you* the seller:
  business account, tax, $20 chargebacks.)

## Business model

1. Install from the store. The first contact with the server starts a **7-day trial** of AI narration.
2. After the trial the app keeps working with its built-in lines, and once a day shows an
   *"Out of Mana: restore it at &lt;url&gt;, code K7P3QX"* message. The code is also on the watch's
   **MENU** screen.
3. On the phone, the user opens the unlock page, types the code, and is sent to the checkout with the
   code attached; pays **$7.99** (or redeems a 100% coupon on the page itself).
4. The watch is licensed for **3 years**, at most **6 AI-written achievements a day** (the rest of the
   daily cap uses built-in lines). After 3 years: built-in lines, and the message comes back.

Coupons (`coupon add FRIEND 100 --uses 1`) give friends/testers a free or discounted unlock.

## What a user costs

One achievement ≈ 1,200 input + 80 output tokens with Claude Haiku 4.5 ($1 / $5 per million):
**$0.0016 per call**, or about **$0.0013** with a trimmed prompt. Add 10% for retries.

AI cost per licensed user (Haiku, 6/day cap):

| Usage | Per year | 3 years |
|---|---|---|
| Light, 4 calls/day | $2.09 | $6.26 |
| At the cap, 6/day | $3.13 | **$9.40** (hard worst case) |
| Expected, after churn (~1.45 active years out of 3) | | **≈ $4.55** |

Trial users who never buy cost about **$0.06** each (7 days × 6 × $0.0013). At a 10% conversion rate
that adds ≈ $0.55 to each sale.

Fixed costs, about **$60/year** (≈ $180 over 3 years): one always-on Fly machine (shared CPU, 256 MB,
~$2-3/month), a 1 GB volume, a domain if you want one. No Garmin fee.

| Users | Fixed cost per user, 3 years |
|---|---|
| 100 | $1.80 |
| 500 | $0.36 |

## What a sale nets

Lemon Squeezy (5% + $0.50; PayPal Checkout in brackets for comparison):

| Price | Net |
|---|---|
| $4.99 | $4.24 ($4.24) |
| $5.99 | $5.19 ($5.19) |
| $6.99 | $6.14 ($6.15) |
| **$7.99** | **$7.09** ($7.10) |

Payouts have a small fixed fee per payout (PayPal payout is free above a minimum; bank payouts a few
dollars), so let them accumulate rather than paying out every sale.

## Price evaluation

- Expected cost per paying user ≈ $4.55 (AI) + $0.55 (trials) + $0.40-1.80 (fixed) ≈ **$5.5-6.9**.
- **$7.99 nets $7.09**: covers the expected case from the first ~100 buyers. Only a user who hits the
  cap every single day for 3 years ($9.40) costs more than they paid.
- $5.99 runs slightly at a loss until about 500 users.
- If a lower price matters more: **$5.99 with a cap of 4/day** (worst case $6.26, expected ≈ $3).
- Upfront risk is only the infrastructure (~$5/month). Set `HOSTED_MONTHLY_BUDGET_USD` and a spend
  limit in the Anthropic console so a surprise can't run away.

Before fixing the price for good: run a week on your own watches and compare real tokens per call and
calls per day with the estimates (`GET /admin/stats`).

## Checklist to go live

- [ ] Lemon Squeezy account (check your country is in their payout list first). One product
      "WatchCrawler unlock", $7.99, single payment. Copy its **Buy link** → `LEMONSQUEEZY_CHECKOUT_URL`.
      Settings → Webhooks → add `https://<server>/webhook/lemonsqueezy`, events `order_created` and `order_refunded`, a
      long random signing secret → `LEMONSQUEEZY_WEBHOOK_SECRET`. Use their **test mode** first: a
      test order must flip your watch's MENU screen to "MANA RESTORED".
- [ ] Discounts for friends: 100% = our coupons (`coupon add FRIEND 100 --uses 1`); partial =
      a discount code in the Lemon Squeezy dashboard (buyers type it in the same coupon box).
- [ ] Deploy the hosted server (`server/fly.hosted.toml`), `fly secrets set` everything in
      `server/README.md` → Hosted mode. Make sure `min_machines_running = 1`.
- [ ] Daily backup of `/data/licenses.db` (a `fly ssh console -C "sqlite3 ..."` cron, or
      `fly volumes snapshots` which Fly takes daily by default).
- [ ] `watch/tools/build_personal.sh --store` with the public URL and the shared key → upload the
      `.iq` to the Connect IQ Store. Keep `watch/keys/developer_key.der` safe: every update must be
      signed with the same key.
- [ ] Store listing: icon, screenshots, "7-day free AI trial, $7.99 one-time unlock = 3 years of AI
      narration; works with built-in lines without it", privacy policy URL.
- [ ] Privacy policy: activity data → your server → Anthropic (zero retention), history kept 10 lines
      per watch, deletion on request (`DELETE /admin/devices/{code}`).
- [ ] Refund: in the Lemon Squeezy dashboard; a full refund revokes the licence automatically
      (`order_refunded` webhook). By hand: `POST /admin/license {code, revoke:true}`.
- [ ] Income tax on the payouts.
