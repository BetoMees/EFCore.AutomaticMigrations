using System;

namespace EFCore.AutomaticMigrations;

public class MigrateDatabaseException : Exception
{
	public MigrateDatabaseException()
	{
	}

	public MigrateDatabaseException(string message)
		: base(message)
	{
	}

	public MigrateDatabaseException(string message, Exception innerException)
		: base(message, innerException)
	{
	}
}
