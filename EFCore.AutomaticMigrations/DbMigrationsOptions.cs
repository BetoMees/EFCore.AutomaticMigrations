using System.Collections.Generic;

namespace EFCore.AutomaticMigrations;

public class DbMigrationsOptions
{
	public bool AutomaticMigrationDataLossAllowed { get; set; }

	public bool AutomaticMigrationsEnabled { get; set; } = true;


	public bool ResetDatabaseSchema { get; set; }

	public Dictionary<string, string> UpdateSnapshot { get; set; } = new Dictionary<string, string>();

}
