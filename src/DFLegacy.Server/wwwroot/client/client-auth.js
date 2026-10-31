/*
 * \u5145\u503C\u9875\u5171\u7528\u7684\u9274\u6743\u811A\u672C\uFF08client_pay.htm / client_pay_bank.htm / client_diamond.htm\uFF09\u3002
 *
 * 8081 \u7BA1\u7406\u63A5\u53E3\u73B0\u5728\u9700\u8981 Bearer token\uFF0C\u800C\u5145\u503C\uFF08client-pay/*\uFF09\u53EA\u5141\u8BB8\u7BA1\u7406\u5458\u8C03\u7528\u3002
 * \u672C\u811A\u672C\u8D1F\u8D23\uFF1A\u590D\u7528 /admin \u7684\u4F1A\u8BDD\uFF08\u540C\u6E90 sessionStorage\uFF09\u2192 \u5E26 Authorization \u53D1\u8BF7\u6C42 \u2192
 * 401/403 \u65F6\u5F39\u51FA\u7BA1\u7406\u5458\u767B\u5F55\u6846\uFF0C\u767B\u5F55\u6210\u529F\u540E\u81EA\u52A8\u91CD\u653E\u4E00\u6B21\u3002
 *
 * \u6CE8\u610F\uFF1A\u672C\u6587\u4EF6\u523B\u610F\u53EA\u4F7F\u7528 ASCII\uFF08\u4E2D\u6587\u4E00\u5F8B\u5199\u6210 \uXXXX \u8F6C\u4E49\uFF09\u3002
 * \u8FD9\u4E9B\u8001\u9875\u9762\u662F gb2312 \u7F16\u7801\uFF0C\u5916\u94FE\u811A\u672C\u91CC\u82E5\u51FA\u73B0\u975E ASCII \u5B57\u8282\uFF0C\u6D4F\u89C8\u5668\u4F1A\u6309\u9875\u9762\u7F16\u7801\u89E3\u91CA\u800C\u4E71\u7801\u3002
 */
(function () {
  var TOKEN_KEY = "dflegacy.admin.token";
  var USER_KEY = "dflegacy.admin.userName";
  var ROLE_KEY = "dflegacy.admin.role";

  var TEXT = {
    title: "\u5145\u503c\u9700\u8981\u7ba1\u7406\u5458\u767b\u5f55",
    user: "\u8d26\u53f7",
    pass: "\u5bc6\u7801",
    ok: "\u767b\u5f55",
    cancel: "\u53d6\u6d88",
    badCreds: "\u8d26\u53f7\u6216\u5bc6\u7801\u9519\u8bef\u3002",
    throttled: "\u5931\u8d25\u6b21\u6570\u8fc7\u591a\uff0c\u8bf7\u7a0d\u540e\u518d\u8bd5\u3002",
    loginFailed: "\u767b\u5f55\u5931\u8d25",
    badResponse: "\u670d\u52a1\u7aef\u767b\u5f55\u54cd\u5e94\u5f02\u5e38\u3002",
    identity: "\u5f53\u524d\u8eab\u4efd\uff1a",
    signOut: "\u9000\u51fa\u767b\u5f55"
  };

  function readToken() {
    try {
      return sessionStorage.getItem(TOKEN_KEY) || "";
    } catch (e) {
      return "";
    }
  }

  function readUser() {
    try {
      return sessionStorage.getItem(USER_KEY) || "";
    } catch (e) {
      return "";
    }
  }

  function writeSession(token, user, role) {
    try {
      sessionStorage.setItem(TOKEN_KEY, token);
      sessionStorage.setItem(USER_KEY, user || "");
      sessionStorage.setItem(ROLE_KEY, String(role == null ? 0 : role));
    } catch (e) {
    }
  }

  function clearSession() {
    try {
      sessionStorage.removeItem(TOKEN_KEY);
      sessionStorage.removeItem(USER_KEY);
      sessionStorage.removeItem(ROLE_KEY);
    } catch (e) {
    }
  }

  function send(method, url, body, token, onDone) {
    var request = new XMLHttpRequest();
    request.open(method, url, true);
    request.setRequestHeader("Content-Type", "application/json; charset=utf-8");
    if (token) {
      request.setRequestHeader("Authorization", "Bearer " + token);
    }
    request.onreadystatechange = function () {
      if (request.readyState != 4) {
        return;
      }
      onDone(request.status, request.responseText || "");
    };
    request.send(body == null ? null : body);
  }

  var overlay = null;
  var overlayDone = null;

  function buildOverlay() {
    var box = document.createElement("div");
    box.id = "dflegacy-payauth";
    box.style.cssText = "display:none;position:fixed;left:0;top:0;right:0;bottom:0;" +
      "background:rgba(0,0,0,.65);z-index:99999;";
    var panel = document.createElement("div");
    panel.style.cssText = "position:absolute;left:50%;top:50%;transform:translate(-50%,-50%);" +
      "width:300px;padding:18px 20px;background:#1b1b1b;border:1px solid #555;color:#ddd;" +
      "font:13px/1.6 'Microsoft YaHei',SimSun,sans-serif;";
    var title = document.createElement("div");
    title.textContent = TEXT.title;
    title.style.cssText = "font-weight:bold;margin-bottom:12px;";
    panel.appendChild(title);

    function labeled(text, type, id) {
      var label = document.createElement("label");
      label.textContent = text;
      var input = document.createElement("input");
      input.type = type;
      input.id = id;
      input.style.cssText = "width:100%;box-sizing:border-box;margin:4px 0 10px;padding:5px;" +
        "background:#111;border:1px solid #555;color:#fff;";
      label.appendChild(input);
      panel.appendChild(label);
      return input;
    }

    var userInput = labeled(TEXT.user, "text", "dflegacy-payauth-user");
    var passInput = labeled(TEXT.pass, "password", "dflegacy-payauth-pass");
    var message = document.createElement("div");
    message.id = "dflegacy-payauth-msg";
    message.style.cssText = "color:#f66;min-height:18px;margin-bottom:10px;";
    panel.appendChild(message);

    var buttons = document.createElement("div");
    buttons.style.cssText = "text-align:right;";
    var cancel = document.createElement("button");
    cancel.type = "button";
    cancel.textContent = TEXT.cancel;
    cancel.style.cssText = "margin-right:8px;padding:4px 14px;";
    var ok = document.createElement("button");
    ok.type = "button";
    ok.textContent = TEXT.ok;
    ok.style.cssText = "padding:4px 14px;";
    buttons.appendChild(cancel);
    buttons.appendChild(ok);
    panel.appendChild(buttons);
    box.appendChild(panel);
    document.body.appendChild(box);

    function submit() {
      var userName = userInput.value.replace(/^\s+|\s+$/g, "");
      var password = passInput.value;
      if (!userName || !password) {
        message.textContent = TEXT.badCreds;
        return;
      }
      ok.disabled = true;
      message.textContent = "";
      login(userName, password, function (success, error) {
        ok.disabled = false;
        if (success) {
          hideOverlay(true);
          return;
        }
        message.textContent = error || TEXT.badCreds;
      });
    }

    ok.onclick = submit;
    cancel.onclick = function () { hideOverlay(false); };
    panel.onkeydown = function (event) {
      if (event.keyCode == 13) {
        submit();
      }
    };
    overlay = { box: box, user: userInput, pass: passInput, msg: message };
  }

  function showLogin(onDone) {
    if (!overlay) {
      buildOverlay();
    }
    overlayDone = onDone;
    overlay.msg.textContent = "";
    overlay.pass.value = "";
    overlay.user.value = readUser();
    overlay.box.style.display = "block";
    overlay.user.focus();
  }

  function hideOverlay(success) {
    if (overlay) {
      overlay.box.style.display = "none";
    }
    var done = overlayDone;
    overlayDone = null;
    if (done) {
      done(!!success);
    }
  }

  function login(userName, password, onDone) {
    send("POST", "/api/auth/login",
      JSON.stringify({ userName: userName, password: password }), "",
      function (status, text) {
        if (status >= 200 && status < 300) {
          var payload = null;
          try {
            payload = JSON.parse(text);
          } catch (e) {
          }
          if (payload && payload.token) {
            writeSession(payload.token, payload.userName, payload.role);
            onDone(true, "");
            return;
          }
          onDone(false, TEXT.badResponse);
          return;
        }
        if (status === 429) {
          onDone(false, TEXT.throttled);
          return;
        }
        if (status === 401) {
          onDone(false, TEXT.badCreds);
          return;
        }
        onDone(false, TEXT.loginFailed + "\uff08HTTP " + status + "\uff09\u3002");
      });
  }

  /* \u5E26\u9274\u6743\u7684 POST\u3002\u56DE\u8C03\u7B7E\u540D\uFF1AonResult(\u6210\u529F?, HTTP \u72B6\u6001\u7801, \u54CD\u5E94\u6587\u672C)\u3002 */
  function post(url, bodyObject, onResult) {
    var body = JSON.stringify(bodyObject);
    var attempt = function (token, retried) {
      send("POST", url, body, token, function (status, text) {
        if (status >= 200 && status < 300) {
          onResult(true, status, text);
          return;
        }
        if ((status === 401 || status === 403) && !retried) {
          showLogin(function (success) {
            if (success) {
              attempt(readToken(), true);
            } else {
              onResult(false, status, text);
            }
          });
          return;
        }
        onResult(false, status, text);
      });
    };
    attempt(readToken(), false);
  }

  window.PayAuth = {
    post: post,
    login: login,
    token: readToken,
    user: readUser,
    clear: clearSession
  };
})();
