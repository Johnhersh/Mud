using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mud.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class NonNullableCurrentWorldId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Update existing NULL values to "overworld"
            migrationBuilder.Sql(
                "UPDATE \"Characters\" SET \"CurrentWorldId\" = 'overworld' WHERE \"CurrentWorldId\" IS NULL");

            migrationBuilder.AlterColumn<string>(
                name: "CurrentWorldId",
                table: "Characters",
                type: "text",
                nullable: false,
                defaultValue: "overworld",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "CurrentWorldId",
                table: "Characters",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text");
        }
    }
}
