using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuotationApp.API.Migrations
{
    /// <inheritdoc />
    public partial class FixPoVerificationStatusConstraint : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Drop existing CHECK constraint if it exists
            migrationBuilder.Sql(@"
                DECLARE @constraintName NVARCHAR(128);
                SELECT @constraintName = name
                FROM sys.check_constraints
                WHERE parent_object_id = OBJECT_ID(N'dbo.purchase_orders')
                  AND name = 'CK_po_verification_status';

                IF @constraintName IS NOT NULL
                BEGIN
                    DECLARE @sql NVARCHAR(MAX) = N'ALTER TABLE dbo.purchase_orders DROP CONSTRAINT ' + QUOTENAME(@constraintName);
                    EXEC sp_executesql @sql;
                END
            ");

            // Add CHECK constraint with all allowed status values
            migrationBuilder.Sql(@"
                ALTER TABLE dbo.purchase_orders
                ADD CONSTRAINT CK_po_verification_status
                CHECK (verification_status IN ('Draft', 'pending', 'PendingReview', 'Approved', 'ApprovedWithMismatch', 'Rejected', 'Cancelled', 'Closed', 'Completed', 'verified'));
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Drop the CHECK constraint
            migrationBuilder.Sql(@"
                DECLARE @constraintName NVARCHAR(128);
                SELECT @constraintName = name
                FROM sys.check_constraints
                WHERE parent_object_id = OBJECT_ID(N'dbo.purchase_orders')
                  AND name = 'CK_po_verification_status';

                IF @constraintName IS NOT NULL
                BEGIN
                    DECLARE @sql NVARCHAR(MAX) = N'ALTER TABLE dbo.purchase_orders DROP CONSTRAINT ' + QUOTENAME(@constraintName);
                    EXEC sp_executesql @sql;
                END
            ");
        }
    }
}