using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ImperialColors.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddProdutoFreteImagem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "altura_cm",
                table: "produtos",
                type: "numeric(10,2)",
                precision: 10,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "comprimento_cm",
                table: "produtos",
                type: "numeric(10,2)",
                precision: 10,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "imagem_produto_path",
                table: "produtos",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "imagem_removida",
                table: "produtos",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "largura_cm",
                table: "produtos",
                type: "numeric(10,2)",
                precision: 10,
                scale: 2,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_produtos_dimensoes_positivas",
                table: "produtos",
                sql: "(altura_cm IS NULL OR altura_cm > 0) AND (largura_cm IS NULL OR largura_cm > 0) AND (comprimento_cm IS NULL OR comprimento_cm > 0)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_produtos_imagem_removida",
                table: "produtos",
                sql: "NOT imagem_removida OR imagem_produto_path IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_produtos_dimensoes_positivas",
                table: "produtos");

            migrationBuilder.DropCheckConstraint(
                name: "CK_produtos_imagem_removida",
                table: "produtos");

            migrationBuilder.DropColumn(
                name: "altura_cm",
                table: "produtos");

            migrationBuilder.DropColumn(
                name: "comprimento_cm",
                table: "produtos");

            migrationBuilder.DropColumn(
                name: "imagem_produto_path",
                table: "produtos");

            migrationBuilder.DropColumn(
                name: "imagem_removida",
                table: "produtos");

            migrationBuilder.DropColumn(
                name: "largura_cm",
                table: "produtos");
        }
    }
}
