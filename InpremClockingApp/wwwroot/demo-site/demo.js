// The demo website's helpers: the public API (same origin), the CAPTCHA answer, the session's tokens.
"use strict";
const demo = {
  api: "/api/public/v1",
  config: null,

  async loadConfig() {
    if (!this.config) this.config = await (await fetch("/demo-site/config.json")).json();
    return this.config;
  },

  uuid() { return crypto.randomUUID ? crypto.randomUUID() : String(Date.now()) + Math.random().toString(16).slice(2); },

  // A call to the website API. Returns { status, body }; never throws for an HTTP error.
  async call(method, path, { body, token, key } = {}) {
    const headers = { "Accept": "application/json" };
    if (body !== undefined) headers["Content-Type"] = "application/json";
    if (token) headers["Authorization"] = "Bearer " + token;
    if (key) headers["Idempotency-Key"] = key;
    const response = await fetch(this.api + path, { method, headers, body: body === undefined ? undefined : JSON.stringify(body), credentials: "omit" });
    let json = null;
    try { json = await response.json(); } catch (e) { json = null; }
    return { status: response.status, body: json };
  },

  // The CAPTCHA answer: the Turnstile widget's when a site key is configured, else the configured test answer.
  async captcha() {
    const config = await this.loadConfig();
    const widget = document.querySelector("[name='cf-turnstile-response']");
    return widget && widget.value ? widget.value : config.captchaTestToken;
  },

  async addCaptchaWidget(container) {
    const config = await this.loadConfig();
    if (!config.captchaSiteKey) return;
    const div = document.createElement("div");
    div.className = "cf-turnstile";
    div.dataset.sitekey = config.captchaSiteKey;
    container.appendChild(div);
    const script = document.createElement("script");
    script.src = "https://challenges.cloudflare.com/turnstile/v0/api.js";
    script.async = true;
    document.head.appendChild(script);
  },

  // Tokens live only for this browser tab (never localStorage: design 7.7).
  save(name, value) { sessionStorage.setItem(name, value); },
  load(name) { return sessionStorage.getItem(name); },

  here(page) { return location.origin + "/demo-site/" + page; },

  problem(result) {
    const b = result.body || {};
    const fields = b.errors ? " " + Object.entries(b.errors).map(([k, v]) => k + ": " + v.join(" ")).join(" ") : "";
    return (b.title || "Something went wrong.") + (b.code ? " (" + b.code + ")" : "") + fields;
  },

  money(price) { return price.amount + " " + price.currency; },

  // design 7.5: confirm, then poll every 2 s for 30 s, then every 10 s up to 2 minutes.
  async confirmAndPoll(checkoutId, token, show) {
    let result = await this.call("POST", "/checkout-sessions/" + checkoutId + "/confirm", { token });
    const started = Date.now();
    const finals = ["Active", "Cancelled", "Expired", "Failed"];
    while (result.status === 200 && !finals.includes(result.body.status)) {
      show(result.body);
      const elapsed = Date.now() - started;
      if (elapsed > 120000) { show({ status: "Slow", message: "Payment received, activation is taking longer than usual. We will email you." }); return; }
      await new Promise(r => setTimeout(r, elapsed < 30000 ? 2000 : 10000));
      result = await this.call("GET", "/checkout-sessions/" + checkoutId, { token });
    }
    show(result.status === 200 ? result.body : { status: "Error", message: this.problem(result) });
  },
};
