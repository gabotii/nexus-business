using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexus.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Privilege model (ADR-0010). Runs as the schema owner. Every table or sequence the owner creates from now on is
    /// automatically usable by the runtime role for DML only; the runtime role can never create, alter or drop objects.
    /// Append-only tables revoke UPDATE/DELETE in their own migrations.
    /// </summary>
    public partial class Baseline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'nexus_app') THEN
                        RAISE EXCEPTION 'The runtime role nexus_app must exist before migrating (see docs/adr/0010).';
                    END IF;
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'nexus_app' AND (rolsuper OR rolbypassrls)) THEN
                        RAISE EXCEPTION 'The runtime role nexus_app must not be SUPERUSER or BYPASSRLS (see docs/adr/0004).';
                    END IF;
                END
                $$;

                REVOKE ALL ON SCHEMA public FROM PUBLIC;
                GRANT USAGE ON SCHEMA public TO nexus_app;

                ALTER DEFAULT PRIVILEGES IN SCHEMA public
                    GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO nexus_app;
                ALTER DEFAULT PRIVILEGES IN SCHEMA public
                    GRANT USAGE, SELECT ON SEQUENCES TO nexus_app;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER DEFAULT PRIVILEGES IN SCHEMA public
                    REVOKE USAGE, SELECT ON SEQUENCES FROM nexus_app;
                ALTER DEFAULT PRIVILEGES IN SCHEMA public
                    REVOKE SELECT, INSERT, UPDATE, DELETE ON TABLES FROM nexus_app;
                REVOKE USAGE ON SCHEMA public FROM nexus_app;
                -- PostgreSQL 15+ default for the public schema.
                GRANT USAGE ON SCHEMA public TO PUBLIC;
                """);
        }
    }
}
