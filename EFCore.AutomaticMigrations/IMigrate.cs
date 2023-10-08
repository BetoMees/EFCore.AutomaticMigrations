using System.Threading.Tasks;

namespace EFCore.AutomaticMigrations;

public interface IMigrate
{
	void RunMigrations();

	Task RunMigrationsAsync();
}
