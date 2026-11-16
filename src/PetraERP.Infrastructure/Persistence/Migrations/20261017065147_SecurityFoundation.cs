using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PetraERP.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class SecurityFoundation : Migration
{
    private static readonly string[] CompanyIdIdColumns = ["CompanyId", "Id"];
    private static readonly string[] UserIdCompanyIdColumns = ["UserId", "CompanyId"];
    private static readonly string[] EventTypeOccurredAtUtcColumns = ["EventType", "OccurredAtUtc"];
    private static readonly string[] UserIdOccurredAtUtcColumns = ["UserId", "OccurredAtUtc"];
    private static readonly string[] UserIdRevokedAtUtcColumns = ["UserId", "RevokedAtUtc"];
    private static readonly string[] CompanyIdBranchIdColumns = ["CompanyId", "BranchId"];
    // Seeded from PetraERP.Application.Security.PermissionCatalog (see PermissionConfiguration).
    private static readonly object[] PermissionSeed =
    [
        "Audit.Security.View",
        "Ledger.Journals.Post",
        "Ledger.Journals.Reverse",
        "System.Roles.Manage",
        "System.Roles.View",
        "System.Users.Manage",
        "System.Users.View"
    ];

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(
            name: "security");

        migrationBuilder.CreateTable(
            name: "Permissions",
            schema: "security",
            columns: table => new
            {
                Code = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2")
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Permissions", x => x.Code);
                table.CheckConstraint("CK_Permissions_Code", "LEN([Code]) BETWEEN 3 AND 128 AND [Code] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^A-Za-z.]%'");
            });

        migrationBuilder.CreateTable(
            name: "Roles",
            schema: "security",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Code = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                NameArabic = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                NameEnglish = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Roles", x => x.Id);
                table.CheckConstraint("CK_Roles_Code", "LEN([Code]) BETWEEN 2 AND 64 AND [Code] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^A-Z0-9._-]%'");
                table.CheckConstraint("CK_Roles_Id", "[Id] <> '00000000-0000-0000-0000-000000000000'");
                table.CheckConstraint("CK_Roles_Names", "(DATALENGTH([NameArabic]) > 0 AND DATALENGTH([NameArabic]) = DATALENGTH(TRIM(NCHAR(9)+NCHAR(10)+NCHAR(11)+NCHAR(12)+NCHAR(13)+NCHAR(32)+NCHAR(133)+NCHAR(160)+NCHAR(5760)+NCHAR(8192)+NCHAR(8193)+NCHAR(8194)+NCHAR(8195)+NCHAR(8196)+NCHAR(8197)+NCHAR(8198)+NCHAR(8199)+NCHAR(8200)+NCHAR(8201)+NCHAR(8202)+NCHAR(8232)+NCHAR(8233)+NCHAR(8239)+NCHAR(8287)+NCHAR(12288) FROM [NameArabic]))) AND ([NameEnglish] IS NULL OR (DATALENGTH([NameEnglish]) > 0 AND DATALENGTH([NameEnglish]) = DATALENGTH(TRIM(NCHAR(9)+NCHAR(10)+NCHAR(11)+NCHAR(12)+NCHAR(13)+NCHAR(32)+NCHAR(133)+NCHAR(160)+NCHAR(5760)+NCHAR(8192)+NCHAR(8193)+NCHAR(8194)+NCHAR(8195)+NCHAR(8196)+NCHAR(8197)+NCHAR(8198)+NCHAR(8199)+NCHAR(8200)+NCHAR(8201)+NCHAR(8202)+NCHAR(8232)+NCHAR(8233)+NCHAR(8239)+NCHAR(8287)+NCHAR(12288) FROM [NameEnglish]))))");
                table.CheckConstraint("CK_Roles_Utc", "DATEPART(TZOFFSET, [CreatedAtUtc]) = 0 AND DATEPART(TZOFFSET, [UpdatedAtUtc]) = 0");
            });

        migrationBuilder.CreateTable(
            name: "SecurityAuditEvents",
            schema: "security",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                OccurredAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false),
                EventType = table.Column<byte>(type: "tinyint", nullable: false),
                UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                UserNameSnapshot = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                SessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                BranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                IpAddress = table.Column<string>(type: "nvarchar(45)", maxLength: 45, nullable: true),
                UserAgent = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                CorrelationId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                Details = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_SecurityAuditEvents", x => x.Id);
                table.CheckConstraint("CK_SecurityAuditEvents_EventType", "[EventType] BETWEEN 1 AND 25");
                table.CheckConstraint("CK_SecurityAuditEvents_Id", "[Id] <> '00000000-0000-0000-0000-000000000000'");
                table.CheckConstraint("CK_SecurityAuditEvents_Utc", "DATEPART(TZOFFSET, [OccurredAtUtc]) = 0");
            });

        migrationBuilder.CreateTable(
            name: "Users",
            schema: "security",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                UserName = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                NormalizedUserName = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                DisplayNameArabic = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                DisplayNameEnglish = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                PasswordAlgorithm = table.Column<byte>(type: "tinyint", nullable: false),
                PasswordIterations = table.Column<int>(type: "int", nullable: false),
                PasswordSalt = table.Column<byte[]>(type: "binary(32)", nullable: false),
                PasswordHash = table.Column<byte[]>(type: "binary(64)", nullable: false),
                PasswordChangedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false),
                MustChangePassword = table.Column<bool>(type: "bit", nullable: false),
                IsActive = table.Column<bool>(type: "bit", nullable: false),
                AccessFailedCount = table.Column<int>(type: "int", nullable: false),
                LockoutEndUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: true),
                LastLoginAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: true),
                SecurityStamp = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Users", x => x.Id);
                table.CheckConstraint("CK_Users_AccessFailedCount", "[AccessFailedCount] >= 0");
                table.CheckConstraint("CK_Users_DisplayNames", "(DATALENGTH([DisplayNameArabic]) > 0 AND DATALENGTH([DisplayNameArabic]) = DATALENGTH(TRIM(NCHAR(9)+NCHAR(10)+NCHAR(11)+NCHAR(12)+NCHAR(13)+NCHAR(32)+NCHAR(133)+NCHAR(160)+NCHAR(5760)+NCHAR(8192)+NCHAR(8193)+NCHAR(8194)+NCHAR(8195)+NCHAR(8196)+NCHAR(8197)+NCHAR(8198)+NCHAR(8199)+NCHAR(8200)+NCHAR(8201)+NCHAR(8202)+NCHAR(8232)+NCHAR(8233)+NCHAR(8239)+NCHAR(8287)+NCHAR(12288) FROM [DisplayNameArabic]))) AND ([DisplayNameEnglish] IS NULL OR (DATALENGTH([DisplayNameEnglish]) > 0 AND DATALENGTH([DisplayNameEnglish]) = DATALENGTH(TRIM(NCHAR(9)+NCHAR(10)+NCHAR(11)+NCHAR(12)+NCHAR(13)+NCHAR(32)+NCHAR(133)+NCHAR(160)+NCHAR(5760)+NCHAR(8192)+NCHAR(8193)+NCHAR(8194)+NCHAR(8195)+NCHAR(8196)+NCHAR(8197)+NCHAR(8198)+NCHAR(8199)+NCHAR(8200)+NCHAR(8201)+NCHAR(8202)+NCHAR(8232)+NCHAR(8233)+NCHAR(8239)+NCHAR(8287)+NCHAR(12288) FROM [DisplayNameEnglish]))))");
                table.CheckConstraint("CK_Users_Id", "[Id] <> '00000000-0000-0000-0000-000000000000'");
                table.CheckConstraint("CK_Users_NormalizedUserName", "[NormalizedUserName] = UPPER([UserName])");
                table.CheckConstraint("CK_Users_PasswordAlgorithm", "[PasswordAlgorithm] = 1");
                table.CheckConstraint("CK_Users_PasswordIterations", "[PasswordIterations] >= 100000");
                table.CheckConstraint("CK_Users_PasswordMaterial", "DATALENGTH([PasswordSalt]) = 32 AND DATALENGTH([PasswordHash]) = 64");
                table.CheckConstraint("CK_Users_SecurityStamp", "[SecurityStamp] <> '00000000-0000-0000-0000-000000000000'");
                table.CheckConstraint("CK_Users_Timestamps", "DATEPART(TZOFFSET, [PasswordChangedAtUtc]) = 0 AND ([LockoutEndUtc] IS NULL OR DATEPART(TZOFFSET, [LockoutEndUtc]) = 0) AND ([LastLoginAtUtc] IS NULL OR DATEPART(TZOFFSET, [LastLoginAtUtc]) = 0)");
                table.CheckConstraint("CK_Users_UserName", "LEN([UserName]) BETWEEN 3 AND 64 AND [UserName] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^A-Za-z0-9._-]%'");
                table.CheckConstraint("CK_Users_Utc", "DATEPART(TZOFFSET, [CreatedAtUtc]) = 0 AND DATEPART(TZOFFSET, [UpdatedAtUtc]) = 0");
            });

        migrationBuilder.CreateTable(
            name: "RolePermissions",
            schema: "security",
            columns: table => new
            {
                RoleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                PermissionCode = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false, defaultValueSql: "SYSUTCDATETIME()")
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_RolePermissions", x => new { x.RoleId, x.PermissionCode });
                table.CheckConstraint("CK_RolePermissions_Utc", "DATEPART(TZOFFSET, [CreatedAtUtc]) = 0");
                table.ForeignKey(
                    name: "FK_RolePermissions_Permissions_PermissionCode",
                    column: x => x.PermissionCode,
                    principalSchema: "security",
                    principalTable: "Permissions",
                    principalColumn: "Code");
                table.ForeignKey(
                    name: "FK_RolePermissions_Roles_RoleId",
                    column: x => x.RoleId,
                    principalSchema: "security",
                    principalTable: "Roles",
                    principalColumn: "Id");
            });

        migrationBuilder.CreateTable(
            name: "BootstrapState",
            schema: "security",
            columns: table => new
            {
                Id = table.Column<byte>(type: "tinyint", nullable: false),
                AdministratorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CompletedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_BootstrapState", x => x.Id);
                table.CheckConstraint("CK_BootstrapState_Singleton", "[Id] = 1");
                table.CheckConstraint("CK_BootstrapState_Utc", "DATEPART(TZOFFSET, [CompletedAtUtc]) = 0");
                table.ForeignKey(
                    name: "FK_BootstrapState_Users_AdministratorUserId",
                    column: x => x.AdministratorUserId,
                    principalSchema: "security",
                    principalTable: "Users",
                    principalColumn: "Id");
            });

        migrationBuilder.CreateTable(
            name: "Sessions",
            schema: "security",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ClientKind = table.Column<byte>(type: "tinyint", nullable: false),
                DeviceName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                UserSecurityStamp = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false),
                LastRefreshedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false),
                ExpiresAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false),
                RevokedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: true),
                RevocationReason = table.Column<byte>(type: "tinyint", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Sessions", x => x.Id);
                table.CheckConstraint("CK_Sessions_ClientKind", "[ClientKind] IN (1, 2, 3, 4)");
                table.CheckConstraint("CK_Sessions_Id", "[Id] <> '00000000-0000-0000-0000-000000000000'");
                table.CheckConstraint("CK_Sessions_Lifetime", "[ExpiresAtUtc] > [CreatedAtUtc] AND [LastRefreshedAtUtc] >= [CreatedAtUtc]");
                table.CheckConstraint("CK_Sessions_Revocation", "([RevokedAtUtc] IS NULL AND [RevocationReason] IS NULL) OR ([RevokedAtUtc] IS NOT NULL AND [RevocationReason] BETWEEN 1 AND 8)");
                table.CheckConstraint("CK_Sessions_SecurityStamp", "[UserSecurityStamp] <> '00000000-0000-0000-0000-000000000000'");
                table.CheckConstraint("CK_Sessions_Utc", "DATEPART(TZOFFSET, [CreatedAtUtc]) = 0 AND DATEPART(TZOFFSET, [LastRefreshedAtUtc]) = 0 AND DATEPART(TZOFFSET, [ExpiresAtUtc]) = 0 AND ([RevokedAtUtc] IS NULL OR DATEPART(TZOFFSET, [RevokedAtUtc]) = 0)");
                table.ForeignKey(
                    name: "FK_Sessions_Users_UserId",
                    column: x => x.UserId,
                    principalSchema: "security",
                    principalTable: "Users",
                    principalColumn: "Id");
            });

        migrationBuilder.CreateTable(
            name: "UserCompanyScopes",
            schema: "security",
            columns: table => new
            {
                UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false, defaultValueSql: "SYSUTCDATETIME()")
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_UserCompanyScopes", x => new { x.UserId, x.CompanyId });
                table.CheckConstraint("CK_UserCompanyScopes_Utc", "DATEPART(TZOFFSET, [CreatedAtUtc]) = 0");
                table.ForeignKey(
                    name: "FK_UserCompanyScopes_Companies_CompanyId",
                    column: x => x.CompanyId,
                    principalSchema: "system",
                    principalTable: "Companies",
                    principalColumn: "Id");
                table.ForeignKey(
                    name: "FK_UserCompanyScopes_Users_UserId",
                    column: x => x.UserId,
                    principalSchema: "security",
                    principalTable: "Users",
                    principalColumn: "Id");
            });

        migrationBuilder.CreateTable(
            name: "UserRoles",
            schema: "security",
            columns: table => new
            {
                UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                RoleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false, defaultValueSql: "SYSUTCDATETIME()")
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_UserRoles", x => new { x.UserId, x.RoleId });
                table.CheckConstraint("CK_UserRoles_Utc", "DATEPART(TZOFFSET, [CreatedAtUtc]) = 0");
                table.ForeignKey(
                    name: "FK_UserRoles_Roles_RoleId",
                    column: x => x.RoleId,
                    principalSchema: "security",
                    principalTable: "Roles",
                    principalColumn: "Id");
                table.ForeignKey(
                    name: "FK_UserRoles_Users_UserId",
                    column: x => x.UserId,
                    principalSchema: "security",
                    principalTable: "Users",
                    principalColumn: "Id");
            });

        migrationBuilder.CreateTable(
            name: "RefreshTokens",
            schema: "security",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                SessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                TokenHash = table.Column<byte[]>(type: "binary(32)", nullable: false),
                IssuedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false),
                ExpiresAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false),
                ConsumedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: true),
                ReplacedByTokenId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_RefreshTokens", x => x.Id);
                table.CheckConstraint("CK_RefreshTokens_Consumption", "([ConsumedAtUtc] IS NULL AND [ReplacedByTokenId] IS NULL) OR ([ConsumedAtUtc] IS NOT NULL)");
                table.CheckConstraint("CK_RefreshTokens_Id", "[Id] <> '00000000-0000-0000-0000-000000000000'");
                table.CheckConstraint("CK_RefreshTokens_Lifetime", "[ExpiresAtUtc] > [IssuedAtUtc]");
                table.CheckConstraint("CK_RefreshTokens_TokenHash", "DATALENGTH([TokenHash]) = 32");
                table.CheckConstraint("CK_RefreshTokens_Utc", "DATEPART(TZOFFSET, [IssuedAtUtc]) = 0 AND DATEPART(TZOFFSET, [ExpiresAtUtc]) = 0 AND ([ConsumedAtUtc] IS NULL OR DATEPART(TZOFFSET, [ConsumedAtUtc]) = 0)");
                table.ForeignKey(
                    name: "FK_RefreshTokens_Sessions_SessionId",
                    column: x => x.SessionId,
                    principalSchema: "security",
                    principalTable: "Sessions",
                    principalColumn: "Id");
            });

        migrationBuilder.CreateTable(
            name: "UserBranchScopes",
            schema: "security",
            columns: table => new
            {
                UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                BranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false, defaultValueSql: "SYSUTCDATETIME()")
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_UserBranchScopes", x => new { x.UserId, x.CompanyId, x.BranchId });
                table.CheckConstraint("CK_UserBranchScopes_Utc", "DATEPART(TZOFFSET, [CreatedAtUtc]) = 0");
                table.ForeignKey(
                    name: "FK_UserBranchScopes_Branches_CompanyId_BranchId",
                    columns: x => new { x.CompanyId, x.BranchId },
                    principalSchema: "system",
                    principalTable: "Branches",
                    principalColumns: CompanyIdIdColumns);
                table.ForeignKey(
                    name: "FK_UserBranchScopes_UserCompanyScopes_UserId_CompanyId",
                    columns: x => new { x.UserId, x.CompanyId },
                    principalSchema: "security",
                    principalTable: "UserCompanyScopes",
                    principalColumns: UserIdCompanyIdColumns);
            });

        migrationBuilder.InsertData(
            schema: "security",
            table: "Permissions",
            column: "Code",
            values: PermissionSeed);

        migrationBuilder.CreateIndex(
            name: "IX_BootstrapState_AdministratorUserId",
            schema: "security",
            table: "BootstrapState",
            column: "AdministratorUserId");

        migrationBuilder.CreateIndex(
            name: "IX_RefreshTokens_SessionId",
            schema: "security",
            table: "RefreshTokens",
            column: "SessionId");

        migrationBuilder.CreateIndex(
            name: "IX_RefreshTokens_TokenHash",
            schema: "security",
            table: "RefreshTokens",
            column: "TokenHash",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_RolePermissions_PermissionCode",
            schema: "security",
            table: "RolePermissions",
            column: "PermissionCode");

        migrationBuilder.CreateIndex(
            name: "IX_Roles_Code",
            schema: "security",
            table: "Roles",
            column: "Code",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_SecurityAuditEvents_EventType_OccurredAtUtc",
            schema: "security",
            table: "SecurityAuditEvents",
            columns: EventTypeOccurredAtUtcColumns);

        migrationBuilder.CreateIndex(
            name: "IX_SecurityAuditEvents_OccurredAtUtc",
            schema: "security",
            table: "SecurityAuditEvents",
            column: "OccurredAtUtc");

        migrationBuilder.CreateIndex(
            name: "IX_SecurityAuditEvents_UserId_OccurredAtUtc",
            schema: "security",
            table: "SecurityAuditEvents",
            columns: UserIdOccurredAtUtcColumns);

        migrationBuilder.CreateIndex(
            name: "IX_Sessions_UserId_RevokedAtUtc",
            schema: "security",
            table: "Sessions",
            columns: UserIdRevokedAtUtcColumns);

        migrationBuilder.CreateIndex(
            name: "IX_UserBranchScopes_CompanyId_BranchId",
            schema: "security",
            table: "UserBranchScopes",
            columns: CompanyIdBranchIdColumns);

        migrationBuilder.CreateIndex(
            name: "IX_UserCompanyScopes_CompanyId",
            schema: "security",
            table: "UserCompanyScopes",
            column: "CompanyId");

        migrationBuilder.CreateIndex(
            name: "IX_UserRoles_RoleId",
            schema: "security",
            table: "UserRoles",
            column: "RoleId");

        migrationBuilder.CreateIndex(
            name: "IX_Users_NormalizedUserName",
            schema: "security",
            table: "Users",
            column: "NormalizedUserName",
            unique: true);

        // Reviewed addition: the security audit trail is append-only at the database level (defense in depth).
        migrationBuilder.Sql("""
                EXEC(N'CREATE TRIGGER [security].[TR_SecurityAuditEvents_AppendOnly]
                ON [security].[SecurityAuditEvents] INSTEAD OF UPDATE, DELETE AS
                BEGIN
                    SET NOCOUNT ON;
                    THROW 51001, ''Security audit events are append-only and cannot be updated or deleted.'', 1;
                END;');
                """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP TRIGGER IF EXISTS [security].[TR_SecurityAuditEvents_AppendOnly];");
        migrationBuilder.DropTable(
            name: "BootstrapState",
            schema: "security");

        migrationBuilder.DropTable(
            name: "RefreshTokens",
            schema: "security");

        migrationBuilder.DropTable(
            name: "RolePermissions",
            schema: "security");

        migrationBuilder.DropTable(
            name: "SecurityAuditEvents",
            schema: "security");

        migrationBuilder.DropTable(
            name: "UserBranchScopes",
            schema: "security");

        migrationBuilder.DropTable(
            name: "UserRoles",
            schema: "security");

        migrationBuilder.DropTable(
            name: "Sessions",
            schema: "security");

        migrationBuilder.DropTable(
            name: "Permissions",
            schema: "security");

        migrationBuilder.DropTable(
            name: "UserCompanyScopes",
            schema: "security");

        migrationBuilder.DropTable(
            name: "Roles",
            schema: "security");

        migrationBuilder.DropTable(
            name: "Users",
            schema: "security");
    }
}
