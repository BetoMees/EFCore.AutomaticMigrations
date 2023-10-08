using Microsoft.EntityFrameworkCore.Design.Internal;

namespace EFCore.AutomaticMigrations;

public abstract class MigrateOperation : IOperationReporter
{
	void IOperationReporter.WriteError(string message)
	{
	}

	void IOperationReporter.WriteInformation(string message)
	{
	}

	void IOperationReporter.WriteVerbose(string message)
	{
	}

	void IOperationReporter.WriteWarning(string message)
	{
	}
}
