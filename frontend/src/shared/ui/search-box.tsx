import { type FormEvent } from "react";
import { Button } from "./button";
import { Input } from "./input";

interface SearchBoxProps {
  value: string;
  placeholder: string;
  label: string;
  onSearch: (value: string) => void;
}

/** Поиск по кнопке или Enter (не на каждую букву: каждый поиск — запрос в Keycloak). */
export function SearchBox({ value, placeholder, label, onSearch }: SearchBoxProps) {
  const submit = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    onSearch(String(new FormData(event.currentTarget).get("search") ?? "").trim());
  };

  return (
    <form role="search" onSubmit={submit} className="flex max-w-xl gap-2">
      <Input
        name="search"
        defaultValue={value}
        placeholder={placeholder}
        aria-label={label}
        key={value}
      />
      <Button type="submit">Найти →</Button>
    </form>
  );
}
