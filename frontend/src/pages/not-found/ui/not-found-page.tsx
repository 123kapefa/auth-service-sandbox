import { Link } from "react-router";

export function NotFoundPage() {
  return (
    <div className="py-16 text-center">
      <h1 className="mb-3 text-3xl font-bold tracking-tight">Страница не найдена</h1>
      <Link to="/" className="text-sm underline">
        На главную
      </Link>
    </div>
  );
}
