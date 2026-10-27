using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PhotonBackend.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPlayerStats : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AccountXp",
                table: "Players",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Assists",
                table: "Players",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Deaths",
                table: "Players",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Kills",
                table: "Players",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ReportedAssists",
                table: "MatchPlayers",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ReportedDeaths",
                table: "MatchPlayers",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ReportedKills",
                table: "MatchPlayers",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AccountXp",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "Assists",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "Deaths",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "Kills",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "ReportedAssists",
                table: "MatchPlayers");

            migrationBuilder.DropColumn(
                name: "ReportedDeaths",
                table: "MatchPlayers");

            migrationBuilder.DropColumn(
                name: "ReportedKills",
                table: "MatchPlayers");
        }
    }
}
