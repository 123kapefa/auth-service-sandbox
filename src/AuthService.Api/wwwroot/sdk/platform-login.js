/*
 * Вход для страницы приложения проекта — Authorization Code + PKCE через страницы входа проекта, без своей библиотеки.
 * Подключение (адрес — сервис входа проекта):
 *   <script src="https://auth.shop.example.com/sdk/platform-login.js"></script>
 *   const auth = PlatformLogin.create({
 *     authority: "https://id.shop.example.com/realms/shop",  // realm проекта (Authentication:Authority сервиса)
 *     clientId: "orders-web",                                 // клиент входа приложения (add-app.py --web)
 *     app: "orders",                                          // оформление приложения на страницах входа
 *   });
 *   await auth.handleRedirect();                 // вернулись со страницы входа — забрать токены
 *   if (!auth.isLoggedIn()) auth.login();        // или по кнопке
 *   const response = await auth.fetch("/api/orders");   // с токеном; 401 — обновит токен и повторит один раз
 *   auth.logout();
 * Токены — в sessionStorage вкладки (storage — другое хранилище), в API приложения — заголовком Authorization.
 * 401 с access_changed (роли или режим изменились) и истёкший токен лечатся одинаково — обновлением по refresh-токену.
 */
(function (global) {
  "use strict";

  var b64url = function (bytes) {
    return btoa(String.fromCharCode.apply(null, new Uint8Array(bytes)))
      .replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "");
  };
  var random = function () { return b64url(crypto.getRandomValues(new Uint8Array(32))); };

  function create(options) {
    if (!options || !options.authority || !options.clientId) {
      throw new Error("PlatformLogin.create: нужны authority и clientId");
    }
    var authority = options.authority.replace(/\/+$/, "");
    var store = options.storage || global.sessionStorage;
    var prefix = "platform-login:" + options.clientId + ":";
    var redirectUri = options.redirectUri || global.location.origin + global.location.pathname;
    var oidc = function (path) { return authority + "/protocol/openid-connect/" + path; };
    var read = function (key) { var value = store.getItem(prefix + key); return value ? JSON.parse(value) : null; };
    var write = function (key, value) {
      if (value === null) store.removeItem(prefix + key); else store.setItem(prefix + key, JSON.stringify(value));
    };

    function token(params) {
      var body = new URLSearchParams(Object.assign({ client_id: options.clientId }, params));
      return fetch(oidc("token"), { method: "POST", body: body }).then(function (response) {
        if (!response.ok) throw new Error("Сервис входа не выдал токен: " + response.status);
        return response.json();
      }).then(function (tokens) { write("tokens", tokens); return tokens; });
    }

    function refresh() {
      var tokens = read("tokens");
      if (!tokens || !tokens.refresh_token) { write("tokens", null); return Promise.resolve(null); }
      return token({ grant_type: "refresh_token", refresh_token: tokens.refresh_token })
        .catch(function () { write("tokens", null); return null; }); // сессия закончилась — войти заново
    }

    return {
      /** Перейти на страницу входа проекта. extra — свои параметры (например, login_hint). */
      login: function (extra) {
        var verifier = random();
        var state = random();
        write("pkce", { verifier: verifier, state: state });
        return crypto.subtle.digest("SHA-256", new TextEncoder().encode(verifier)).then(function (hash) {
          var query = Object.assign({
            client_id: options.clientId, response_type: "code", scope: "openid", redirect_uri: redirectUri,
            state: state, code_challenge: b64url(hash), code_challenge_method: "S256",
          }, options.app ? { platform_app: options.app } : {}, extra || {});
          global.location.href = oidc("auth") + "?" + new URLSearchParams(query);
        });
      },

      /** Возврат со страницы входа: код → токены, параметры из адреса убираются. true — вход только что завершён. */
      handleRedirect: function () {
        var query = new URLSearchParams(global.location.search);
        if (!query.has("code") && !query.has("error")) return Promise.resolve(false);
        global.history.replaceState(null, "", redirectUri.replace(global.location.origin, "") || "/");
        var pkce = read("pkce") || {};
        write("pkce", null);
        if (query.has("error")) return Promise.reject(new Error(query.get("error_description") || query.get("error")));
        if (query.get("state") !== pkce.state) return Promise.reject(new Error("Вход начат не здесь — войдите ещё раз"));
        return token({
          grant_type: "authorization_code", code: query.get("code"), code_verifier: pkce.verifier, redirect_uri: redirectUri,
        }).then(function () { return true; });
      },

      isLoggedIn: function () { return read("tokens") !== null; },

      /** Текущий access-токен (или null): для своих запросов, если fetch не подходит. */
      accessToken: function () { var tokens = read("tokens"); return tokens ? tokens.access_token : null; },

      /**
       * fetch с токеном. 401 — обновить токен и повторить один раз. Обновить не вышло (сессия закончилась) — токены
       * забываются (isLoggedIn() = false) и возвращается сам ответ 401.
       */
      fetch: function (input, init) {
        var send = function (tokens) {
          var headers = new Headers((init && init.headers) || {});
          if (tokens) headers.set("Authorization", "Bearer " + tokens.access_token);
          return fetch(input, Object.assign({}, init, { headers: headers }));
        };
        var tokens = read("tokens");
        return send(tokens).then(function (response) {
          if (response.status !== 401 || !tokens) return response;
          return refresh().then(function (fresh) { return fresh ? send(fresh) : response; });
        });
      },

      /** Выйти: завершить сессию на сервере входа и вернуться на страницу. */
      logout: function () {
        var tokens = read("tokens") || {};
        write("tokens", null);
        global.location.href = oidc("logout") + "?" + new URLSearchParams({
          id_token_hint: tokens.id_token || "", post_logout_redirect_uri: redirectUri,
        });
      },
    };
  }

  global.PlatformLogin = { create: create };
})(window);
