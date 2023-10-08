namespace EFCore.AutomaticMigrations;

internal class AutoMigrateResponse
{
	public string Snapshot { get; set; } = string.Empty;

	public string MigrationId { get; set; } = string.Empty;

}
