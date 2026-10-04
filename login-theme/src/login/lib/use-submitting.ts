import { useState } from "react";

/**
 * «Идёт отправка» — чтобы заблокировать кнопки от второго нажатия. Отмечается после того, как браузер собрал данные
 * формы: React применяет состояние сразу после onSubmit, а заблокированная кнопка в отправку не попадает — сервер не
 * узнал бы, какой нажали («Войти по коду», «Отправить ещё раз»).
 */
export function useSubmitting() {
  const [submitting, setSubmitting] = useState(false);
  const markSubmitting = () => setTimeout(() => setSubmitting(true), 0);
  return { submitting, markSubmitting, setSubmitting };
}
