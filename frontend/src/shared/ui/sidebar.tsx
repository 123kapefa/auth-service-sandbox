import { type ReactNode, useState } from "react";
import { NavLink } from "react-router";
import { cn } from "@/shared/lib/cn";

/**
 * Каркас с боковым меню: каркас ровно в экран, прокручивается только содержимое (меню на месте и не тянется за
 * длинной страницей, края не оттягиваются); на узком экране — шапка с кнопкой «☰», меню раскрывается над
 * содержимым, прокручивается вся страница.
 */
export function SidebarLayout({ sidebar, children }: { sidebar: ReactNode; children: ReactNode }) {
  return (
    <div className="flex min-h-dvh flex-col bg-background text-foreground md:h-dvh md:flex-row md:overflow-hidden">
      {sidebar}
      <main className="min-w-0 flex-1 px-4 py-8 md:overflow-y-auto md:overscroll-none md:px-10 md:py-10">
        <div className="max-w-6xl">{children}</div>
      </main>
    </div>
  );
}

export function Sidebar({
  title,
  subtitle,
  logo,
  footer,
  children,
}: {
  title: string;
  /** Подпись под названием (в кабинете — «Личный кабинет» под названием проекта). */
  subtitle?: string;
  /** Логотип проекта вместо квадрата цветом. */
  logo?: string | null;
  footer: ReactNode;
  children: ReactNode;
}) {
  // На узком экране меню свёрнуто под «☰»; выбрали раздел — сворачивается.
  const [open, setOpen] = useState(false);
  const folded = open ? "flex" : "hidden md:flex";
  return (
    <aside
      aria-label="Меню"
      className="flex flex-col gap-6 border-b-2 border-primary bg-card px-3 py-5 md:w-64 md:shrink-0 md:overflow-y-auto md:overscroll-none md:border-r-2 md:border-b-0"
    >
      <div className="flex items-center gap-2.5 px-3">
        {logo ? (
          <img src={logo} alt="" className="h-7 max-w-20 shrink-0 object-contain" />
        ) : (
          <span className="size-3.5 shrink-0 bg-accent" aria-hidden />
        )}
        <div className="grid min-w-0">
          <span className="truncate text-[17px] font-bold tracking-tight">{title}</span>
          {subtitle && (
            <span className="font-mono text-[10px] tracking-[0.16em] text-muted-foreground uppercase">
              {subtitle}
            </span>
          )}
        </div>
        <button
          type="button"
          aria-expanded={open}
          aria-controls="sidebar-menu"
          aria-label={open ? "Свернуть меню" : "Меню"}
          onClick={() => setOpen(!open)}
          className="ml-auto grid size-10 shrink-0 place-content-center border border-primary text-lg hover:bg-muted focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring md:hidden"
        >
          <span aria-hidden>{open ? "×" : "☰"}</span>
        </button>
      </div>
      <nav
        id="sidebar-menu"
        aria-label="Разделы"
        className={cn("flex-col gap-5", folded)}
        onClick={(event) => {
          if ((event.target as HTMLElement).closest("a")) {
            setOpen(false);
          }
        }}
      >
        {children}
      </nav>
      <div
        className={cn(
          "mt-auto flex-col gap-3 border-t border-dotted border-border px-3 pt-4",
          folded,
        )}
      >
        {footer}
      </div>
    </aside>
  );
}

export function SidebarGroup({ label, children }: { label?: string; children: ReactNode }) {
  return (
    <div>
      {label && (
        <div className="px-3 pb-2 font-mono text-[10px] tracking-[0.16em] text-muted-foreground uppercase">
          {label}
        </div>
      )}
      <ul className="flex flex-col gap-1">{children}</ul>
    </div>
  );
}

/** Пункт меню: номер моноширинным; активный — залит, с жёсткой тенью. */
export function SidebarLink({
  to,
  number,
  end,
  children,
  sub,
}: {
  to: string;
  number: number;
  end?: boolean;
  children: ReactNode;
  /** Вложенные пункты (разделы приложения) — под пунктом. */
  sub?: ReactNode;
}) {
  return (
    <li>
      <NavLink
        to={to}
        end={end}
        className={({ isActive }) =>
          cn(
            "group flex items-baseline gap-2.5 px-3 py-2.5 text-[15px] hover:bg-background",
            isActive &&
              "bg-accent font-semibold text-accent-foreground shadow-hard hover:bg-accent",
          )
        }
      >
        <span className="w-[18px] shrink-0 font-mono text-[11px] text-muted-foreground group-aria-[current=page]:text-accent-foreground">
          {String(number).padStart(2, "0")}
        </span>
        {children}
      </NavLink>
      {sub && <ul className="mt-1.5 mb-1 flex flex-col">{sub}</ul>}
    </li>
  );
}

export function SidebarSubLink({ to, children }: { to: string; children: ReactNode }) {
  return (
    <li>
      <NavLink
        to={to}
        className={({ isActive }) =>
          cn(
            "block py-1.5 pr-3 pl-10 text-sm text-muted-foreground hover:text-foreground",
            isActive && "border-l-[3px] border-accent pl-[37px] font-semibold text-foreground",
          )
        }
      >
        {children}
      </NavLink>
    </li>
  );
}
