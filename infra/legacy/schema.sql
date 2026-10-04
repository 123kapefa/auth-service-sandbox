-- «Боевая» таблица пользователей для спайка этапа 6 — схема как у боевого auth-service
-- (Auth.Infrastructure, AuthDbContextModelSnapshot: сущность User → таблица users). Только для песочницы.
SET client_min_messages = warning;
DROP TABLE IF EXISTS users;

CREATE TABLE users (
    "Id"                  uuid                        NOT NULL PRIMARY KEY,
    "Phone"               character varying(20)       NOT NULL,          -- E.164: +79991110001
    "Role"                character varying(20)       NOT NULL DEFAULT 'customer',
    "IsRestricted"        boolean                     NOT NULL DEFAULT false,
    "RestrictionReason"   character varying(500),
    "RestrictedAtUtc"     timestamp with time zone,
    "LastLoginAtUtc"      timestamp with time zone,
    "LoginCount"          integer                     NOT NULL DEFAULT 0,
    "PasswordHash"        character varying(256),                        -- BCrypt.Net, factor 12; NULL — входит только по коду
    "PasswordSetAtUtc"    timestamp with time zone,
    "FailedLoginAttempts" integer                     NOT NULL DEFAULT 0,
    "LockedUntilUtc"      timestamp with time zone,
    "CreatedAt"           timestamp with time zone    NOT NULL,
    "UpdatedAt"           timestamp with time zone    NOT NULL
);

CREATE UNIQUE INDEX "IX_users_Phone" ON users ("Phone");
CREATE INDEX "IX_users_IsRestricted" ON users ("IsRestricted") WHERE "IsRestricted" = true;
