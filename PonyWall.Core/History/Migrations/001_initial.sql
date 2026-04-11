CREATE TABLE IF NOT EXISTS meta (
    key TEXT PRIMARY KEY,
    value TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS rulesets (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    timestamp_utc_ms INTEGER NOT NULL,
    content_hash TEXT NOT NULL UNIQUE,
    content_json BLOB NOT NULL
);

CREATE TABLE IF NOT EXISTS events_hot (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    decision_id TEXT NOT NULL,
    flow_id INTEGER NOT NULL,
    timestamp_utc_ms INTEGER NOT NULL,
    action INTEGER NOT NULL,
    direction INTEGER NOT NULL,
    protocol INTEGER NOT NULL,
    local_ip TEXT,
    local_port INTEGER,
    remote_ip TEXT,
    remote_port INTEGER,
    pid INTEGER,
    app_path TEXT,
    app_name TEXT,
    package_sid TEXT,
    service_name TEXT,
    mode_at_event INTEGER NOT NULL,
    ruleset_id INTEGER NOT NULL,
    reason_id INTEGER NOT NULL DEFAULT 0,
    confidence INTEGER NOT NULL DEFAULT 0,
    matched_rule_id TEXT,
    near_miss_rule_ids TEXT,
    schema_version INTEGER NOT NULL,
    FOREIGN KEY (ruleset_id) REFERENCES rulesets(id)
);

CREATE INDEX IF NOT EXISTS idx_events_hot_ts ON events_hot(timestamp_utc_ms DESC, action);
CREATE INDEX IF NOT EXISTS idx_events_hot_app ON events_hot(app_path, timestamp_utc_ms DESC);
CREATE INDEX IF NOT EXISTS idx_events_hot_decision ON events_hot(decision_id);

CREATE TABLE IF NOT EXISTS events_warm (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    timestamp_utc_ms INTEGER NOT NULL,
    action INTEGER NOT NULL,
    reason_id INTEGER NOT NULL,
    app_name TEXT,
    remote_ip TEXT,
    remote_port INTEGER,
    protocol INTEGER NOT NULL,
    ruleset_id INTEGER NOT NULL,
    schema_version INTEGER NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_events_warm_ts ON events_warm(timestamp_utc_ms DESC);
CREATE INDEX IF NOT EXISTS idx_events_warm_app ON events_warm(app_name, timestamp_utc_ms DESC);

INSERT OR IGNORE INTO meta (key, value) VALUES ('schema_version', '1');
