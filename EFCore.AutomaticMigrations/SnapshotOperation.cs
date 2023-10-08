using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace EFCore.AutomaticMigrations;

internal class SnapshotOperation
{
    public static readonly string CREATE_CONTEXT_SNAPSHOT_TABLE = "IF NOT EXISTS (SELECT 1 FROM sysobjects WHERE name='__ContextSnapshot' and xtype='U')\r\n                              CREATE TABLE __ContextSnapshot (Snapshot varbinary(max) null, MigrationId nvarchar(150), CreatedDate datetime DEFAULT GETDATE())";

    public static readonly string ALTER_CONTEXT_SNAPSHOT_TABLE = "IF NOT EXISTS ( SELECT *  FROM   sys.columns  WHERE  object_id = OBJECT_ID(N'[dbo].[__ContextSnapshot]') AND name = 'MigrationId')\r\n\t             ALTER TABLE __ContextSnapshot ADD MigrationId nvarchar(150)\r\n              IF NOT EXISTS ( SELECT *  FROM   sys.columns  WHERE  object_id = OBJECT_ID(N'[dbo].[__ContextSnapshot]') AND name = 'CreatedDate')\r\n\t             ALTER TABLE __ContextSnapshot ADD CreatedDate datetime DEFAULT GETDATE();\r\n             ";

    public static readonly string UPDATE_CONTEXT_SNAPSHOT_TABLE = "UPDATE __ContextSnapshot SET CreatedDate = (GETDATE() - 1)  where CreatedDate IS NULL;";

    public static readonly string INSERT_IN_CONTEXT_SNAPSHOT_TABLE = "INSERT INTO [dbo].[__ContextSnapshot] ( [Snapshot], [MigrationId] ) VALUES ({0},{1})";

    public static readonly string SELECT_LAST_SNAPSHOT = "select top 1 Snapshot from __ContextSnapshot order by CreatedDate DESC";

    public static readonly string SELECT_ALL_SNAPSHOTS = "select ISNULL(MigrationId, '') AS MigrationId, CreatedDate from __ContextSnapshot\r\n                                                                where MigrationId IS NOT NULL OR CreatedDate IS NOT NULL\r\n                                                                order by CreatedDate DESC";

    public static readonly string DROP_ALL_TABLES = "while(exists(select 1 from INFORMATION_SCHEMA.TABLE_CONSTRAINTS where CONSTRAINT_TYPE='FOREIGN KEY'))\r\n                                                begin\r\n                                                    declare @sql nvarchar(2000)\r\n                                                        SELECT TOP 1 @sql=('ALTER TABLE ' + TABLE_SCHEMA + '.[' + TABLE_NAME\r\n                                                        + '] DROP CONSTRAINT [' + CONSTRAINT_NAME + ']')\r\n                                                        FROM information_schema.table_constraints\r\n                                                        WHERE CONSTRAINT_TYPE = 'FOREIGN KEY'\r\n                                                        exec (@sql)\r\n                                                    end\r\n\r\n                                                    while(exists(select 1 from INFORMATION_SCHEMA.TABLES \r\n                                                                    where TABLE_NAME != '__EFMigrationsHistory' \r\n                                                                    AND TABLE_TYPE = 'BASE TABLE'))\r\n                                                    begin\r\n                                                        --declare @sql nvarchar(2000)\r\n                                                        SELECT TOP 1 @sql=('DROP TABLE ' + TABLE_SCHEMA + '.[' + TABLE_NAME\r\n                                                        + ']')\r\n                                                        FROM INFORMATION_SCHEMA.TABLES\r\n                                                        WHERE TABLE_NAME != '__EFMigrationsHistory' AND TABLE_TYPE = 'BASE TABLE'\r\n                                                    exec (@sql)\r\n                                                end\r\n\t                                            declare @sqlEF nvarchar(2000)\r\n\t                                            set @sqlEF = 'IF EXISTS(SELECT * FROM INFORMATION_SCHEMA.TABLES where TABLE_NAME =''__EFMigrationsHistory'' AND TABLE_TYPE = ''BASE TABLE'')  \r\n\t                                               DROP TABLE [dbo].[__EFMigrationsHistory]'\r\n                                            exec (@sqlEF)\r\n                                            exec ('CREATE TABLE __ContextSnapshot (Snapshot varbinary(max) null, MigrationId nvarchar(150), CreatedDate datetime DEFAULT GETDATE())')";

    public static readonly string SELECT_COUNT_TABLES = "SELECT COUNT(*) FROM sys.tables WHERE NAME NOT IN ('__EFMigrationsHistory','__ContextSnapshot')";

    public static void WriteSnapshotSource(DbContext context, AutoMigrateResponse response)
    {
        using MemoryStream memoryStream = new MemoryStream();
        using (GZipStream gZipStream = new GZipStream(memoryStream, CompressionLevel.Fastest, leaveOpen: true))
        {
            gZipStream.Write(Encoding.UTF8.GetBytes(response.Snapshot));
        }
        memoryStream.Seek(0L, SeekOrigin.Begin);
        context.Database.ExecuteSqlRaw(INSERT_IN_CONTEXT_SNAPSHOT_TABLE, memoryStream.ToArray(), response.MigrationId);
    }

    public static async Task WriteSnapshotSourceAsync(DbContext context, AutoMigrateResponse response)
    {
        using MemoryStream dbStream = new MemoryStream();
        using (GZipStream stream = new GZipStream(dbStream, CompressionLevel.Fastest, leaveOpen: true))
        {
            await stream.WriteAsync(Encoding.UTF8.GetBytes(response.Snapshot));
        }
        dbStream.Seek(0L, SeekOrigin.Begin);
        await context.Database.ExecuteSqlRawAsync(INSERT_IN_CONTEXT_SNAPSHOT_TABLE, dbStream.ToArray(), response.MigrationId);
    }

    public static string ReadSnapshotSource(DbContext context)
    {
        using DbCommand dbCommand = CreateCommand(context);
        dbCommand.CommandText = SELECT_LAST_SNAPSHOT;
        context.Database.OpenConnection();
        try
        {
            using DbDataReader dbDataReader = dbCommand.ExecuteReader();
            if (!dbDataReader.Read())
            {
                return null;
            }
            using GZipStream stream = new GZipStream(dbDataReader.GetStream(0), CompressionMode.Decompress);
            return new StreamReader(stream).ReadToEnd();
        }
        catch
        {
            throw;
        }
        finally
        {
            context.Database.CloseConnection();
        }
    }

    public static async Task<string> ReadSnapshotSourceAsync(DbContext context)
    {
        using DbCommand cmd = CreateCommand(context);
        cmd.CommandText = SELECT_LAST_SNAPSHOT;
        await context.Database.OpenConnectionAsync();
        try
        {
            string result;
            using DbDataReader reader = cmd.ExecuteReader();
            if (!(await reader.ReadAsync()))
            {
                result = null;
            }
            else
            {
                using GZipStream stream = new GZipStream(reader.GetStream(0), CompressionMode.Decompress);
                result = await new StreamReader(stream).ReadToEndAsync();
            }
            return result;
        }
        finally
        {
            await context.Database.CloseConnectionAsync();
        }
    }

    public static List<MigrationRaw> ListMigrations(DbContext context)
    {
        return RawSqlQuery(context, SELECT_ALL_SNAPSHOTS, (DbDataReader x) => new MigrationRaw
        {
            MigrationId = (string)x[0],
            CreatedDate = (DateTime?)x[1]
        });
    }

    public static async Task<List<MigrationRaw>> ListMigrationsAsync(DbContext context)
    {
        return await RawSqlQueryAsync(context, SELECT_ALL_SNAPSHOTS, (DbDataReader x) => new MigrationRaw
        {
            MigrationId = (string)x[0],
            CreatedDate = (DateTime?)x[1]
        });
    }

    public static void ExecuteResetDatabaseSchema(DbContext context, bool? canConnect)
    {
        if (!canConnect.HasValue)
        {
            canConnect = context.Database.CanConnect();
        }
        if (canConnect.Value)
        {
            context.Database.ExecuteSqlRaw(DROP_ALL_TABLES);
        }
    }

    public static async Task ExecuteResetDatabaseSchemaAsync(DbContext context, bool? canConnect)
    {
        if (!canConnect.HasValue)
        {
            canConnect = await context.Database.CanConnectAsync();
        }
        if (canConnect.Value)
        {
            await context.Database.ExecuteSqlRawAsync(DROP_ALL_TABLES);
        }
    }

    public static void EnsureSnapshotTableIsCreated(DbContext context, bool? canConnect)
    {
        if (!canConnect.HasValue)
        {
            canConnect = context.Database.CanConnect();
        }
        if (canConnect.Value)
        {
            context.Database.ExecuteSqlRaw(CREATE_CONTEXT_SNAPSHOT_TABLE);
            context.Database.ExecuteSqlRaw(ALTER_CONTEXT_SNAPSHOT_TABLE);
            context.Database.ExecuteSqlRaw(UPDATE_CONTEXT_SNAPSHOT_TABLE);
        }
    }

    public static async Task EnsureSnapshotTableIsCreatedAsync(DbContext context, bool? canConnect)
    {
        if (!canConnect.HasValue)
        {
            canConnect = await context.Database.CanConnectAsync();
        }
        if (canConnect.Value)
        {
            await context.Database.ExecuteSqlRawAsync(CREATE_CONTEXT_SNAPSHOT_TABLE ?? "");
            await context.Database.ExecuteSqlRawAsync(ALTER_CONTEXT_SNAPSHOT_TABLE ?? "");
            await context.Database.ExecuteSqlRawAsync(UPDATE_CONTEXT_SNAPSHOT_TABLE ?? "");
        }
    }

    public static int GetTablesCount(DbContext context)
    {
        if (!context.Database.CanConnect())
        {
            return 0;
        }
        using DbCommand dbCommand = CreateCommand(context);
        dbCommand.CommandText = SELECT_COUNT_TABLES;
        context.Database.OpenConnection();
        try
        {
            using DbDataReader dbDataReader = dbCommand.ExecuteReader();
            if (!dbDataReader.Read())
            {
                return 0;
            }
            return (int)dbDataReader[0];
        }
        catch (Exception ex)
        {
            throw ex;
        }
        finally
        {
            context.Database.CloseConnection();
        }
    }

    public static async Task<int> GetTablesCountAsync(DbContext context)
    {
        if (!await context.Database.CanConnectAsync())
        {
            return await Task.FromResult(0);
        }
        using DbCommand cmd = CreateCommand(context);
        cmd.CommandText = SELECT_COUNT_TABLES;
        await context.Database.OpenConnectionAsync();
        try
        {
            using DbDataReader reader = await cmd.ExecuteReaderAsync();
            return (await reader.ReadAsync()) ? ((int)reader[0]) : 0;
        }
        finally
        {
            await context.Database.CloseConnectionAsync();
        }
    }

    private static DbCommand CreateCommand(DbContext context)
    {
        DbCommand dbCommand = context.Database.GetDbConnection().CreateCommand();
        dbCommand.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        return dbCommand;
    }

    private static List<T> RawSqlQuery<T>(DbContext context, string query, Func<DbDataReader, T> map)
    {
        try
        {
            using DbCommand dbCommand = CreateCommand(context);
            dbCommand.CommandText = query;
            dbCommand.CommandType = CommandType.Text;
            context.Database.OpenConnection();
            using DbDataReader dbDataReader = dbCommand.ExecuteReader();
            List<T> list = new List<T>();
            while (dbDataReader.Read())
            {
                list.Add(map(dbDataReader));
            }
            return list;
        }
        catch (Exception)
        {
            throw;
        }
        finally
        {
            context.Database.CloseConnection();
        }
    }

    private static async Task<List<T>> RawSqlQueryAsync<T>(DbContext context, string query, Func<DbDataReader, T> map)
    {
        try
        {
            List<T> result2;
            try
            {
                using DbCommand command = CreateCommand(context);
                command.CommandText = query;
                command.CommandType = CommandType.Text;
                await context.Database.OpenConnectionAsync();
                using DbDataReader result = await command.ExecuteReaderAsync();
                List<T> entities = new List<T>();
                while (await result.ReadAsync())
                {
                    entities.Add(map(result));
                }
                result2 = entities;
            }
            catch (Exception)
            {
                throw;
            }
            return result2;
        }
        finally
        {
            await context.Database.CloseConnectionAsync();
        }
    }
}
