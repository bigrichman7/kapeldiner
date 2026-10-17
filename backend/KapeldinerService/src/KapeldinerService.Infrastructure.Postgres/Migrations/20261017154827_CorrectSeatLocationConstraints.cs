using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KapeldinerService.Infrastructure.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class CorrectSeatLocationConstraints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_reservation_seat_location",
                table: "reservation_seats");

            migrationBuilder.DropCheckConstraint(
                name: "ck_event_seat_designation_location",
                table: "event_seat_designation");

            migrationBuilder.AddCheckConstraint(
                name: "ck_reservation_seat_location",
                table: "reservation_seats",
                sql: "row_label IN ('1','2','3','4','5','6','7','8','9','10','11','12','13','14','15','16','17','18','19','20','21','22','23','24','25','26','27','12А') AND seat_number BETWEEN 1 AND 26");

            migrationBuilder.AddCheckConstraint(
                name: "ck_event_seat_designation_location",
                table: "event_seat_designation",
                sql: "row_label IN ('1','2','3','4','5','6','7','8','9','10','11','12','13','14','15','16','17','18','19','20','21','22','23','24','25','26','27','12А') AND seat_number BETWEEN 1 AND 26");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_reservation_seat_location",
                table: "reservation_seats");

            migrationBuilder.DropCheckConstraint(
                name: "ck_event_seat_designation_location",
                table: "event_seat_designation");

            migrationBuilder.AddCheckConstraint(
                name: "ck_reservation_seat_location",
                table: "reservation_seats",
                sql: "(row_label = '12А' AND seat_number BETWEEN 1 AND 24) OR (row_label = '1' AND seat_number BETWEEN 1 AND 24) OR (row_label = '2' AND seat_number BETWEEN 1 AND 24) OR (row_label = '3' AND seat_number BETWEEN 1 AND 24) OR (row_label = '4' AND seat_number BETWEEN 1 AND 24) OR (row_label = '5' AND seat_number BETWEEN 1 AND 24) OR (row_label = '6' AND seat_number BETWEEN 1 AND 24) OR (row_label = '7' AND seat_number BETWEEN 1 AND 24) OR (row_label = '8' AND seat_number BETWEEN 1 AND 24) OR (row_label = '9' AND seat_number BETWEEN 1 AND 24) OR (row_label = '10' AND seat_number BETWEEN 1 AND 24) OR (row_label = '11' AND seat_number BETWEEN 1 AND 24) OR (row_label = '12' AND seat_number BETWEEN 1 AND 24) OR (row_label = '13' AND seat_number BETWEEN 1 AND 24) OR (row_label = '14' AND seat_number BETWEEN 1 AND 24) OR (row_label = '15' AND seat_number BETWEEN 1 AND 24) OR (row_label = '16' AND seat_number BETWEEN 1 AND 24) OR (row_label = '17' AND seat_number BETWEEN 1 AND 24) OR (row_label = '18' AND seat_number BETWEEN 1 AND 24) OR (row_label = '19' AND seat_number BETWEEN 1 AND 24) OR (row_label = '20' AND seat_number BETWEEN 1 AND 24) OR (row_label = '21' AND seat_number BETWEEN 1 AND 24) OR (row_label = '22' AND seat_number BETWEEN 1 AND 24) OR (row_label = '23' AND seat_number BETWEEN 1 AND 24) OR (row_label = '24' AND seat_number BETWEEN 1 AND 24) OR (row_label = '25' AND seat_number BETWEEN 1 AND 24) OR (row_label = '26' AND seat_number BETWEEN 1 AND 26) OR (row_label = '27' AND seat_number BETWEEN 1 AND 26)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_event_seat_designation_location",
                table: "event_seat_designation",
                sql: "(row_label IN ('1','2','3','4','5','6','7','8','9','10','11','12','13','14','15','16','17','18','19','20','21','22','23','24','25') AND seat_number BETWEEN 1 AND 24) OR (row_label IN ('26','27') AND seat_number BETWEEN 1 AND 26) OR (row_label = '12А' AND seat_number BETWEEN 1 AND 24)");
        }
    }
}
