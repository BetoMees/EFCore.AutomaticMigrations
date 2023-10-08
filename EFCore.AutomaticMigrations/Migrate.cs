using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime;
using System.Runtime.Loader;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design.Internal;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Design;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyModel;

namespace EFCore.AutomaticMigrations;

internal class Migrate<TContext, TMigrationsConfiguration> : MigrateOperation, IMigrate where TContext : DbContext where TMigrationsConfiguration : DbMigrationsOptions
{
    private readonly DbContext _context;

    private readonly DbMigrationsOptions _options;

    internal Migrate(TContext context, TMigrationsConfiguration options)
    {
        _context = context;
        _options = options ?? new DbMigrationsOptions();
    }

    public void RunMigrations()
    {
        try
        {
            if (!_options.AutomaticMigrationsEnabled)
            {
                return;
            }
            IMigrationsAssembly service = _context.GetService<IMigrationsAssembly>();
            IModel model = _context.GetService<IDesignTimeModel>().Model;
            ModelSnapshot modelSnapshot = null;
            bool canConnect = _context.Database.CanConnect();
            if (!canConnect)
            {
                _options.ResetDatabaseSchema = false;
            }
            if (_options.ResetDatabaseSchema)
            {
                SnapshotOperation.ExecuteResetDatabaseSchema(_context, canConnect);
            }
            else
            {
                SnapshotOperation.EnsureSnapshotTableIsCreated(_context, canConnect);
                List<string> list = _context.Database.GetAppliedMigrations().ToList();
                IEnumerable<string> migrations = _context.Database.GetMigrations();
                bool toMigrate = _options.AutomaticMigrationsEnabled || migrations.Intersect(list).Any();
                bool hasPendence = toMigrate && migrations.Except(list).Any();
                string text = list.Except(migrations).LastOrDefault();
                if (text != null)
                {
                    if (hasPendence)
                    {
                        throw new InvalidOperationException("Automatic migrations can not run. You  have to restore from a release database.");
                    }
                    string source = SnapshotOperation.ReadSnapshotSource(_context) ?? throw new InvalidOperationException("Model Snapshot for " + text + " could not be found in the database");
                    modelSnapshot = CompileSnapshot(service.Assembly, source);
                }
                else if (toMigrate)
                {
                    if (hasPendence)
                    {
                        _context.Database.Migrate();
                    }
                    modelSnapshot = service.ModelSnapshot;
                }
            }
            int tablesCount = SnapshotOperation.GetTablesCount(_context);
            AutoMigrateResponse autoMigrateResponse = null;
            if (!canConnect || (canConnect && tablesCount > 0))
            {
                autoMigrateResponse = AutoMigrate(service.Assembly, modelSnapshot?.Model, model);
            }
            else if (_options.ResetDatabaseSchema || (canConnect && tablesCount == 0))
            {
                autoMigrateResponse = AutoMigrateFromReset(service.Assembly, model);
            }
            if (autoMigrateResponse != null && !string.IsNullOrWhiteSpace(autoMigrateResponse.Snapshot))
            {
                SnapshotOperation.WriteSnapshotSource(_context, autoMigrateResponse);
            }
        }
        catch
        {
            throw;
        }
    }

    public async Task RunMigrationsAsync()
    {
        try
        {
            if (!_options.AutomaticMigrationsEnabled)
            {
                return;
            }

            IMigrationsAssembly migrationAssembly = _context.GetService<IMigrationsAssembly>();
            IModel newModel = _context.GetService<IDesignTimeModel>().Model;
            ModelSnapshot modelSnapshot = null;
            bool dbExists = await _context.Database.CanConnectAsync();
            if (!dbExists)
            {
                _options.ResetDatabaseSchema = false;
            }
            if (_options.ResetDatabaseSchema)
            {
                await SnapshotOperation.ExecuteResetDatabaseSchemaAsync(_context, dbExists);
            }
            else
            {
                await SnapshotOperation.EnsureSnapshotTableIsCreatedAsync(_context, dbExists);
                IEnumerable<string> migrations = _context.Database.GetMigrations();
                List<string> list = (await _context.Database.GetAppliedMigrationsAsync()).ToList();
                bool toMigrate = _options.AutomaticMigrationsEnabled || migrations.Intersect(list).Any();
                bool hasPendence = toMigrate && migrations.Except(list).Any();
                string diffMigration = list.Except(migrations).LastOrDefault();
                if (diffMigration != null)
                {
                    if (hasPendence)
                    {
                        throw new InvalidOperationException("Automatic migrations can not run. You  have to restore from a release database.");
                    }
                    string source = (await SnapshotOperation.ReadSnapshotSourceAsync(_context)) ?? throw new InvalidOperationException("Model Snapshot for " + diffMigration + " could not be found in the database");
                    modelSnapshot = CompileSnapshot(migrationAssembly.Assembly, source);
                }
                else if (toMigrate)
                {
                    if (hasPendence)
                    {
                        await _context.Database.MigrateAsync();
                    }
                    modelSnapshot = migrationAssembly.ModelSnapshot;
                }
            }
            int tablesCount = await SnapshotOperation.GetTablesCountAsync(_context);
            AutoMigrateResponse autoMigrateResponse = null;
            if (!dbExists || (dbExists && tablesCount > 0))
            {
                autoMigrateResponse = await AutoMigrateAsync(migrationAssembly.Assembly, modelSnapshot?.Model, newModel);
            }
            else if (_options.ResetDatabaseSchema || (dbExists && tablesCount == 0))
            {
                autoMigrateResponse = await AutoMigrateFromResetAsync(migrationAssembly.Assembly, newModel);
            }
            if (autoMigrateResponse != null && !string.IsNullOrWhiteSpace(autoMigrateResponse.Snapshot))
            {
                await SnapshotOperation.WriteSnapshotSourceAsync(_context, autoMigrateResponse);
            }
        }
        catch
        {
            throw;
        }
    }

    public List<MigrationRaw> ListMigrations()
    {
        return SnapshotOperation.ListMigrations(_context);
    }

    public async Task<List<MigrationRaw>> ListMigrationsAsync()
    {
        return await SnapshotOperation.ListMigrationsAsync(_context);
    }

    public List<MigrationOperation> ListMigrationOperationsAsRawSql()
    {
        if (!_context.Database.CanConnect())
        {
            throw new InvalidOperationException("Can not connect to the database. Please ensure that database is created and accept connections.");
        }
        IServiceProvider serviceProvider = BuildServiceProvider();
        IModel model = _context.GetService<IDesignTimeModel>().Model;
        IModel model2 = ResolveContextModel(serviceProvider);
        if (model2 == null)
        {
            return new List<MigrationOperation>();
        }
        return (from o in serviceProvider.GetRequiredService<MigrationsScaffolderDependencies>().MigrationsModelDiffer.GetDifferences(model2.GetRelationalModel(), model.GetRelationalModel())
                where !(o is UpdateDataOperation)
                select o).ToList();
    }

    public async Task<List<MigrationOperation>> ListMigrationOperationsAsRawSqlAsync()
    {
        if (!(await _context.Database.CanConnectAsync()))
        {
            throw new InvalidOperationException("Can not connect to the database. Please ensure that database is created and accept connections.");
        }
        if (SnapshotOperation.ReadSnapshotSource(_context) == null)
        {
            throw new InvalidOperationException("Model Snapshot could not be found in the database");
        }
        IServiceProvider serviceProvider = BuildServiceProvider();
        IModel model = _context.GetService<IDesignTimeModel>().Model;
        IModel model2 = ResolveContextModel(serviceProvider);
        return (from o in serviceProvider.GetRequiredService<MigrationsScaffolderDependencies>().MigrationsModelDiffer.GetDifferences(model2.GetRelationalModel(), model.GetRelationalModel())
                where !(o is UpdateDataOperation)
                select o).ToList();
    }

    private static T Compile<T>(string source, IEnumerable<Assembly> references)
    {
        CSharpParseOptions options = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest);
        AssemblyIdentityComparer @default = DesktopAssemblyIdentityComparer.Default;
        CSharpCompilationOptions options2 = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, reportSuppressedDiagnostics: false, null, null, null, null, OptimizationLevel.Release, checkOverflow: false, allowUnsafe: false, null, null, default(ImmutableArray<byte>), null, Platform.AnyCpu, ReportDiagnostic.Default, 4, null, concurrentBuild: true, deterministic: false, null, null, null, @default);
        List<PortableExecutableReference> mtReferences = references.Select((Assembly a) => MetadataReference.CreateFromFile(a.Location)).ToList();
        Assembly.GetEntryAssembly()!.GetReferencedAssemblies().ToList().ForEach(delegate (AssemblyName a)
        {
            mtReferences.Add(MetadataReference.CreateFromFile(Assembly.Load(a).Location));
        });
        CSharpCompilation cSharpCompilation = CSharpCompilation.Create("Dynamic", new SyntaxTree[1] { SyntaxFactory.ParseSyntaxTree(source, options) }, mtReferences, options2);
        using MemoryStream memoryStream = new MemoryStream();
        EmitResult emitResult = cSharpCompilation.Emit(memoryStream);
        if (!emitResult.Success)
        {
            throw new MigrateDatabaseException("Compilation failed. Error: " + string.Join(";", emitResult.Diagnostics.Select((Diagnostic t) => t.GetMessage()).ToArray()));
        }
        memoryStream.Seek(0L, SeekOrigin.Begin);
        return (T)Activator.CreateInstance(new AssemblyLoadContext(null, isCollectible: true).LoadFromStream(memoryStream).DefinedTypes.Single((System.Reflection.TypeInfo t) => typeof(T)!.IsAssignableFrom(t)));
    }

    private ModelSnapshot CompileSnapshot(Assembly migrationAssembly, string source)
    {
        HashSet<Assembly> references = new HashSet<Assembly>
        {
            typeof(DbContext)!.Assembly,
            migrationAssembly,
            _context.GetType().Assembly,
            typeof(DbContextAttribute)!.Assembly,
            typeof(ModelSnapshot)!.Assembly,
            typeof(SqlServerValueGenerationStrategy)!.Assembly,
            typeof(AssemblyTargetedPatchBandAttribute)!.Assembly,
            typeof(AbstractionsStrings)!.Assembly,
            typeof(Expression)!.Assembly
        };
        if (_options.UpdateSnapshot.Any())
        {
            foreach (KeyValuePair<string, string> item in _options.UpdateSnapshot)
            {
                source = source.Replace(item.Key, item.Value);
            }
        }
        AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault((Assembly a) => a.GetName().Name!.Equals("netstandard", StringComparison.OrdinalIgnoreCase));
        DependencyContext.Default.RuntimeLibraries.FirstOrDefault((RuntimeLibrary t) => t.Name.Contains("netstandard", StringComparison.OrdinalIgnoreCase));
        return Compile<ModelSnapshot>(source, references);
    }

    private IModel ResolveContextModel(IServiceProvider serviceProvider = null)
    {
        if (serviceProvider == null)
        {
            serviceProvider = BuildServiceProvider();
        }
        MigrationsScaffolderDependencies requiredService = serviceProvider.GetRequiredService<MigrationsScaffolderDependencies>();
        ModelSnapshot modelSnapshot = CompileSnapshot(source: SnapshotOperation.ReadSnapshotSource(_context) ?? throw new InvalidOperationException("Model Snapshot could not be found in the database"), migrationAssembly: _context.GetService<IMigrationsAssembly>().Assembly);
        return requiredService.SnapshotModelProcessor.Process(modelSnapshot.Model);
    }

    private IServiceProvider BuildServiceProvider()
    {
        return new DesignTimeServicesBuilder(_context.GetService<IMigrationsAssembly>().Assembly, Assembly.GetEntryAssembly(), this, (string[])null).Build(_context);
    }

    private AutoMigrateResponse AutoMigrate(Assembly migrationAssembly, IModel oldModel, IModel newModel)
    {
        AutoMigrateResponse autoMigrateResponse = new AutoMigrateResponse();
        MigrationsScaffolderDependencies requiredService = new DesignTimeServicesBuilder(migrationAssembly, Assembly.GetEntryAssembly(), this, (string[])null).Build(_context).GetRequiredService<MigrationsScaffolderDependencies>();
        string text = requiredService.MigrationsIdGenerator.GenerateId("Auto");
        string insertScript = requiredService.HistoryRepository.GetInsertScript(new HistoryRow(text, ((string)newModel.FindAnnotation("ProductVersion")?.Value) ?? "Unknown version"));
        if (oldModel == null)
        {
            _context.Database.EnsureCreated();
            _context.Database.ExecuteSqlRaw(requiredService.HistoryRepository.GetCreateIfNotExistsScript());
            _context.Database.ExecuteSqlRaw(insertScript);
            _context.Database.ExecuteSqlRaw(SnapshotOperation.CREATE_CONTEXT_SNAPSHOT_TABLE ?? "");
        }
        else
        {
            oldModel = requiredService.SnapshotModelProcessor.Process(oldModel);
            List<MigrationOperation> list = (from o in requiredService.MigrationsModelDiffer.GetDifferences(oldModel.GetRelationalModel(), newModel.GetRelationalModel())
                                             where !(o is UpdateDataOperation)
                                             select o).ToList();
            if (!list.Any())
            {
                return null;
            }
            if (!_options.AutomaticMigrationDataLossAllowed && list.Any((MigrationOperation o) => o.IsDestructiveChange))
            {
                throw new InvalidOperationException("Automatic migration was not applied because it could result in data loss.");
            }

            list.Add(new SqlOperation
            {
                Sql = insertScript
            });
            IReadOnlyList<MigrationCommand> migrationCommands = _context.GetService<IMigrationsSqlGenerator>().Generate(list, _context.GetService<IDesignTimeModel>().Model);
            _context.GetService<IMigrationCommandExecutor>().ExecuteNonQuery(migrationCommands, _context.GetService<IRelationalConnection>());
        }
        IMigrationsCodeGenerator service = requiredService.MigrationsCodeGeneratorSelector.Select(null);
        autoMigrateResponse.MigrationId = text;
        autoMigrateResponse.Snapshot = service.GenerateSnapshot("AutomaticMigration", _context.GetType(), "Migration_" + text, newModel);
        return autoMigrateResponse;
    }

    private AutoMigrateResponse AutoMigrateFromReset(Assembly migrationAssembly, IModel newModel)
    {
        AutoMigrateResponse autoMigrateResponse = new AutoMigrateResponse();
        MigrationsScaffolderDependencies requiredService = new DesignTimeServicesBuilder(migrationAssembly, Assembly.GetEntryAssembly(), this, (string[])null).Build(_context).GetRequiredService<MigrationsScaffolderDependencies>();
        string text = requiredService.MigrationsIdGenerator.GenerateId("Auto");
        string insertScript = requiredService.HistoryRepository.GetInsertScript(new HistoryRow(text, ((string)newModel.FindAnnotation("ProductVersion")?.Value) ?? "Unknown version"));
        _context.Database.EnsureCreated();
        _context.Database.ExecuteSqlRaw(requiredService.HistoryRepository.GetCreateIfNotExistsScript());
        List<MigrationOperation> list = (from o in requiredService.MigrationsModelDiffer.GetDifferences(null, newModel.GetRelationalModel())
                                         where o is not UpdateDataOperation
                                         select o).ToList();
        if (!list.Any())
        {
            return null;
        }

        if (!_options.AutomaticMigrationDataLossAllowed && list.Any((MigrationOperation o) => o.IsDestructiveChange))
        {
            throw new InvalidOperationException("Automatic migration was not applied because it could result in data loss.");
        }

        list.Add(new SqlOperation
        {
            Sql = insertScript
        });
        IReadOnlyList<MigrationCommand> migrationCommands = _context.GetService<IMigrationsSqlGenerator>().Generate(list, _context.GetService<IDesignTimeModel>().Model);
        _context.GetService<IMigrationCommandExecutor>().ExecuteNonQuery(migrationCommands, _context.GetService<IRelationalConnection>());
        IMigrationsCodeGenerator val = requiredService.MigrationsCodeGeneratorSelector.Select(null);
        autoMigrateResponse.MigrationId = text;
        autoMigrateResponse.Snapshot = val.GenerateSnapshot("AutomaticMigration", _context.GetType(), "Migration_" + text, newModel);
        return autoMigrateResponse;
    }

    private async Task<AutoMigrateResponse> AutoMigrateAsync(Assembly migrationAssembly, IModel oldModel, IModel newModel)
    {
        AutoMigrateResponse response = new AutoMigrateResponse();
        IServiceProvider provider = new DesignTimeServicesBuilder(migrationAssembly, Assembly.GetEntryAssembly(), (IOperationReporter)(object)this, (string[])null).Build(_context);
        MigrationsScaffolderDependencies dependencies = provider.GetRequiredService<MigrationsScaffolderDependencies>();
        string name = dependencies.MigrationsIdGenerator.GenerateId("Auto");
        string insert = dependencies.HistoryRepository.GetInsertScript(new HistoryRow(name, ((string)newModel.FindAnnotation("ProductVersion")?.Value) ?? "Unknown version"));
        if (oldModel == null)
        {
            await _context.Database.EnsureCreatedAsync();
            await _context.Database.ExecuteSqlRawAsync(dependencies.HistoryRepository.GetCreateIfNotExistsScript());
            await _context.Database.ExecuteSqlRawAsync(insert);
            await _context.Database.ExecuteSqlRawAsync(SnapshotOperation.CREATE_CONTEXT_SNAPSHOT_TABLE ?? "");
        }
        else
        {
            oldModel = dependencies.SnapshotModelProcessor.Process(oldModel);
            List<MigrationOperation> list = (from o in dependencies.MigrationsModelDiffer.GetDifferences(oldModel.GetRelationalModel(), newModel.GetRelationalModel())
                                             where o is not UpdateDataOperation
                                             select o).ToList();
            if (!list.Any())
            {
                return null;
            }

            if (!_options.AutomaticMigrationDataLossAllowed && list.Any((MigrationOperation o) => o.IsDestructiveChange))
            {
                throw new InvalidOperationException("Automatic migration was not applied because it could result in data loss.");
            }

            list.Add(new SqlOperation
            {
                Sql = insert
            });
            IReadOnlyList<MigrationCommand> migrationCommands = _context.GetService<IMigrationsSqlGenerator>().Generate(list, _context.GetService<IDesignTimeModel>().Model);
            await _context.GetService<IMigrationCommandExecutor>().ExecuteNonQueryAsync(migrationCommands, _context.GetService<IRelationalConnection>());
        }

        IMigrationsCodeGenerator val = dependencies.MigrationsCodeGeneratorSelector.Select(null);
        response.MigrationId = name;
        response.Snapshot = val.GenerateSnapshot("AutoMigrations", _context.GetType(), "Migration_" + name, newModel);
        return response;
    }

    private async Task<AutoMigrateResponse> AutoMigrateFromResetAsync(Assembly migrationAssembly, IModel newModel)
    {
        AutoMigrateResponse response = new AutoMigrateResponse();
        IServiceProvider provider = new DesignTimeServicesBuilder(migrationAssembly, Assembly.GetEntryAssembly(), (IOperationReporter)(object)this, (string[])null).Build(_context);
        MigrationsScaffolderDependencies dependencies = provider.GetRequiredService<MigrationsScaffolderDependencies>();
        string name = dependencies.MigrationsIdGenerator.GenerateId("Auto");
        string insert = dependencies.HistoryRepository.GetInsertScript(new HistoryRow(name, ((string)newModel.FindAnnotation("ProductVersion")?.Value) ?? "Unknown version"));
        await _context.Database.EnsureCreatedAsync();
        await _context.Database.ExecuteSqlRawAsync(dependencies.HistoryRepository.GetCreateIfNotExistsScript());
        List<MigrationOperation> list = (from o in dependencies.MigrationsModelDiffer.GetDifferences(null, newModel.GetRelationalModel())
                                         where o is not UpdateDataOperation
                                         select o).ToList();
        if (!list.Any())
        {
            return null;
        }

        list.Add(new SqlOperation
        {
            Sql = insert
        });
        IReadOnlyList<MigrationCommand> migrationCommands = _context.GetService<IMigrationsSqlGenerator>().Generate(list, _context.GetService<IDesignTimeModel>().Model);
        await _context.GetService<IMigrationCommandExecutor>().ExecuteNonQueryAsync(migrationCommands, _context.GetService<IRelationalConnection>());
        IMigrationsCodeGenerator val = dependencies.MigrationsCodeGeneratorSelector.Select(null);
        response.MigrationId = name;
        response.Snapshot = val.GenerateSnapshot("AutoMigrations", _context.GetType(), "Migration_" + name, newModel);
        return response;
    }
}
