-- =============================================================================
-- Agentiva — relational database provisioning
--
-- One database per service. The isolation is the point: a service physically
-- cannot query another service's tables, so a schema change is contained to the
-- service that owns it and cross-service coupling has to go through a published
-- contract rather than a convenient join.
--
-- Runs once, on an empty data directory. Changing it afterwards requires
-- recreating the volume: `docker compose down --volumes`.
--
-- Note on scope: the engineering specification lists nine databases. This file
-- creates twelve, adding execution_db, reconciliation_db and notification_db,
-- because the microservice rule that every service owns its own database takes
-- precedence over the illustrative list — those three services would otherwise
-- have nowhere to put their own state. See docs/database/schema.md.
-- =============================================================================

\set ON_ERROR_STOP on

-- The "agentiva" role is created by the image entrypoint from POSTGRES_USER,
-- so it already exists here. It owns every database for local development only;
-- production issues a separate least-privilege role per service, each able to
-- reach only its own database. See docs/security/database-access.md.

CREATE DATABASE identity_db        OWNER agentiva ENCODING 'UTF8';
CREATE DATABASE trading_db         OWNER agentiva ENCODING 'UTF8';
CREATE DATABASE risk_db            OWNER agentiva ENCODING 'UTF8';
CREATE DATABASE execution_db       OWNER agentiva ENCODING 'UTF8';
CREATE DATABASE portfolio_db       OWNER agentiva ENCODING 'UTF8';
CREATE DATABASE strategy_db        OWNER agentiva ENCODING 'UTF8';
CREATE DATABASE agent_db           OWNER agentiva ENCODING 'UTF8';
CREATE DATABASE audit_db           OWNER agentiva ENCODING 'UTF8';
CREATE DATABASE backtesting_db     OWNER agentiva ENCODING 'UTF8';
CREATE DATABASE configuration_db   OWNER agentiva ENCODING 'UTF8';
CREATE DATABASE reconciliation_db  OWNER agentiva ENCODING 'UTF8';
CREATE DATABASE notification_db    OWNER agentiva ENCODING 'UTF8';

-- Every database gets the same baseline settings.
DO $$
DECLARE
    db text;
BEGIN
    FOREACH db IN ARRAY ARRAY[
        'identity_db','trading_db','risk_db','execution_db','portfolio_db',
        'strategy_db','agent_db','audit_db','backtesting_db','configuration_db',
        'reconciliation_db','notification_db'
    ]
    LOOP
        -- UTC everywhere. A service interpreting "today" in a local timezone
        -- would attribute a trade to the wrong trading day and so compare it
        -- against the wrong daily loss budget.
        EXECUTE format('ALTER DATABASE %I SET timezone TO ''UTC''', db);

        -- Fail a statement that waits too long for a lock rather than letting
        -- it block a migration or a trading write indefinitely.
        EXECUTE format('ALTER DATABASE %I SET lock_timeout TO ''10s''', db);
        EXECUTE format('ALTER DATABASE %I SET idle_in_transaction_session_timeout TO ''60s''', db);
    END LOOP;
END
$$;
