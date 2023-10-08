using System;

namespace EFCore.AutomaticMigrations;

public class MigrationRaw
{
	public string MigrationId { get; set; } = string.Empty;


	public DateTime? CreatedDate { get; set; }
}
