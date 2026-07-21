using System.Text.Json.Nodes;

namespace RecMode.Core.Settings;

/// <summary>
/// Upgrades a settings JSON document from an older schema version to <see cref="RecModeSettings.CurrentSchemaVersion"/>
/// (plan §3.8 — schema migration tests, v1→vN fixtures). Runs on the raw <see cref="JsonObject"/> before
/// deserialization so old field names/shapes can be remapped. Each version bump adds one step here.
/// </summary>
public static class SettingsMigrator
{
    /// <summary>
    /// Migrates <paramref name="root"/> in place to the current schema. Returns true if anything changed.
    /// Unknown-but-newer versions are left untouched (forward compatibility handled by the deserializer).
    /// </summary>
    public static bool Migrate(JsonObject root)
    {
        ArgumentNullException.ThrowIfNull(root);

        int version = root["SchemaVersion"]?.GetValue<int>() ?? 0;
        int start = version;

        // Future steps go here, e.g.:
        //   if (version == 1) { MigrateV1ToV2(root); version = 2; }
        // Each step transforms the object and increments `version`.

        if (version != RecModeSettings.CurrentSchemaVersion)
        {
            // Clamp forward: a version-0 (pre-versioning) or partially-migrated doc adopts the current
            // version once the known steps have run. Genuinely newer docs keep their higher number.
            if (version < RecModeSettings.CurrentSchemaVersion)
            {
                root["SchemaVersion"] = RecModeSettings.CurrentSchemaVersion;
                version = RecModeSettings.CurrentSchemaVersion;
            }
        }

        return version != start;
    }
}
