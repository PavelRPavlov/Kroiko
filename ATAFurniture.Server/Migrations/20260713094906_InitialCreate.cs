using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ATAFurniture.Server.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AadId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreditsCount = table.Column<int>(type: "int", nullable: false),
                    CreditResets = table.Column<int>(type: "int", nullable: false),
                    LastSelectedCompany_Name = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LastSelectedCompany_Translation = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LastSelectedCompany_Email = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Email = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    MobileNumber = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CompanyName = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Users");
        }
    }
}
