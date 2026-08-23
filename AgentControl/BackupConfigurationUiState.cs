namespace AgentControl
{
    internal readonly record struct BackupConfigurationUiState(
        bool EditorEnabled,
        bool DeployEnabled,
        bool EditEnabled,
        bool DeleteEnabled,
        bool RecoveryEnabled)
    {
        public static BackupConfigurationUiState Resolve(
            bool hasSelectedAgent,
            bool hasConfiguration,
            bool isOnline,
            bool hasActiveSession,
            bool isEditing,
            bool isBusy)
        {
            if (!hasSelectedAgent)
            {
                return default;
            }

            if (!hasConfiguration)
            {
                return isBusy
                    ? default
                    : new BackupConfigurationUiState(
                        EditorEnabled: true,
                        DeployEnabled: true,
                        EditEnabled: false,
                        DeleteEnabled: false,
                        RecoveryEnabled: false);
            }

            bool canManage = isOnline && !hasActiveSession && !isBusy;
            if (isEditing)
            {
                return new BackupConfigurationUiState(
                    EditorEnabled: !isBusy,
                    DeployEnabled: canManage,
                    EditEnabled: false,
                    DeleteEnabled: false,
                    RecoveryEnabled: !isBusy);
            }

            return new BackupConfigurationUiState(
                EditorEnabled: false,
                DeployEnabled: false,
                EditEnabled: canManage,
                DeleteEnabled: canManage,
                RecoveryEnabled: !isBusy);
        }
    }
}
