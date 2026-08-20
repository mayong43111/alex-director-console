using AlexDirectorConsole.V2.Database.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlexDirectorConsole.V2.Database.Data.Migrations;

[DbContext(typeof(V2DbContext))]
[Migration("20260820053000_RemoveProtagonistSpecies")]
public sealed class RemoveProtagonistSpecies : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            UPDATE Assets
            SET DocumentJson = json_remove(DocumentJson, '$.protagonistSpecies'),
                SchemaVersion = 2,
                SizeBytes = length(CAST(json_remove(DocumentJson, '$.protagonistSpecies') AS BLOB))
            WHERE Type = 'creative-settings'
              AND DocumentJson IS NOT NULL
              AND json_valid(DocumentJson)
              AND json_type(DocumentJson, '$.protagonistSpecies') IS NOT NULL;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            UPDATE Assets
            SET DocumentJson = json_set(DocumentJson, '$.protagonistSpecies', '拟人动物'),
                SchemaVersion = 1,
                SizeBytes = length(CAST(json_set(DocumentJson, '$.protagonistSpecies', '拟人动物') AS BLOB))
            WHERE Type = 'creative-settings'
              AND DocumentJson IS NOT NULL
              AND json_valid(DocumentJson);
            """);
    }
}