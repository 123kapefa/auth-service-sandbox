import { useEffect, useRef, useState } from "react";

/** Ключ сайта у заглушки капчи стенда (плагин, CaptchaVerifier.STUB_SITE_KEY) и токен, который она пропускает. */
export const STUB_SITE_KEY = "sandbox-stub";
const STUB_TOKEN = "sandbox-captcha-ok";
const SCRIPT = "https://smartcaptcha.yandexcloud.net/captcha.js";

type SmartCaptcha = {
  render: (
    container: HTMLElement,
    params: { sitekey: string; hl: string; callback: (token: string) => void },
  ) => number;
};

declare global {
  interface Window {
    smartCaptcha?: SmartCaptcha;
  }
}

/**
 * Капча перед отправкой кода (10.1): виджет Яндекс SmartCaptcha — ответ уходит полем smart-token той формы, в которой
 * стоит виджет; на стенде (ключ sandbox-stub) — галочка «Я не робот» в рамке, как виджет. onToken — капча пройдена
 * (кнопку отправки можно включить).
 */
export function Captcha({
  siteKey,
  language,
  label,
  stubNote,
  onToken,
}: {
  siteKey: string;
  language: string;
  label: string;
  stubNote: string;
  onToken: (token: string) => void;
}) {
  if (siteKey === STUB_SITE_KEY) {
    return <StubCaptcha label={label} note={stubNote} onToken={onToken} />;
  }
  return <SmartCaptchaWidget siteKey={siteKey} language={language} onToken={onToken} />;
}

function StubCaptcha({
  label,
  note,
  onToken,
}: {
  label: string;
  note: string;
  onToken: (token: string) => void;
}) {
  const [checked, setChecked] = useState(false);
  return (
    <label className="flex cursor-pointer items-center gap-3 border border-border bg-background px-3.5 py-3 text-[15px]">
      <input
        id="platform-captcha-stub"
        type="checkbox"
        checked={checked}
        onChange={(event) => {
          setChecked(event.target.checked);
          onToken(event.target.checked ? STUB_TOKEN : "");
        }}
      />
      <span className="flex-1">{label}</span>
      <span className="text-right font-mono text-[10px] leading-tight tracking-[0.08em] text-muted-foreground uppercase">
        {note}
      </span>
      {checked && <input type="hidden" name="smart-token" value={STUB_TOKEN} />}
    </label>
  );
}

function SmartCaptchaWidget({
  siteKey,
  language,
  onToken,
}: {
  siteKey: string;
  language: string;
  onToken: (token: string) => void;
}) {
  const container = useRef<HTMLDivElement>(null);
  const callback = useRef(onToken);
  useEffect(() => {
    callback.current = onToken;
  }, [onToken]);

  useEffect(() => {
    let cancelled = false;
    const render = () => {
      if (cancelled || !container.current || !window.smartCaptcha) return;
      // Виджет сам кладёт в форму скрытое поле smart-token.
      window.smartCaptcha.render(container.current, {
        sitekey: siteKey,
        hl: language,
        callback: (token) => callback.current(token),
      });
    };
    if (window.smartCaptcha) {
      render();
    } else {
      let script = document.querySelector<HTMLScriptElement>(`script[src^="${SCRIPT}"]`);
      if (!script) {
        script = document.createElement("script");
        script.src = SCRIPT;
        script.defer = true;
        document.head.appendChild(script);
      }
      script.addEventListener("load", render);
    }
    return () => {
      cancelled = true;
    };
  }, [siteKey, language]);

  return <div id="platform-captcha" ref={container} className="min-h-[100px]" />;
}
