import { useParams } from "react-router";
import { AuditLog } from "@/widgets/audit-log";

/** Журнал приложения: действия с его ролями, правами, ролями пользователей и доступом. */
export function AppJournalPage() {
  const { clientId = "" } = useParams();
  return <AuditLog clientId={clientId} />;
}
