using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace EFCore.AutomaticMigrations;

public static class MigrateDatabaseToLatestVersionExtensions
{
	public static DbMigrationsOptions DbMigrationsOptions { get; set; } = new DbMigrationsOptions();


	public static void MigrateToLatestVersion<TContext>(this TContext context) where TContext : DbContext
	{
		new Migrate<TContext, DbMigrationsOptions>(context, DbMigrationsOptions).RunMigrations();
	}

	public static void MigrateToLatestVersion<TContext, TMigrationsOptions>(this TContext context, TMigrationsOptions options) where TContext : DbContext where TMigrationsOptions : DbMigrationsOptions
	{
		new Migrate<TContext, TMigrationsOptions>(context, options).RunMigrations();
	}

	public static async Task MigrateToLatestVersionAsync<TContext>(this TContext context) where TContext : DbContext
	{
		await new Migrate<TContext, DbMigrationsOptions>(context, DbMigrationsOptions).RunMigrationsAsync();
	}

	public static async Task MigrateToLatestVersionAsync<TContext, TMigrationsOptions>(this TContext context, TMigrationsOptions options) where TContext : DbContext where TMigrationsOptions : DbMigrationsOptions
	{
		await new Migrate<TContext, TMigrationsOptions>(context, options).RunMigrationsAsync();
	}

	[Obsolete("ListMigrations method is deprecated and will be removed in next release, please use ListAppliedMigrations instead.")]
	public static List<MigrationRaw> ListMigrations<TContext>(this TContext context) where TContext : DbContext
	{
		return new Migrate<TContext, DbMigrationsOptions>(context, DbMigrationsOptions).ListMigrations();
	}

	public static List<MigrationRaw> ListAppliedMigrations<TContext>(this TContext context) where TContext : DbContext
	{
		return new Migrate<TContext, DbMigrationsOptions>(context, DbMigrationsOptions).ListMigrations();
	}

	[Obsolete("ListMigrationsAsync is deprecated and will be removed in next release, please use ListAppliedMigrationsAsync instead.")]
	public static async Task<List<MigrationRaw>> ListMigrationsAsync<TContext>(this TContext context) where TContext : DbContext
	{
		return await new Migrate<TContext, DbMigrationsOptions>(context, DbMigrationsOptions).ListMigrationsAsync();
	}

	public static async Task<List<MigrationRaw>> ListAppliedMigrationsAsync<TContext>(this TContext context) where TContext : DbContext
	{
		return await new Migrate<TContext, DbMigrationsOptions>(context, DbMigrationsOptions).ListMigrationsAsync();
	}

	public static List<MigrationOperation> ListMigrationOperationsAsRawSql<TContext>(this TContext context) where TContext : DbContext
	{
		return new Migrate<TContext, DbMigrationsOptions>(context, DbMigrationsOptions).ListMigrationOperationsAsRawSql();
	}

	public static async Task<List<MigrationOperation>> ListMigrationOperationsAsRawSqlAsync<TContext>(this TContext context) where TContext : DbContext
	{
		return await new Migrate<TContext, DbMigrationsOptions>(context, DbMigrationsOptions).ListMigrationOperationsAsRawSqlAsync();
	}
}
