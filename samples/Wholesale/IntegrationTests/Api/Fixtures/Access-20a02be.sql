DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'access') THEN
        CREATE SCHEMA access;
    END IF;
END $EF$;
CREATE TABLE IF NOT EXISTS access."__EFMigrationsHistory" (
    "MigrationId" character varying(150) NOT NULL,
    "ProductVersion" character varying(32) NOT NULL,
    CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId")
);

START TRANSACTION;
DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'access') THEN
        CREATE SCHEMA access;
    END IF;
END $EF$;

CREATE TABLE access.organizations (
    id character varying(256) NOT NULL,
    slug character varying(63) NOT NULL,
    CONSTRAINT "PK_organizations" PRIMARY KEY (id),
    CONSTRAINT ck_organizations_slug CHECK (slug ~ '^[a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?$')
);

CREATE TABLE access.users (
    id character varying(256) NOT NULL,
    CONSTRAINT "PK_users" PRIMARY KEY (id)
);

CREATE TABLE access.external_identities (
    issuer character varying(512) NOT NULL,
    subject character varying(255) NOT NULL,
    user_id character varying(256) NOT NULL,
    CONSTRAINT "PK_external_identities" PRIMARY KEY (issuer, subject),
    CONSTRAINT "FK_external_identities_users_user_id" FOREIGN KEY (user_id) REFERENCES access.users (id) ON DELETE RESTRICT
);

CREATE TABLE access.memberships (
    id uuid NOT NULL,
    organization_id character varying(256) NOT NULL,
    user_id character varying(256) NOT NULL,
    status integer NOT NULL,
    CONSTRAINT "PK_memberships" PRIMARY KEY (id),
    CONSTRAINT ck_memberships_status CHECK (status IN (1, 2, 3)),
    CONSTRAINT "FK_memberships_organizations_organization_id" FOREIGN KEY (organization_id) REFERENCES access.organizations (id) ON DELETE RESTRICT,
    CONSTRAINT "FK_memberships_users_user_id" FOREIGN KEY (user_id) REFERENCES access.users (id) ON DELETE RESTRICT
);

CREATE INDEX "IX_external_identities_user_id" ON access.external_identities (user_id);

CREATE UNIQUE INDEX "IX_memberships_organization_id_user_id" ON access.memberships (organization_id, user_id) WHERE status IN (1, 2);

CREATE INDEX "IX_memberships_user_id" ON access.memberships (user_id);

CREATE UNIQUE INDEX "IX_organizations_slug" ON access.organizations (slug);

INSERT INTO access."__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261005204257_InitialAccess', '10.0.12');

COMMIT;

