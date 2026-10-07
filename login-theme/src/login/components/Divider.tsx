/** Разделитель «или» между основным действием и другими способами — моноширинный, капсом, с линиями. */
export function Divider({ label }: { label: string }) {
  return (
    <p className="my-4 flex items-center gap-3 font-mono text-[11px] tracking-[0.1em] text-muted-foreground uppercase before:h-px before:flex-1 before:bg-border after:h-px after:flex-1 after:bg-border">
      {label}
    </p>
  );
}
