import { Link } from "react-router";

export function NotFoundPage() {
  return (
    <div className="py-16 text-center">
      <h1 className="mb-2 text-lg font-semibold">Страница не найдена</h1>
      <Link to="/" className="text-sm underline">
        На главную
      </Link>
    </div>
  );
}
