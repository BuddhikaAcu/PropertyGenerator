using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

[Flags]
public enum GeneratorBehavior
{
    Default = 0x0,
    View = 0x1,
    DapperContrib = 0x2,
    Comment = 0x4
}

public static partial class PocoClassGenerator
{
    #region Property
    private static readonly Dictionary<Type, string> TypeAliases = new Dictionary<Type, string> {
               { typeof(int), "int" },
               { typeof(short), "short" },
               { typeof(byte), "byte" },
               { typeof(byte[]), "byte[]" },
               { typeof(long), "long" },
               { typeof(double), "double" },
               { typeof(decimal), "decimal" },
               { typeof(float), "float" },
               { typeof(bool), "bool" },
               { typeof(string), "string" },
               { typeof(Guid), "Guid" },
               { typeof(DateTime), "DateTime" }
       };

    private static readonly Dictionary<Type, string> BqlTypeAliases = new Dictionary<Type, string> {
               { typeof(int), "BqlInt" },
               { typeof(short), "BqlShort" },
               { typeof(byte), "BqlByte" },
               { typeof(byte[]), "BqlByteArray" },
               { typeof(long), "BqlLong" },
               { typeof(double), "BqlDouble" },
               { typeof(decimal), "BqlDecimal" },
               { typeof(float), "BqlFloat" },
               { typeof(bool), "BqlBool" },
               { typeof(string), "BqlString" },
               { typeof(DateTime), "BqlDateTime" },
               { typeof(Guid), "BqlGuid" }
       };

    private static readonly Dictionary<Type, string> DBTypeAliases = new Dictionary<Type, string> {
               { typeof(int), "PXDBInt" },
               { typeof(short), "PXDBShort" },
               { typeof(byte), "PXDBByte" },
               { typeof(byte[]), "PXDBByte" },
               { typeof(long), "PXDBLong" },
               { typeof(double), "PXDBDouble" },
               { typeof(decimal), "PXDBDecimal" },
               { typeof(float), "PXDBFloat" },
               { typeof(bool), "PXDBBool" },
               { typeof(string), "PXDBString" },
               { typeof(DateTime), "PXDBDateAndTime" },
               { typeof(Guid), "PXDBGuid" }
       };

    private static readonly Dictionary<string, string> QuerySqls = new Dictionary<string, string> {
               {"sqlconnection", "select  *  from [{0}] where 1=2" },
               {"sqlceserver", "select  *  from [{0}] where 1=2" },
               {"sqliteconnection", "select  *  from [{0}] where 1=2" },
               {"oracleconnection", "select  *  from \"{0}\" where 1=2" },
               {"mysqlconnection", "select  *  from `{0}` where 1=2" },
               {"npgsqlconnection", "select  *  from \"{0}\" where 1=2" }
       };

    private static readonly Dictionary<string, string> TableSchemaSqls = new Dictionary<string, string> {
               {"sqlconnection", "select TABLE_NAME from INFORMATION_SCHEMA.TABLES where TABLE_TYPE = 'BASE TABLE'" },
               {"sqlceserver", "select TABLE_NAME from INFORMATION_SCHEMA.TABLES  where TABLE_TYPE = 'BASE TABLE'" },
               {"sqliteconnection", "SELECT name FROM sqlite_master where type = 'table'" },
               {"oracleconnection", "select TABLE_NAME from USER_TABLES where table_name not in (select View_name from user_views)" },
               {"mysqlconnection", "select TABLE_NAME from  information_schema.tables where table_type = 'BASE TABLE'" },
               {"npgsqlconnection", "select table_name from information_schema.tables where table_type = 'BASE TABLE'" }
       };


    private static readonly HashSet<Type> NullableTypes = new HashSet<Type> {
               typeof(int),
               typeof(short),
               typeof(long),
               typeof(double),
               typeof(decimal),
               typeof(float),
               typeof(bool),
               typeof(DateTime)
       };
    #endregion

    public static string GenerateAllTables(this System.Data.Common.DbConnection connection, GeneratorBehavior generatorBehavior = GeneratorBehavior.Default)
    {
        if (connection.State != ConnectionState.Open) connection.Open();

        var conneciontName = connection.GetType().Name.ToLower();
        var tables = new List<string>();
        var sql = generatorBehavior.HasFlag(GeneratorBehavior.View) ? TableSchemaSqls[conneciontName].Split("where")[0] : TableSchemaSqls[conneciontName];
        using (var command = connection.CreateCommand(sql))
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
                tables.Add(reader.GetString(0));
        }

        var sb = new StringBuilder();
        sb.AppendLine("namespace Models { ");
        tables.ForEach(table => sb.Append(connection.GenerateClass(
               string.Format(QuerySqls[conneciontName], table), table, generatorBehavior: generatorBehavior
        )));
        sb.AppendLine("}");
        return sb.ToString();
    }

    public static string GenerateClass(this IDbConnection connection, string sql, GeneratorBehavior generatorBehavior)
         => connection.GenerateClass(sql, null, generatorBehavior);

    public static string GenerateClass(this IDbConnection connection, string sql, string className = null, GeneratorBehavior generatorBehavior = GeneratorBehavior.Default)
    {
        if (connection.State != ConnectionState.Open) connection.Open();

        var builder = new StringBuilder();

        //Get Table Name
        //Fix : [When View using CommandBehavior.KeyInfo will get duplicate columns �P Issue #8 �P shps951023/PocoClassGenerator](https://github.com/shps951023/PocoClassGenerator/issues/8 )
        var isFromMutiTables = false;
        using (var command = connection.CreateCommand(sql))
        using (var reader = command.ExecuteReader(CommandBehavior.KeyInfo | CommandBehavior.SingleRow))
        {
            var tables = reader.GetSchemaTable().Select().Select(s => s["BaseTableName"] as string).Distinct();
            var tableName = string.IsNullOrWhiteSpace(className) ? tables.First() ?? "Info" : className;

            isFromMutiTables = tables.Count() > 1;

            builder.AppendFormat("	public class {0}{1}", tableName.Replace(" ", ""), Environment.NewLine);
            builder.AppendLine("	{");
        }

        //Get Columns 
        var behavior = isFromMutiTables ? (CommandBehavior.SchemaOnly | CommandBehavior.SingleRow) : (CommandBehavior.KeyInfo | CommandBehavior.SingleRow);

        using (var command = connection.CreateCommand(sql))
        using (var reader = command.ExecuteReader(behavior))
        {
            do
            {
                var schema = reader.GetSchemaTable();
                foreach (DataRow row in schema.Rows)
                {
                    AppenProperty(row, builder);
                }

                builder.AppendLine("	}");
                builder.AppendLine();
            } while (reader.NextResult());

            return builder.ToString();
        }
    }

    private static string GetBQLClassName(string propertyName)
    {
        return propertyName.Substring(0, 1).ToLower() + propertyName.Substring(1, propertyName.Length - 1);
    }

    private static string GetLabelName(string propertyName)
    {
        Regex r = new Regex(@"(?<=[A-Z])(?=[A-Z][a-z])|(?<=[^A-Z])(?=[A-Z])|(?<=[A-Za-z])(?=[^A-Za-z])");
        return r.Replace(propertyName, " ");
    }

    private static StringBuilder AppenProperty(DataRow row, StringBuilder builder)
    {
        var type = (Type)row["DataType"];
        var name = TypeAliases.ContainsKey(type) ? TypeAliases[type] : type.FullName;
        var bqlName = BqlTypeAliases.ContainsKey(type) ? BqlTypeAliases[type] : "Bql" + type.FullName;
        var pxdbType = DBTypeAliases.ContainsKey(type) ? DBTypeAliases[type] : "PXDB" + type.FullName;
        var length = name == "string" ? row.ItemArray[2].ToString() : string.Empty;
        var isNullable = NullableTypes.Contains(type);
        var collumnName = (string)row["ColumnName"];
        var isKey = false;
        isKey = (bool)row["IsKey"];

        switch (collumnName)
        {
            case "CreatedByID":
                builder.AppendLine(@"#region CreatedByID 
                            public abstract class createdByID : PX.Data.BQL.BqlGuid.Field<createdByID> { }
                            [PXDBCreatedByID()]
                            public virtual Guid? CreatedByID { get; set; }
                            #endregion");
                return builder;
            case "CreatedByScreenID":
                builder.AppendLine(@"#region CreatedByScreenID 
                            public abstract class createdByScreenID : PX.Data.BQL.BqlString.Field<createdByScreenID> { }
                            [PXDBCreatedByScreenID()]
                            public virtual string CreatedByScreenID { get; set; }
                            #endregion");
                return builder;
            case "CreatedDateTime":
                builder.AppendLine(@"#region CreatedDateTime 
                            public abstract class createdDateTime : PX.Data.BQL.BqlDateTime.Field<createdDateTime> { }
                            [PXDBCreatedDateTime()]
                            public virtual DateTime? CreatedDateTime { get; set; }
                            #endregion");
                return builder;
            case "LastModifiedByID":
                builder.AppendLine(@"#region LastModifiedByID 
                            public abstract class lastModifiedByID : PX.Data.BQL.BqlGuid.Field<lastModifiedByID> { }
                            [PXDBLastModifiedByID()]
                            public virtual Guid? LastModifiedByID { get; set; }
                            #endregion");
                return builder;
            case "LastModifiedByScreenID":
                builder.AppendLine(@"#region LastModifiedByScreenID 
                            public abstract class lastModifiedByScreenID : PX.Data.BQL.BqlString.Field<lastModifiedByScreenID> { }
                            [PXDBLastModifiedByScreenID()]
                            public virtual string LastModifiedByScreenID { get; set; }
                            #endregion");
                return builder;
            case "LastModifiedDateTime":
                builder.AppendLine(@"
                            #region LastModifiedDateTime 
                            public abstract class lastModifiedDateTime : PX.Data.BQL.BqlDateTime.Field<lastModifiedDateTime> { }
                            [PXDBLastModifiedDateTime()]
                            public virtual DateTime? LastModifiedDateTime { get; set; }
                            #endregion");
                return builder;
            case "tstamp":
                builder.AppendLine(@"
                            #region tstamp 
                            public abstract class Tstamp : PX.Data.BQL.BqlByteArray.Field<Tstamp> { }
                            [PXDBTimestamp()]
                            public virtual Byte[] tstamp { get; set; }
                            #endregion");
                return builder;
            default:
                builder.AppendLine(string.Format("		#region {0}", collumnName));
                builder.AppendLine(string.Format("		public abstract class {0} : {1}.Field<{0}> {{ }}", GetBQLClassName(collumnName), bqlName));
                builder.AppendLine(string.Format("		[{0}({1}{2} {3})]", pxdbType, length, isKey && !string.IsNullOrEmpty(length) ? " , " : string.Empty, isKey ? "IsKey = true" : string.Empty));
                builder.AppendLine(string.Format("		[PXUIField(DisplayName = \"{0}\")]", GetLabelName(collumnName)));
                builder.AppendLine(string.Format("		public virtual {0}{1} {2} {{ get; set; }}", name, isNullable ? "?" : string.Empty, collumnName));
                builder.AppendLine("		#endregion");
                return builder;
        }

    }

    #region Private
    private static string[] Split(this string text, string splitText) => text.Split(new[] { splitText }, StringSplitOptions.None);
    private static IDbCommand CreateCommand(this IDbConnection connection, string sql)
    {
        var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        return cmd;
    }
    #endregion
}