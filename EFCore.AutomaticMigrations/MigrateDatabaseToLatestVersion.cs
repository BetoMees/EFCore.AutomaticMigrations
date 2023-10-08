using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace EFCore.AutomaticMigrations;

public sealed class MigrateDatabaseToLatestVersion
{
	public static DbMigrationsOptions DbMigrationsOptions { get; set; } = new DbMigrationsOptions();


	private MigrateDatabaseToLatestVersion()
	{
	}

	public static void Execute<TContext>(TContext context) where TContext : DbContext
	{
		new Migrate<TContext, DbMigrationsOptions>(context, DbMigrationsOptions).RunMigrations();
	}

	public static void Execute<TContext, TMigrationsOptions>(TContext context, TMigrationsOptions options) where TContext : DbContext where TMigrationsOptions : DbMigrationsOptions
	{
		new Migrate<TContext, TMigrationsOptions>(context, options).RunMigrations();
	}

	public static async Task ExecuteAsync<TContext>(TContext context) where TContext : DbContext
	{
		await new Migrate<TContext, DbMigrationsOptions>(context, DbMigrationsOptions).RunMigrationsAsync();
	}

	public static async Task ExecuteAsync<TContext, TMigrationsOptions>(TContext context, TMigrationsOptions options) where TContext : DbContext where TMigrationsOptions : DbMigrationsOptions
	{
		await new Migrate<TContext, TMigrationsOptions>(context, options).RunMigrationsAsync();
	}
}
