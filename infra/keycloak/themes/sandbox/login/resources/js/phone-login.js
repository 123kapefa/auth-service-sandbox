// Поле «Телефон или почта»: логин-телефон хранится цифрами без «+» (79990000001),
// а пользователь видит и может вводить его привычно: +7 (999) 000-00-01.
// - начали вводить номер — перед ним появляется «+»;
// - почта не трогается;
// - перед отправкой номер приводится к цифрам.
(function () {
  "use strict";

  // Похоже на номер: только цифры, «+», пробелы, скобки и дефисы, и есть хотя бы одна цифра.
  var PHONE_LIKE = /^[+\d\s()\-]*\d[+\d\s()\-]*$/;

  function showPlus(input) {
    var value = input.value;
    if (/^\d/.test(value)) {
      input.value = "+" + value;
    } else if (value === "+") {
      input.value = ""; // стёрли номер целиком — не оставляем одинокий «+»
    }
  }

  function toDigits(input) {
    if (PHONE_LIKE.test(input.value)) {
      input.value = input.value.replace(/\D/g, "");
    }
  }

  function attach(input) {
    showPlus(input); // значение после неудачной попытки входа тоже показываем с «+»
    input.addEventListener("input", function () { showPlus(input); });
    if (input.form) {
      input.form.addEventListener("submit", function () { toDigits(input); });
    }
  }

  document.addEventListener("DOMContentLoaded", function () {
    var input = document.getElementById("username");
    if (input) {
      attach(input);
    }
  });
})();
