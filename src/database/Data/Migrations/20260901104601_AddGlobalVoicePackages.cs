using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlexDirectorConsole.V2.Database.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddGlobalVoicePackages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "VoicePackages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ResourceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    Engine = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    BaseModelVersion = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    GptWeightsPath = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    SoVitsWeightsPath = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    ReferenceAudioFileName = table.Column<string>(type: "TEXT", maxLength: 260, nullable: false),
                    ReferenceAudioContentType = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ReferenceAudioContent = table.Column<byte[]>(type: "BLOB", nullable: false),
                    ReferenceText = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    Language = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Dialect = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    SpeakingStyle = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    DefaultSpeed = table.Column<double>(type: "REAL", nullable: false),
                    License = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    SourceUrl = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsCurrent = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VoicePackages", x => x.Id);
                    table.CheckConstraint("CK_VoicePackages_DefaultSpeed", "DefaultSpeed >= 0.5 AND DefaultSpeed <= 2.0");
                    table.CheckConstraint("CK_VoicePackages_Version", "Version > 0");
                });

            migrationBuilder.CreateIndex(
                name: "IX_VoicePackages_IsCurrent_IsEnabled_Name",
                table: "VoicePackages",
                columns: new[] { "IsCurrent", "IsEnabled", "Name" });

            migrationBuilder.CreateIndex(
                name: "IX_VoicePackages_ResourceId_Version",
                table: "VoicePackages",
                columns: new[] { "ResourceId", "Version" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "VoicePackages");
        }
    }
}
