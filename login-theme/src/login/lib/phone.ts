/**
 * Поле «Телефон или почта». Логин-телефон хранится цифрами без «+» (79990000001), а человек видит и вводит его
 * привычно: +7 (999) 000-00-01. Российский номер можно набрать как угодно — с 8, с 7 или 10 цифр без кода: в поле
 * появится +7 и маска. Окончательно номер приводит к логину сервер (8XXX → 7XXX, 10 цифр → 7XXX) — здесь только вид.
 */

/** Похоже на номер: только цифры, «+», пробелы, скобки и дефисы, и есть хотя бы одна цифра. */
const PHONE_LIKE = /^[+\d\s()-]*\d[+\d\s()-]*$/;

/** Цифры номера с кодом страны: 8XXXXXXXXXX и 10 цифр на 9 — российский номер с 7. */
function countryDigits(digits: string, typedPlus: boolean): string {
  if (!typedPlus && digits.startsWith("8")) {
    return `7${digits.slice(1)}`;
  }

  if (!typedPlus && digits.startsWith("9")) {
    return `7${digits}`;
  }

  return digits;
}

/** +7 (999) 000-00-01 — по мере набора; другие страны — «+» и цифры. */
function mask(digits: string): string {
  if (!digits.startsWith("7")) {
    return `+${digits}`;
  }

  const rest = digits.slice(1, 11);
  const parts = [rest.slice(0, 3), rest.slice(3, 6), rest.slice(6, 8), rest.slice(8, 10)];
  let out = "+7";
  if (parts[0]) {
    out += ` (${parts[0]}${parts[0].length === 3 ? ")" : ""}`;
  }

  if (parts[1]) {
    out += ` ${parts[1]}`;
  }

  if (parts[2]) {
    out += `-${parts[2]}`;
  }

  if (parts[3]) {
    out += `-${parts[3]}`;
  }

  return out;
}

/** Как показать введённое: номер — с «+» и маской, почта и прочее — как есть, стёртый номер — пусто. */
export function formatLogin(value: string): string {
  if (!PHONE_LIKE.test(value)) {
    return value === "+" ? "" : value;
  }

  const digits = value.replace(/\D/g, "");
  return digits ? mask(countryDigits(digits, value.trimStart().startsWith("+"))) : "";
}

/** Что отправить серверу: номер — только цифры (с кодом страны), почта и прочее — как есть. */
export function toLogin(value: string): string {
  if (!PHONE_LIKE.test(value)) {
    return value;
  }

  return countryDigits(value.replace(/\D/g, ""), value.trimStart().startsWith("+"));
}

/**
 * Новое значение поля после правки. Стёрли знак маски (скобку, пробел, дефис) — стирается и цифра перед ним, иначе
 * маска вернула бы знак на место и Backspace «застревал».
 */
export function nextLogin(previous: string, typed: string): string {
  const digits = (value: string) => value.replace(/\D/g, "");
  const erasedOnlyMask =
    typed.length < previous.length &&
    PHONE_LIKE.test(previous) &&
    digits(typed) === digits(previous);
  return formatLogin(erasedOnlyMask ? typed.replace(/\d(?=\D*$)/, "") : typed);
}
