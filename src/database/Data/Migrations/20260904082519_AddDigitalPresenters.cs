using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlexDirectorConsole.V2.Database.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDigitalPresenters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DigitalPresenters",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    IdentityImageAssetId = table.Column<Guid>(type: "TEXT", nullable: false),
                    BackgroundImageAssetId = table.Column<Guid>(type: "TEXT", nullable: true),
                    OutfitImageAssetId = table.Column<Guid>(type: "TEXT", nullable: true),
                    VoiceAssetId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DigitalPresenters", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DigitalPresenters_Assets_BackgroundImageAssetId",
                        column: x => x.BackgroundImageAssetId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DigitalPresenters_Assets_IdentityImageAssetId",
                        column: x => x.IdentityImageAssetId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DigitalPresenters_Assets_OutfitImageAssetId",
                        column: x => x.OutfitImageAssetId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DigitalPresenters_Assets_VoiceAssetId",
                        column: x => x.VoiceAssetId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DigitalPresenters_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DigitalPresenterEpisodes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PresenterId = table.Column<Guid>(type: "TEXT", nullable: false),
                    EpisodeNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Dialogue = table.Column<string>(type: "TEXT", maxLength: 12000, nullable: false),
                    BackgroundImageAssetId = table.Column<Guid>(type: "TEXT", nullable: true),
                    OutfitImageAssetId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DigitalPresenterEpisodes", x => x.Id);
                    table.CheckConstraint("CK_DigitalPresenterEpisodes_EpisodeNumber", "EpisodeNumber > 0");
                    table.ForeignKey(
                        name: "FK_DigitalPresenterEpisodes_Assets_BackgroundImageAssetId",
                        column: x => x.BackgroundImageAssetId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DigitalPresenterEpisodes_Assets_OutfitImageAssetId",
                        column: x => x.OutfitImageAssetId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DigitalPresenterEpisodes_DigitalPresenters_PresenterId",
                        column: x => x.PresenterId,
                        principalTable: "DigitalPresenters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DigitalPresenterEpisodes_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DigitalPresenterShots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    EpisodeId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    Dialogue = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    EffectiveCharacterCount = table.Column<int>(type: "INTEGER", nullable: false),
                    DurationSeconds = table.Column<int>(type: "INTEGER", nullable: false),
                    FirstFrameAssetId = table.Column<Guid>(type: "TEXT", nullable: true),
                    VideoAssetId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    Error = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DigitalPresenterShots", x => x.Id);
                    table.CheckConstraint("CK_DigitalPresenterShots_DurationSeconds", "DurationSeconds BETWEEN 4 AND 15");
                    table.CheckConstraint("CK_DigitalPresenterShots_EffectiveCharacterCount", "EffectiveCharacterCount > 0");
                    table.CheckConstraint("CK_DigitalPresenterShots_SortOrder", "SortOrder > 0");
                    table.ForeignKey(
                        name: "FK_DigitalPresenterShots_Assets_FirstFrameAssetId",
                        column: x => x.FirstFrameAssetId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DigitalPresenterShots_Assets_VideoAssetId",
                        column: x => x.VideoAssetId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DigitalPresenterShots_DigitalPresenterEpisodes_EpisodeId",
                        column: x => x.EpisodeId,
                        principalTable: "DigitalPresenterEpisodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DigitalPresenterShots_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DigitalPresenterEpisodes_BackgroundImageAssetId",
                table: "DigitalPresenterEpisodes",
                column: "BackgroundImageAssetId");

            migrationBuilder.CreateIndex(
                name: "IX_DigitalPresenterEpisodes_OutfitImageAssetId",
                table: "DigitalPresenterEpisodes",
                column: "OutfitImageAssetId");

            migrationBuilder.CreateIndex(
                name: "IX_DigitalPresenterEpisodes_PresenterId_EpisodeNumber",
                table: "DigitalPresenterEpisodes",
                columns: new[] { "PresenterId", "EpisodeNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DigitalPresenterEpisodes_ProjectId",
                table: "DigitalPresenterEpisodes",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_DigitalPresenters_BackgroundImageAssetId",
                table: "DigitalPresenters",
                column: "BackgroundImageAssetId");

            migrationBuilder.CreateIndex(
                name: "IX_DigitalPresenters_IdentityImageAssetId",
                table: "DigitalPresenters",
                column: "IdentityImageAssetId");

            migrationBuilder.CreateIndex(
                name: "IX_DigitalPresenters_OutfitImageAssetId",
                table: "DigitalPresenters",
                column: "OutfitImageAssetId");

            migrationBuilder.CreateIndex(
                name: "IX_DigitalPresenters_ProjectId_Name",
                table: "DigitalPresenters",
                columns: new[] { "ProjectId", "Name" });

            migrationBuilder.CreateIndex(
                name: "IX_DigitalPresenters_VoiceAssetId",
                table: "DigitalPresenters",
                column: "VoiceAssetId");

            migrationBuilder.CreateIndex(
                name: "IX_DigitalPresenterShots_EpisodeId_SortOrder",
                table: "DigitalPresenterShots",
                columns: new[] { "EpisodeId", "SortOrder" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DigitalPresenterShots_FirstFrameAssetId",
                table: "DigitalPresenterShots",
                column: "FirstFrameAssetId");

            migrationBuilder.CreateIndex(
                name: "IX_DigitalPresenterShots_ProjectId",
                table: "DigitalPresenterShots",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_DigitalPresenterShots_VideoAssetId",
                table: "DigitalPresenterShots",
                column: "VideoAssetId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DigitalPresenterShots");

            migrationBuilder.DropTable(
                name: "DigitalPresenterEpisodes");

            migrationBuilder.DropTable(
                name: "DigitalPresenters");
        }
    }
}
